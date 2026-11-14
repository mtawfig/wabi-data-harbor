import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import type { EChartsOption } from 'echarts';
import { NgxEchartsDirective } from 'ngx-echarts';
import { LanguageService } from '../../core/i18n/language.service';
import { AnalysisPanel, AskResponse, EmailDraft } from '../../core/platform/ai.models';
import { problemMessage } from '../../core/problem-message';
import { baseChartOption } from '../ui/chart-theme';
import { answerAccess } from './answer-access';
import { longList, shownRows } from './long-list';
import { PlanDescription, describePanel } from './plan-description';
import { WiNumberPipe } from '../format/locale-pipes';

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
  imports: [MatButtonModule, MatExpansionModule, MatIconModule, TranslocoModule, NgxEchartsDirective, WiNumberPipe],
  templateUrl: './analysis-answer.component.html',
  styleUrl: './analysis-answer.component.scss',
})
export class AnalysisAnswerComponent {
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly answer = input.required<AskResponse>();
  /** Where the PDF of this answer is served from (differs between a fresh answer and a stored version). */
  readonly reportUrl = input.required<string>();
  /** Where "View all in Excel" is asked for (P13.50); without it a long table has its note and no button. */
  readonly excelUrl = input<string | null>(null);

  private readonly http = inject(HttpClient);
  readonly exportingAll = signal(false);
  readonly exportError = signal<string | null>(null);

  readonly copied = signal(false);
  readonly panels = computed<AnalysisPanel[]>(() => this.answer().panels ?? []);
  /** Row scopes that limited this answer to what the asker may see (shown so it is never read as the whole organization). */
  readonly access = computed(() => answerAccess(this.panels()));
  readonly totalRows = computed(() => this.panels().reduce((n, p) => n + p.result.rowCount, 0));
  /** Re-evaluates once the active language's translations have loaded, so the plain-language text never shows keys. */
  private readonly translation = toSignal(this.transloco.selectTranslation());
  /** "How this was computed": each panel's validated plan in plain words, in the interface language (no SQL). */
  readonly descriptions = computed<PlanDescription[]>(() => {
    this.translation();
    const language = this.language.current();
    return this.panels().map((p) => describePanel(p, language, (key, params) => this.transloco.translate(key, params)));
  });

  /** Each panel's column headers in the interface language (the catalogue's Arabic names in Arabic). */
  private readonly headers = computed(() => new Map(this.panels().map((p, i) => [p, this.descriptions()[i]?.headers ?? {}])));

  /** The rows a table shows: the first hundred. */
  shown(panel: AnalysisPanel): AnalysisPanel['result']['rows'] {
    return shownRows(panel.result);
  }

  /** For a list longer than its table: how many rows the table shows and how many the list has (null when not known). */
  longList(panel: AnalysisPanel): { shown: number; total: number | null } | null {
    return longList(panel.result);
  }

  /** "View all in Excel": the workbook with every row of the list, made by the server under the reader's own permissions. */
  viewAll(): void {
    const url = this.excelUrl();
    if (!url || this.exportingAll()) {
      return;
    }
    this.exportingAll.set(true);
    this.exportError.set(null);
    this.http.post(url, {}, { responseType: 'blob', observe: 'response' }).subscribe({
      next: (response) => {
        this.exportingAll.set(false);
        const blob = response.body;
        if (!blob) {
          return;
        }
        const named = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(response.headers.get('Content-Disposition') ?? '');
        const link = document.createElement('a');
        const href = URL.createObjectURL(blob);
        link.href = href;
        link.download = named ? decodeURIComponent(named[1]) : 'analysis.xlsx';
        link.click();
        setTimeout(() => URL.revokeObjectURL(href), 10_000);
      },
      error: (e: HttpErrorResponse) => {
        this.exportingAll.set(false);
        // A failed download arrives as a blob: read the problem out of it so its code can be translated.
        const body = e.error;
        if (body instanceof Blob) {
          void body.text().then((text) => {
            let parsed: unknown;
            try {
              parsed = JSON.parse(text);
            } catch {
              parsed = null;
            }
            this.exportError.set(problemMessage(new HttpErrorResponse({ error: parsed, status: e.status, statusText: e.statusText, url: e.url ?? undefined }), this.transloco));
          });
        } else {
          this.exportError.set(problemMessage(e, this.transloco));
        }
      },
    });
  }

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
    this.headers();
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

  join(items: string[]): string {
    return items.join(this.language.current() === 'ar' ? '، ' : ', ');
  }

  /** A result column's header: the plan's name for it in the interface language, else the alias as words. */
  header(p: AnalysisPanel, name: string): string {
    return this.headers().get(p)?.[name.toLowerCase()] ?? this.label(name);
  }

  /** A series is named after its measure, a value of the second category column, or both ("amount · Arrival"). */
  private seriesName(p: AnalysisPanel, name: string): string {
    const measures = this.headers().get(p) ?? {};
    const split = name.indexOf(' · ');
    if (split > 0 && measures[name.slice(0, split).toLowerCase()]) {
      return `${measures[name.slice(0, split).toLowerCase()]} · ${name.slice(split + 3)}`;
    }
    return measures[name.toLowerCase()] ?? name;
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
      series: p.result.series.map((s) => ({ name: this.seriesName(p, s.name), type: type === 'line' ? 'line' : 'bar', smooth: type === 'line', showSymbol: false, areaStyle: type === 'line' ? { opacity: 0.08 } : undefined, barMaxWidth: 36, data: s.values })),
    };
  }
}
