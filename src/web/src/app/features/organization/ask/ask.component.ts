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
import { AnalysisPanel, AskResponse, AskableApplication, EmailDraft } from '../../../core/platform/ai.models';
import { AiService } from '../../../core/platform/ai.service';
import { problemMessage } from '../../../core/problem-message';
import { baseChartOption } from '../../../shared/ui/chart-theme';

interface Rejection {
  stage: string;
  codes: string[];
}

interface KpiCard {
  title: string;
  value: string | number | boolean | null;
  column: string;
}

interface ChartCard {
  index: number;
  title: string;
  option: EChartsOption;
}

/**
 * "Ask Wabi Insight" (specification §19): question in, verified answer out. An answer is one or more panels
 * (KPI, chart, table), each the result of its own validated, authorized, read-only query. The page lays
 * them out as a dashboard: narrative first, headline numbers, charts, then the detail tables, and
 * "How this was computed" with the redacted SQL of every query. History and sharing arrive with P9.
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
  readonly copied = signal(false);
  readonly examples = ['e1', 'e2', 'e3'];

  readonly application = computed(() => this.applications().find((a) => a.applicationId === this.selectedApplication()) ?? null);
  readonly panels = computed<AnalysisPanel[]>(() => this.answer()?.panels ?? []);
  readonly truncatedRows = computed(() => this.panels().filter((p) => p.result.truncated).reduce((n, p) => n + p.result.rowCount, 0));
  readonly totalRows = computed(() => this.panels().reduce((n, p) => n + p.result.rowCount, 0));
  readonly totalTokens = computed(() => {
    const a = this.answer();
    return a ? a.planUsage.inputTokens + a.planUsage.outputTokens + (a.interpretation?.usage?.inputTokens ?? 0) + (a.interpretation?.usage?.outputTokens ?? 0) : 0;
  });

  /** Single-number panels become headline cards: the first numeric cell of the first row. */
  readonly kpis = computed<KpiCard[]>(() =>
    this.panels()
      .filter((p) => p.plan.visualization?.type === 'kpi' && p.result.rows.length > 0)
      .map((p) => {
        const numeric = p.result.columns.findIndex((c) => c.isNumeric);
        const column = numeric >= 0 ? numeric : 0;
        return { title: p.title || p.plan.objective, value: p.result.rows[0][column], column: p.result.columns[column].name };
      }),
  );

  /** Chart panels (bar, line, pie) with the series the server calculated. */
  readonly charts = computed<ChartCard[]>(() => {
    const rtl = this.language.isRtl();
    return this.panels()
      .map((p, index) => ({ p, index }))
      .filter(({ p }) => this.chartType(p) !== null && p.result.series.length > 0)
      .map(({ p, index }) => ({ index, title: p.title || p.plan.objective, option: this.chartOption(p, rtl) }));
  });

  /** Table panels, plus any panel that could not be drawn (no series) so its rows are never hidden; a single-panel answer always shows its rows. */
  readonly tables = computed<AnalysisPanel[]>(() => {
    const all = this.panels();
    if (all.length === 1) {
      return all[0].result.rows.length > 0 ? all : [];
    }
    return all.filter((p) => p.result.rows.length > 0 && ((p.plan.visualization?.type ?? 'table') === 'table' || (this.chartType(p) !== null && p.result.series.length === 0)));
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

  /** The PDF numbers sections itself; drop a number the model put in the title so the contents list reads the same. */
  sectionTitle(title: string): string {
    return title.replace(/^\s*(\d+|[IVXivx]+)[.)]\s*(\d+[.)]\s*)?/, '').trim();
  }

  reportUrl(analysisId: string): string {
    return this.ai.reportUrl(analysisId);
  }

  copyEmail(draft: EmailDraft): void {
    const text = `${this.transloco.translate('ask.email.subjectLabel')}: ${draft.subject}\n\n${draft.body}`;
    navigator.clipboard?.writeText(text).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2500);
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
    this.copied.set(false);
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

  format(value: string | number | boolean | null, isNumeric: boolean): string {
    if (value === null || value === undefined) {
      return '—';
    }
    if (isNumeric && typeof value === 'number') {
      return value.toLocaleString(this.language.current() === 'ar' ? 'ar-EG' : 'en-US', { maximumFractionDigits: 2 });
    }
    if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T00:00:00(\.0+)?$/.test(value)) {
      return value.slice(0, 10);
    }
    return String(value);
  }

  /** Column aliases come from the plan (snake_case identifiers); show them as words. */
  label(name: string): string {
    const words = name.replace(/_/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2').trim();
    return words.charAt(0).toUpperCase() + words.slice(1);
  }

  /** Month-truncated dates (YYYY-MM-01) read better as YYYY-MM on an axis. */
  private axisLabel(category: string): string {
    return /^\d{4}-\d{2}-01$/.test(category) ? category.slice(0, 7) : category;
  }

  private chartType(p: AnalysisPanel): 'bar' | 'line' | 'pie' | null {
    const type = p.plan.visualization?.type;
    return type === 'bar' || type === 'line' || type === 'pie' ? type : null;
  }

  private chartOption(p: AnalysisPanel, rtl: boolean): EChartsOption {
    const base = baseChartOption(rtl);
    const type = this.chartType(p);
    if (type === 'pie') {
      const s = p.result.series[0];
      return { ...base, xAxis: undefined, yAxis: undefined, grid: undefined, tooltip: { trigger: 'item', backgroundColor: '#0B1F3A', borderWidth: 0, textStyle: { color: '#F5F8FC' } }, series: [{ type: 'pie', radius: ['35%', '70%'], data: s.categories.map((c, i) => ({ name: c, value: s.values[i] })) }] };
    }
    return {
      ...base,
      legend: p.result.series.length > 1 ? { bottom: 0, textStyle: { color: '#5B6B82' } } : undefined,
      xAxis: { ...(base.xAxis as object), data: p.result.series[0].categories.map((c) => this.axisLabel(c)) },
      series: p.result.series.map((s) => ({ name: s.name, type: type === 'line' ? 'line' : 'bar', smooth: type === 'line', showSymbol: false, areaStyle: type === 'line' ? { opacity: 0.08 } : undefined, barMaxWidth: 36, data: s.values })),
    };
  }
}
