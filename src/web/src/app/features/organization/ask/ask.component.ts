import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import type { EChartsOption } from 'echarts';
import { NgxEchartsDirective } from 'ngx-echarts';
import { AuthService } from '../../../core/auth/auth.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { AskResponse, AskableApplication } from '../../../core/platform/ai.models';
import { AiService } from '../../../core/platform/ai.service';
import { problemMessage } from '../../../core/problem-message';
import { baseChartOption } from '../../../shared/ui/chart-theme';

interface Rejection {
  stage: string;
  codes: string[];
}

/**
 * "Ask Wabi Insight" (specification §19): question in, verified answer out. The page shows the narrative,
 * deterministic KPIs, the chart the plan suggested, the rows, and how the answer was computed (objective,
 * assumptions, the redacted SQL preview and the token cost). History and sharing arrive with P9.
 */
@Component({
  selector: 'app-ask',
  imports: [DecimalPipe, FormsModule, MatButtonModule, MatChipsModule, MatExpansionModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, TranslocoModule, NgxEchartsDirective],
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
  readonly kpiValue = computed(() => {
    const a = this.answer();
    if (!a || a.plan.visualization?.type !== 'kpi' || a.result.rows.length === 0) {
      return null;
    }
    const numeric = a.result.columns.findIndex((c) => c.isNumeric);
    return numeric >= 0 ? a.result.rows[0][numeric] : a.result.rows[0][0];
  });
  readonly chart = computed<EChartsOption | null>(() => {
    const a = this.answer();
    const type = a?.plan.visualization?.type;
    if (!a || !type || type === 'table' || type === 'kpi' || a.result.series.length === 0) {
      return null;
    }
    const base = baseChartOption(this.language.isRtl());
    if (type === 'pie') {
      const s = a.result.series[0];
      return { ...base, xAxis: undefined, yAxis: undefined, grid: undefined, tooltip: { trigger: 'item', backgroundColor: '#0B1F3A', borderWidth: 0, textStyle: { color: '#F5F8FC' } }, series: [{ type: 'pie', radius: ['35%', '70%'], data: s.categories.map((c, i) => ({ name: c, value: s.values[i] })) }] };
    }
    return { ...base, xAxis: { ...(base.xAxis as object), data: a.result.series[0].categories }, series: a.result.series.map((s) => ({ name: s.name, type: type === 'line' ? 'line' : 'bar', smooth: type === 'line', showSymbol: false, areaStyle: type === 'line' ? { opacity: 0.08 } : undefined, barMaxWidth: 36, data: s.values })) };
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
          this.rejection.set({ stage: e.error.stage, codes: (e.error.errors ?? []).map((x: { code: string }) => x.code) });
        } else if (e.status === 502 && e.error?.code) {
          this.error.set(this.transloco.translate('admin.errors.' + e.error.code));
        } else {
          this.error.set(problemMessage(e, this.transloco));
        }
      },
    });
  }

  format(value: string | number | boolean | null, isNumeric: boolean): string {
    if (value === null || value === undefined) {
      return '—';
    }
    if (isNumeric && typeof value === 'number') {
      return value.toLocaleString(this.language.current() === 'ar' ? 'ar-EG' : 'en-US', { maximumFractionDigits: 2 });
    }
    return String(value);
  }
}
