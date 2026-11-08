import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { HistoryService } from '../../../core/history/history.service';
import { HistoryDetail } from '../../../core/history/history.models';
import { LanguageService } from '../../../core/i18n/language.service';
import { AskResponse } from '../../../core/platform/ai.models';
import { problemMessage } from '../../../core/problem-message';
import { AnalysisAnswerComponent } from '../../../shared/analysis/analysis-answer.component';
import { WiDatePipe, WiNumberPipe } from '../../../shared/format/locale-pipes';

/** Print view (specification §23): the stored answer of a version on a plain white page without the shell, with a print button. */
@Component({
  selector: 'app-history-print',
  imports: [MatButtonModule, MatIconModule, TranslocoModule, AnalysisAnswerComponent, WiDatePipe, WiNumberPipe],
  templateUrl: './history-print.component.html',
  styleUrl: './history-print.component.scss',
})
export class HistoryPrintComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly history = inject(HistoryService);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly historyId = this.route.snapshot.paramMap.get('id')!;
  readonly versionNumber = Number(this.route.snapshot.paramMap.get('version') ?? '1');
  readonly detail = signal<HistoryDetail | null>(null);
  readonly answer = signal<AskResponse | null>(null);
  readonly error = signal<string | null>(null);
  readonly reportUrl = this.history.versionReportUrl(this.historyId, this.versionNumber);

  constructor() {
    this.history.get(this.historyId).subscribe({ next: (d) => this.detail.set(d), error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)) });
    this.history.versionResult(this.historyId, this.versionNumber).subscribe({ next: (a) => this.answer.set(a), error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)) });
  }

  print(): void {
    window.print();
  }
}
