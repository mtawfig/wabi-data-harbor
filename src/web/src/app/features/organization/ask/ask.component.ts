import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { AskFollowUp, AskResponse, AskableApplication, ClarifyingQuestion } from '../../../core/platform/ai.models';
import { AiService } from '../../../core/platform/ai.service';
import { problemMessage } from '../../../core/problem-message';
import { AiSpending, PlansService } from '../../../core/plans/plans.service';
import { WiNumberPipe } from '../../../shared/format/locale-pipes';
import { AnswerFeedbackComponent } from '../../../shared/analysis/answer-feedback.component';
import { AnalysisAnswerComponent } from '../../../shared/analysis/analysis-answer.component';

interface Rejection {
  stage: string;
  codes: string[];
}

/**
 * "Ask Wabi Insight" (specification §19): question in, verified answer out. An answer is one or more panels
 * (KPI, chart, table), each the result of its own validated, authorized, read-only query. The page lays
 * them out as a dashboard: narrative first, headline numbers, charts, then the detail tables, and
 * "How this was computed" with the redacted SQL of every query. History and sharing arrive with P9.
 */
/** An example question chip: administrator-written text, or a key of the platform's built-in examples. */
interface ExampleChip {
  text?: string;
  key?: string;
}

@Component({
  selector: 'app-ask',
  imports: [WiNumberPipe, FormsModule, RouterLink, MatButtonModule, MatChipsModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, TranslocoModule, AnalysisAnswerComponent, AnswerFeedbackComponent],
  templateUrl: './ask.component.html',
  styleUrl: './ask.component.scss',
})
export class AskComponent {
  readonly auth = inject(AuthService);
  private readonly ai = inject(AiService);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  private readonly plans = inject(PlansService);
  /** The organization against its plan's monthly AI spending limit: shown when it is near or reached. */
  readonly spending = signal<AiSpending | null>(null);
  readonly applications = signal<AskableApplication[]>([]);
  readonly applicationsLoaded = signal(false);
  readonly selectedApplication = signal<string | null>(null);
  readonly outputLanguage = signal<'same' | 'en' | 'ar'>('same');
  readonly prompt = signal('');
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly rejection = signal<Rejection | null>(null);
  readonly answer = signal<AskResponse | null>(null);
  /** The platform's question back to the asker, while it waits for an answer (P13.54). It takes the place of the sample questions. */
  readonly clarification = signal<ClarifyingQuestion | null>(null);
  /** What the asker answered, shown under the question box once the request has gone on. */
  readonly clarified = signal<string | null>(null);
  /** The asker's own words instead of one of the suggested answers. */
  readonly ownAnswer = signal('');
  /** The last question asked and the request it belongs to, so "Change" can bring the choices back. */
  private lastClarification: { question: string; clarification: ClarifyingQuestion } | null = null;
  readonly application = computed(() => this.applications().find((a) => a.applicationId === this.selectedApplication()) ?? null);

  /**
   * Suggestions for the chosen application: the questions its administrators wrote, in the interface language (else the
   * other one). The built-in Platform Insights application falls back to the platform's own examples; a business
   * application without examples shows none, because only its organization knows what its data can answer.
   */
  readonly exampleChips = computed<ExampleChip[]>(() => {
    const app = this.application();
    if (!app) {
      return [];
    }
    const arabic = this.language.current() === 'ar';
    const own = (app.exampleQuestions ?? [])
      .map((e) => (arabic ? e.ar || e.en : e.en || e.ar)?.trim())
      .filter((text): text is string => !!text)
      .map((text) => ({ text }));
    if (own.length > 0) {
      return own;
    }
    return app.isBuiltIn ? ['e1', 'e2', 'e3'].map((key) => ({ key })) : [];
  });
  constructor() {
    this.ai.askableApplications().subscribe({
      next: (apps) => {
        this.applications.set(apps);
        this.applicationsLoaded.set(true);
        if (apps[0]) {
          this.selectedApplication.set(apps[0].applicationId);
        }
      },
      error: () => this.applicationsLoaded.set(true),
    });
    this.loadSpending();
  }

