import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
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
import { CollectionSummary, ExportFormat, HistoryDetail, HistoryVersion } from '../../../core/history/history.models';
import { LanguageService } from '../../../core/i18n/language.service';
import { AskResponse } from '../../../core/platform/ai.models';
import { problemMessage } from '../../../core/problem-message';
import { AnswerFeedbackComponent } from '../../../shared/analysis/answer-feedback.component';
import { AnalysisAnswerComponent } from '../../../shared/analysis/analysis-answer.component';
import { EmailDialogComponent, EmailDialogData } from '../sharing/email-dialog.component';
import { ShareDialogComponent, ShareDialogData } from '../sharing/share-dialog.component';
import { WiDatePipe, WiNumberPipe } from '../../../shared/format/locale-pipes';

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
  imports: [FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatMenuModule, MatProgressBarModule, MatSelectModule, TranslocoModule, AnalysisAnswerComponent, AnswerFeedbackComponent, WiDatePipe, WiNumberPipe],
  templateUrl: './history-detail.component.html',
  styleUrl: './history-detail.component.scss',
})
export class HistoryDetailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly history = inject(HistoryService);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly dialog = inject(MatDialog);
  readonly language = inject(LanguageService);

  readonly historyId = this.route.snapshot.paramMap.get('id')!;
  readonly detail = signal<HistoryDetail | null>(null);
  readonly versionNumber = signal<number | null>(null);
  readonly answer = signal<AskResponse | null>(null);
  readonly loading = signal(true);
  readonly loadingAnswer = signal(false);
  readonly rerunning = signal(false);
  readonly exporting = signal(false);
  readonly error = signal<string | null>(null);
  readonly rejection = signal<Rejection | null>(null);
  readonly editingTitle = signal(false);
  readonly titleDraft = signal('');
  readonly collections = signal<CollectionSummary[]>([]);

  readonly version = computed<HistoryVersion | null>(() => this.detail()?.versions.find((v) => v.versionNumber === this.versionNumber()) ?? null);
  readonly reportUrl = computed(() => this.history.versionReportUrl(this.historyId, this.versionNumber() ?? 0));
  /** "View all in Excel" under a long table: the Excel export of the version on screen. */
  readonly excelUrl = computed(() => (this.versionNumber() ? `/api/history/${this.historyId}/versions/${this.versionNumber()}/export/Xlsx` : null));
  readonly memberOf = computed(() => new Set(this.detail()?.collections.map((c) => c.collectionId) ?? []));

  constructor() {
    this.load();
    this.history.collections().subscribe({ next: (c) => this.collections.set(c), error: () => undefined });
  }

  /** Loads the analysis; after a failed re-run the message stays on screen (`keepError`) while the versions refresh. */
  load(select?: number, keepError = false): void {
    this.loading.set(true);
    if (!keepError) {
      this.error.set(null);
    }
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
        this.load(undefined, true);
      },
    });
  }

  /** Downloads the rendered file and refreshes the version list (the export now appears in My Reports). */
  exportAs(format: ExportFormat): void {
    const version = this.versionNumber();
    if (!version || this.exporting()) {
      return;
    }
    this.exporting.set(true);
    this.history.export(this.historyId, version, format).subscribe({
      next: (blob) => {
        this.exporting.set(false);
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `${this.detail()?.summary.title ?? 'analysis'}-v${version}.${format.toLowerCase()}`;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 10_000);
        this.snack.open(this.transloco.translate('history.exported', { format: this.transloco.translate('reports.formats.' + format) }), undefined, { duration: 3000 });
      },
      error: (e: HttpErrorResponse) => {
        this.exporting.set(false);
        this.error.set(problemMessage(e, this.transloco));
      },
    });
  }

  /** Share with colleagues (VIEW, expiry, revocation). */
  openShare(): void {
    const d = this.detail();
    if (!d) return;
    this.dialog.open<ShareDialogComponent, ShareDialogData, boolean>(ShareDialogComponent, { data: { historyId: this.historyId, title: d.summary.title }, autoFocus: 'input', width: '640px', maxWidth: '95vw' });
  }

  /** Email this version: owner's text (the AI draft when there is one), PDF attached, classification gate applied by the API. */
  openEmail(): void {
    const d = this.detail();
    const version = this.version();
    if (!d || !version?.hasResult) return;
    const draft = this.answer()?.emailDraft;
    this.dialog
      .open<EmailDialogComponent, EmailDialogData>(EmailDialogComponent, { data: { historyId: this.historyId, versionNumber: version.versionNumber, title: d.summary.title, classification: d.summary.dataClassification, hasReport: version.hasReport, draft: draft ? { subject: draft.subject, body: draft.body } : null }, autoFocus: 'textarea', width: '700px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.snack.open(this.transloco.translate('sharing.email.sent', { count: result.recipients, external: result.externalRecipients }), undefined, { duration: 5000 });
        }
      });
  }

  openPrint(): void {
    window.open(`/print/history/${this.historyId}/${this.versionNumber() ?? 1}`, '_blank', 'noopener');
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
