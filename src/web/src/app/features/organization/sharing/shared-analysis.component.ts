import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { HistoryVersion } from '../../../core/history/history.models';
import { LanguageService } from '../../../core/i18n/language.service';
import { AskResponse } from '../../../core/platform/ai.models';
import { problemMessage } from '../../../core/problem-message';
import { SharedAnalysisDetail } from '../../../core/sharing/sharing.models';
import { SharingService } from '../../../core/sharing/sharing.service';
import { AnalysisAnswerComponent } from '../../../shared/analysis/analysis-answer.component';
import { WiDatePipe, WiNumberPipe } from '../../../shared/format/locale-pipes';

/**
 * A shared analysis as its recipient sees it (specification §24): the owner's stored answer for any completed
 * version and the study PDF, read-only. No re-run, export, rename or delete; the source is never queried.
 */
@Component({
  selector: 'app-shared-analysis',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, TranslocoModule, AnalysisAnswerComponent, WiDatePipe, WiNumberPipe],
  templateUrl: './shared-analysis.component.html',
  styleUrl: './shared-analysis.component.scss',
})
export class SharedAnalysisComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly sharing = inject(SharingService);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly historyId = this.route.snapshot.paramMap.get('id')!;
  readonly detail = signal<SharedAnalysisDetail | null>(null);
  readonly versionNumber = signal<number | null>(null);
  readonly answer = signal<AskResponse | null>(null);
  readonly loading = signal(true);
  readonly loadingAnswer = signal(false);
  readonly error = signal<string | null>(null);

  readonly version = computed<HistoryVersion | null>(() => this.detail()?.versions.find((v) => v.versionNumber === this.versionNumber()) ?? null);
  readonly reportUrl = computed(() => this.sharing.sharedReportUrl(this.historyId, this.versionNumber() ?? 0));
  /** "View all in Excel" is offered only to a recipient who may read this data themselves; the server exports under their permissions. */
  readonly excelUrl = computed(() => (this.detail()?.canViewAllInExcel && this.versionNumber() ? `/api/shares/with-me/${this.historyId}/versions/${this.versionNumber()}/export/xlsx` : null));

  constructor() {
    this.sharing.shared(this.historyId).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
        const latest = d.versions[0]?.versionNumber;
        if (latest) this.selectVersion(latest);
      },
      error: (e: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(e.status === 404 ? this.transloco.translate('sharing.view.notFound') : problemMessage(e, this.transloco));
      },
    });
  }

  selectVersion(versionNumber: number): void {
    this.versionNumber.set(versionNumber);
    this.answer.set(null);
    if (!this.version()?.hasResult) return;
    this.loadingAnswer.set(true);
    this.sharing.sharedResult(this.historyId, versionNumber).subscribe({
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
}
