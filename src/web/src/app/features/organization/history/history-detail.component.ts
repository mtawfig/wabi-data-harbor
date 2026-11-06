import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { HistoryService } from '../../../core/history/history.service';
import { CollectionSummary, HistoryDetail, HistoryVersion } from '../../../core/history/history.models';
import { LanguageService } from '../../../core/i18n/language.service';
import { AskResponse } from '../../../core/platform/ai.models';
import { problemMessage } from '../../../core/problem-message';
import { AnalysisAnswerComponent } from '../../../shared/analysis/analysis-answer.component';

interface Rejection {
  stage: string;
  codes: string[];
}

/**
 * A saved analysis (specification §23): the stored answer of the selected version (never re-queried), the
 * version list, re-run under current permissions, rename, favorite, archive, delete and collections.
 */
@Component({
  selector: 'app-history-detail',
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatMenuModule, MatProgressBarModule, MatSelectModule, TranslocoModule, AnalysisAnswerComponent],
  templateUrl: './history-detail.component.html',
  styleUrl: './history-detail.component.scss',
})
export class HistoryDetailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly history = inject(HistoryService);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly historyId = this.route.snapshot.paramMap.get('id')!;
  readonly detail = signal<HistoryDetail | null>(null);
  readonly versionNumber = signal<number | null>(null);
  readonly answer = signal<AskResponse | null>(null);
  readonly loading = signal(true);
  readonly loadingAnswer = signal(false);
  readonly rerunning = signal(false);
  readonly error = signal<string | null>(null);
  readonly rejection = signal<Rejection | null>(null);
  readonly editingTitle = signal(false);
  readonly titleDraft = signal('');
  readonly collections = signal<CollectionSummary[]>([]);

  readonly version = computed<HistoryVersion | null>(() => this.detail()?.versions.find((v) => v.versionNumber === this.versionNumber()) ?? null);
  readonly reportUrl = computed(() => this.history.versionReportUrl(this.historyId, this.versionNumber() ?? 0));
  readonly memberOf = computed(() => new Set(this.detail()?.collections.map((c) => c.collectionId) ?? []));

  constructor() {
    this.load();
    this.history.collections().subscribe({ next: (c) => this.collections.set(c), error: () => undefined });
  }

  load(select?: number): void {
    this.loading.set(true);
    this.error.set(null);
    this.history.get(this.historyId).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
        this.selectVersion(select ?? d.summary.currentVersionNumber);
      },
      error: (e: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(e.status === 404 ? this.transloco.translate('history.notFound') : problemMessage(e, this.transloco));
      },
    });
  }

  selectVersion(versionNumber: number): void {
    this.versionNumber.set(versionNumber);
    this.answer.set(null);
    const version = this.version();
    if (!version?.hasResult) {
      return;
    }
    this.loadingAnswer.set(true);
    this.history.versionResult(this.historyId, versionNumber).subscribe({
      next: (a) => {
        this.answer.set(a);
        this.loadingAnswer.set(false);
      },
      error: (e: HttpErrorResponse) => {
        this.loadingAnswer.set(false);
        this.error.set(problemMessage(e, this.transloco));
      },
    });
  }

  rerun(): void {
    if (this.rerunning()) {
      return;
    }
    this.rerunning.set(true);
    this.error.set(null);
    this.rejection.set(null);
    this.history.rerun(this.historyId).subscribe({
      next: (a) => {
        this.rerunning.set(false);
        this.snack.open(this.transloco.translate('history.rerunDone', { version: a.versionNumber }), undefined, { duration: 3000 });
        this.load(a.versionNumber);
      },
      error: (e: HttpErrorResponse) => {
        this.rerunning.set(false);
        if (e.status === 422 && e.error?.stage) {
          this.rejection.set({ stage: e.error.stage, codes: [...new Set<string>((e.error.errors ?? []).map((x: { code: string }) => x.code))] });
        } else if (e.status === 502 && e.error?.code) {
          this.error.set(this.transloco.translate('admin.errors.' + e.error.code));
        } else {
          this.error.set(problemMessage(e, this.transloco));
        }
        this.load();
      },
    });
  }

  startRename(): void {
    this.titleDraft.set(this.detail()?.summary.title ?? '');
    this.editingTitle.set(true);
  }

  saveTitle(): void {
    const title = this.titleDraft().trim();
    if (!title) {
      return;
    }
    this.history.rename(this.historyId, title).subscribe({
      next: (s) => {
        this.detail.update((d) => (d ? { ...d, summary: s } : d));
        this.editingTitle.set(false);
      },
      error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)),
    });
  }

  toggleFavorite(): void {
    const s = this.detail()?.summary;
    if (!s) return;
    this.history.setFavorite(this.historyId, !s.isFavorite).subscribe({ next: (u) => this.detail.update((d) => (d ? { ...d, summary: u } : d)), error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)) });
  }

  toggleArchive(): void {
    const s = this.detail()?.summary;
    if (!s) return;
    this.history.setArchived(this.historyId, !s.isArchived).subscribe({ next: (u) => this.detail.update((d) => (d ? { ...d, summary: u } : d)), error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)) });
  }

  remove(): void {
    const s = this.detail()?.summary;
    if (!s || !confirm(this.transloco.translate('history.confirmDelete', { title: s.title }))) {
      return;
    }
    this.history.delete(this.historyId).subscribe({ next: () => void this.router.navigate(['/app/history']), error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)) });
  }

  toggleCollection(c: CollectionSummary): void {
    const call = this.memberOf().has(c.collectionId) ? this.history.removeFromCollection(c.collectionId, this.historyId) : this.history.addToCollection(c.collectionId, this.historyId);
    call.subscribe({ next: () => this.load(this.versionNumber() ?? undefined), error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)) });
  }
}
