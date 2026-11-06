import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import type { EChartsOption } from 'echarts';
import { NgxEchartsDirective } from 'ngx-echarts';
import { LanguageService } from '../../core/i18n/language.service';
import { AnalysisPanel, AskResponse, EmailDraft } from '../../core/platform/ai.models';
import { baseChartOption } from '../ui/chart-theme';

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
 * Renders a verified answer (specification §19 step 16): report and email cards when present, then the
 * narrative, headline KPI cards, chart grid, detail tables and "How this was computed". Used by the Ask page
 * for a fresh answer and by My History for a stored version; the markup and numbers are identical in both.
 */
@Component({
  selector: 'app-analysis-answer',
  imports: [DecimalPipe, MatButtonModule, MatExpansionModule, MatIconModule, TranslocoModule, NgxEchartsDirective],
  templateUrl: './analysis-answer.component.html',
  styleUrl: './analysis-answer.component.scss',
})
export class AnalysisAnswerComponent {
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly answer = input.required<AskResponse>();
  /** Where the PDF of this answer is served from (differs between a fresh answer and a stored version). */
  readonly reportUrl = input.required<string>();

  readonly copied = signal(false);
  readonly panels = computed<AnalysisPanel[]>(() => this.answer().panels ?? []);
  readonly truncatedRows = computed(() => this.panels().filter((p) => p.result.truncated).reduce((n, p) => n + p.result.rowCount, 0));
  readonly totalRows = computed(() => this.panels().reduce((n, p) => n + p.result.rowCount, 0));
  readonly totalTokens = computed(() => {
    const a = this.answer();
    return a.planUsage.inputTokens + a.planUsage.outputTokens + (a.interpretation?.usage?.inputTokens ?? 0) + (a.interpretation?.usage?.outputTokens ?? 0);
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

  copyEmail(draft: EmailDraft): void {
    const text = `${this.transloco.translate('ask.email.subjectLabel')}: ${draft.subject}\n\n${draft.body}`;
    navigator.clipboard?.writeText(text).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2500);
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

  /** The PDF numbers sections itself; drop a number the model put in the title so the contents list reads the same. */
  sectionTitle(title: string): string {
    return title.replace(/^\s*(\d+|[IVXivx]+)[.)]\s*(\d+[.)]\s*)?/, '').trim();
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
