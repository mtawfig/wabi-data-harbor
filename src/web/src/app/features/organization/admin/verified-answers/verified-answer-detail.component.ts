import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { LanguageService } from '../../../../core/i18n/language.service';
import { AnalysisPanel, AskResponse } from '../../../../core/platform/ai.models';
import { problemMessage } from '../../../../core/problem-message';
import { VerifiedAnswerAction, VerifiedAnswerDetail } from '../../../../core/verified-answers/verified-answers.models';
import { VerifiedAnswersService } from '../../../../core/verified-answers/verified-answers.service';
import { AnalysisAnswerComponent } from '../../../../shared/analysis/analysis-answer.component';
import { PlanDescription, describePanel } from '../../../../shared/analysis/plan-description';
import { WiDatePipe, WiNumberPipe } from '../../../../shared/format/locale-pipes';

/**
 * One verified answer for review (P13.53): the question, what the asker said, how it was computed in plain words and,
 * when the administrator may read everything it uses, the result itself. Approve, reject, disable, enable or delete.
 */
@Component({
  selector: 'app-verified-answer-detail',
  imports: [FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, TranslocoModule, AnalysisAnswerComponent, WiDatePipe, WiNumberPipe],
  templateUrl: './verified-answer-detail.component.html',
  styleUrl: './verified-answers.scss',
})
export class VerifiedAnswerDetailComponent {
  private readonly answers = inject(VerifiedAnswersService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly language = inject(LanguageService);

  readonly id = this.route.snapshot.paramMap.get('id')!;
  readonly detail = signal<VerifiedAnswerDetail | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly confirmingDelete = signal(false);
  readonly note = signal('');
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  private readonly translation = toSignal(this.transloco.selectTranslation());

  /** The stored answer without the asker's own report and e-mail draft (those stay in the asker's history). */
  readonly result = computed<AskResponse | null>(() => {
    const result = this.detail()?.result;
    return result ? { ...result, report: null, emailDraft: null } : null;
  });

  /** "How this was computed" from the plan alone, when the result is not shown. */
  readonly descriptions = computed<PlanDescription[]>(() => {
    this.translation();
    const plan = this.detail()?.plan;
    const labels = this.detail()?.panelLabels ?? [];
    if (!plan) {
      return [];
    }
    const language = this.language.current();
    const empty = { columns: [], rows: [], rowCount: 0, truncated: false, kpis: [], series: [], durationMs: 0 };
    return plan.queries.map((query, i) => describePanel({ title: query.title, plan: query, result: empty, labels: labels[i] ?? {} } as AnalysisPanel, language, (key, params) => this.transloco.translate(key, params)));
  });

  readonly applicationName = computed(() => {
    const d = this.detail();
    return d ? (this.language.current() === 'ar' ? d.applicationNameAr || d.applicationName : d.applicationName) : '';
  });

  constructor() {
    this.answers.get(this.id).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.loading.set(false);
      },
      error: (failure: HttpErrorResponse) => {
        this.error.set(problemMessage(failure, this.transloco));
        this.loading.set(false);
      },
    });
  }

  can(action: VerifiedAnswerAction): boolean {
    const status = this.detail()?.status;
    switch (action) {
      case 'approve':
        return status === 'Pending' || status === 'Reported' || status === 'Rejected';
      case 'reject':
        return status === 'Pending' || status === 'Reported' || status === 'Approved';
      case 'disable':
        return status === 'Approved';
      case 'enable':
        return status === 'Disabled' || status === 'SetAside';
    }
  }

  review(action: VerifiedAnswerAction): void {
    this.saving.set(true);
    this.error.set(null);
    this.done.set(null);
    this.answers.review(this.id, action, this.note().trim() || null).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.note.set('');
        this.done.set(`verifiedAnswers.done.${action}`);
        this.saving.set(false);
      },
      error: (failure: HttpErrorResponse) => {
        this.error.set(problemMessage(failure, this.transloco));
        this.saving.set(false);
      },
    });
  }

  remove(): void {
    this.saving.set(true);
    this.answers.delete(this.id).subscribe({
      next: () => void this.router.navigate(['/app/admin/verified-answers']),
      error: (failure: HttpErrorResponse) => {
        this.error.set(problemMessage(failure, this.transloco));
        this.saving.set(false);
      },
    });
  }

  join(items: string[]): string {
    return items.join(this.language.current() === 'ar' ? '، ' : ', ');
  }

  reportUrl(): string {
    return '';
  }
}
