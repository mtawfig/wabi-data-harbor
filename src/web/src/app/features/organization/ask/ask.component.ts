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
import { AskResponse, AskableApplication } from '../../../core/platform/ai.models';
import { AiService } from '../../../core/platform/ai.service';
import { problemMessage } from '../../../core/problem-message';
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
@Component({
  selector: 'app-ask',
  imports: [FormsModule, RouterLink, MatButtonModule, MatChipsModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, TranslocoModule, AnalysisAnswerComponent],
  templateUrl: './ask.component.html',
  styleUrl: './ask.component.scss',
})
export class AskComponent {
  readonly auth = inject(AuthService);
  private readonly ai = inject(AiService);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly applications = signal<AskableApplication[]>([]);
  readonly applicationsLoaded = signal(false);
  readonly selectedApplication = signal<string | null>(null);
  readonly outputLanguage = signal<'same' | 'en' | 'ar'>('same');
  readonly prompt = signal('');
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly rejection = signal<Rejection | null>(null);
  readonly answer = signal<AskResponse | null>(null);
  readonly examples = ['e1', 'e2', 'e3'];

  readonly application = computed(() => this.applications().find((a) => a.applicationId === this.selectedApplication()) ?? null);
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
  }

  reportUrl(analysisId: string): string {
    return this.ai.reportUrl(analysisId);
  }

  appName(a: AskableApplication): string {
    return this.language.current() === 'ar' ? a.applicationNameAr : a.applicationName;
  }

  useExample(key: string): void {
    this.prompt.set(this.transloco.translate(`ask.examples.${key}`));
  }

  submit(): void {
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
    this.ai.ask(applicationId, question, language).subscribe({
      next: (a) => {
        this.answer.set(a);
        this.busy.set(false);
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