  private loadSpending(): void {
    // The band is extra information: the page works without it.
    this.plans.spending().subscribe({ next: (spending) => this.spending.set(spending), error: () => this.spending.set(null) });
  }

  /** "View all in Excel" of the answer just given: the export of its place in My History. */
  excelUrl(historyId: string, versionNumber: number): string {
    return `/api/history/${historyId}/versions/${versionNumber}/export/Xlsx`;
  }

  reportUrl(analysisId: string): string {
    return this.ai.reportUrl(analysisId);
  }

  appName(a: AskableApplication): string {
    return this.language.current() === 'ar' ? a.applicationNameAr : a.applicationName;
  }

  /** Editing the request while the question is open starts over: the choices belonged to the old wording. */
  onPrompt(value: string): void {
    this.prompt.set(value);
    if (this.lastClarification && value.trim() !== this.lastClarification.question) {
      this.clarification.set(null);
      this.clarified.set(null);
      this.lastClarification = null;
    }
  }

  /** One of the suggested answers, or the asker's own words: the request goes on with it. */
  answerClarification(answer: string): void {
    const text = answer.trim();
    if (!text || this.busy()) {
      return;
    }
    this.clarified.set(text);
    this.submit({ clarification: text });
  }

  /** "Answer anyway": the request is answered as understood, without a question. */
  skipClarification(): void {
    this.clarified.set(null);
    this.submit({ skipClarification: true });
  }

  cancelClarification(): void {
    this.clarification.set(null);
  }

  /** "Change": the choices of the last question again. */
  changeClarification(): void {
    if (this.lastClarification && !this.busy()) {
      this.clarified.set(null);
      this.ownAnswer.set('');
      this.clarification.set(this.lastClarification.clarification);
      this.focusClarification();
    }
  }

  private focusClarification(): void {
    // After the panel is drawn, focus moves to it: a screen reader announces the question and the next Tab is the first
    // choice. The panel itself takes the focus, not a choice, so none of them looks chosen.
    setTimeout(() => document.querySelector<HTMLElement>('[data-testid="ask-clarify"]')?.focus());
  }

  useExample(chip: ExampleChip): void {
    if (this.busy()) {
      return;
    }
    this.onPrompt(chip.key ? this.transloco.translate(`ask.examples.${chip.key}`) : (chip.text ?? ''));
  }

  submit(followUp: AskFollowUp = {}): void {
    const applicationId = this.selectedApplication();
    const question = this.prompt().trim();
    if (!applicationId || !question || this.busy()) {
      return;
    }
    const chosen = this.outputLanguage() === 'same' ? this.language.current() : this.outputLanguage();
    const language: 'en' | 'ar' = chosen === 'ar' ? 'ar' : 'en';
    this.busy.set(true);
    this.error.set(null);
    this.rejection.set(null);
    this.answer.set(null);
    this.clarification.set(null);
    this.ownAnswer.set('');
    if (!followUp.clarification) {
      this.clarified.set(null);
    }
    this.ai.ask(applicationId, question, language, followUp).subscribe({
      next: (a) => {
        this.busy.set(false);
        if (a.clarification) {
          // Too open to answer: the page asks first. Nothing was run or saved yet.
          this.lastClarification = { question, clarification: a.clarification };
          this.clarification.set(a.clarification);
          this.focusClarification();
          return;
        }
        this.answer.set(a);
      },
      error: (e: HttpErrorResponse) => {
        this.busy.set(false);
        if (e.status === 422 && e.error?.stage) {
          this.rejection.set({ stage: e.error.stage, codes: [...new Set<string>((e.error.errors ?? []).map((x: { code: string }) => x.code))] });
        } else if (e.status === 502 && e.error?.code) {
          this.error.set(this.transloco.translate('admin.errors.' + e.error.code));
        } else {
          this.error.set(problemMessage(e, this.transloco));
        }
      },
    });
  }
}
