import { SlicePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import type { EChartsOption } from 'echarts';
import { NgxEchartsDirective } from 'ngx-echarts';
import { LanguageService } from '../../../core/i18n/language.service';
import { InsightsColumn, InsightsMetric, MetricResult, OrganizationInsightsCatalog } from '../../../core/platform/insights.models';
import { InsightsService } from '../../../core/platform/insights.service';
import { MetricBucket } from '../../../core/platform/platform.models';
import { problemMessage } from '../../../core/problem-message';
import { baseChartOption } from '../../../shared/ui/chart-theme';
import { WiNumberPipe } from '../../../shared/format/locale-pipes';

type Preset = 'all' | '7d' | '30d' | '90d' | 'year';

/** Organization Insights (specification §12/§14): run the metrics granted to the signed-in user, with period, bucket and grouping, about this organization only. */
@Component({
  selector: 'app-organization-insights',
  imports: [SlicePipe, FormsModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, TranslocoModule, NgxEchartsDirective, WiNumberPipe],
  templateUrl: './organization-insights.component.html',
  styleUrl: './organization-insights.component.scss',
})
export class OrganizationInsightsComponent {
  private readonly insights = inject(InsightsService);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly catalog = signal<OrganizationInsightsCatalog | null>(null);
  readonly selectedCode = signal<string | null>(null);
  readonly preset = signal<Preset>('30d');
  readonly bucket = signal<MetricBucket | null>('Day');
  readonly groupBy = signal<string | null>(null);
  readonly result = signal<MetricResult | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly metrics = computed(() => this.catalog()?.metrics ?? []);
  readonly metric = computed(() => this.metrics().find((m) => m.metricCode === this.selectedCode()) ?? null);
  readonly columns = computed<InsightsColumn[]>(() => {
    const m = this.metric();
    const o = m ? this.catalog()?.objects.find((x) => x.objectId === m.objectId) : null;
    return (o?.columns ?? []).filter((c) => !/^(CreatedAt|LastLoginAt|EventDate|.*At)$/.test(c.columnName) && !/uniqueidentifier|datetime/.test(c.dataType));
  });
  readonly hasDate = computed(() => !!this.metric()?.dateColumn);
  readonly total = computed(() => (this.result()?.rows ?? []).reduce((sum, r) => sum + r.value, 0));
  readonly chart = computed<EChartsOption | null>(() => {
    const r = this.result();
    if (!r || r.rows.length === 0) {
      return null;
    }
    const base = baseChartOption(this.language.isRtl());
    const name = this.metric() ? this.name(this.metric()!) : r.metricCode;
    const bucketed = r.rows.some((x) => x.bucket);
    const grouped = r.rows.some((x) => x.group !== null && x.group !== undefined);
    if (bucketed && grouped) {
      const buckets = [...new Set(r.rows.map((x) => x.bucket!))].sort();
      const groups = [...new Set(r.rows.map((x) => x.group ?? ''))];
      return { ...base, xAxis: { ...(base.xAxis as object), data: buckets.map((b) => this.formatBucket(b)) }, series: groups.map((g) => ({ name: g || '—', type: 'bar', stack: 'total', barMaxWidth: 28, data: buckets.map((b) => r.rows.find((x) => x.bucket === b && (x.group ?? '') === g)?.value ?? 0) })) };
    }
    if (bucketed) {
      return { ...base, xAxis: { ...(base.xAxis as object), data: r.rows.map((x) => this.formatBucket(x.bucket!)) }, series: [{ name, type: 'line', smooth: true, showSymbol: false, areaStyle: { opacity: 0.08 }, data: r.rows.map((x) => x.value) }] };
    }
    return { ...base, xAxis: { ...(base.xAxis as object), data: r.rows.map((x) => x.group ?? '—') }, series: [{ name, type: 'bar', barMaxWidth: 36, data: r.rows.map((x) => x.value) }] };
  });

  constructor() {
    this.insights.organizationCatalog().subscribe({
      next: (c) => {
        this.catalog.set(c);
        const first = c.metrics[0];
        if (first) {
          this.select(first.metricCode);
        }
      },
      error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)),
    });
  }

  name(m: { displayNameEn: string; displayNameAr: string }): string {
    return this.language.current() === 'ar' ? m.displayNameAr : m.displayNameEn;
  }

  columnName(c: InsightsColumn): string {
    return this.language.current() === 'ar' ? c.displayNameAr : c.displayNameEn;
  }

  select(code: string): void {
    this.selectedCode.set(code);
    this.groupBy.set(null);
    this.run();
  }

  setPreset(preset: Preset): void {
    this.preset.set(preset);
    this.run();
  }

  run(): void {
    const m = this.metric();
    if (!m) {
      return;
    }
    const period = this.hasDate() && this.preset() !== 'all' ? this.range() : { from: null, to: null };
    this.loading.set(true);
    this.error.set(null);
    this.insights.queryMetric(m.metricCode, { from: period.from?.toISOString() ?? null, to: period.to?.toISOString() ?? null, bucket: this.hasDate() ? this.bucket() : null, groupBy: this.groupBy() }).subscribe({
      next: (r) => {
        this.result.set(r);
        this.loading.set(false);
      },
      error: (e: HttpErrorResponse) => {
        this.error.set(problemMessage(e, this.transloco));
        this.result.set(null);
        this.loading.set(false);
      },
    });
  }

  describe(m: InsightsMetric): string {
    return `${this.transloco.translate('platformInsights.aggregates.' + m.aggregate)} · ${m.columnName ?? '*'} · ${m.viewName}`;
  }

  private range(): { from: Date; to: Date } {
    const now = new Date();
    const days = this.preset() === '7d' ? 7 : this.preset() === '90d' ? 90 : this.preset() === 'year' ? 365 : 30;
    return { from: new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() - (days - 1))), to: now };
  }

  private formatBucket(iso: string): string {
    const d = new Date(iso);
    const locale = this.language.current() === 'ar' ? 'ar-EG' : 'en-GB';
    return this.bucket() === 'Month' ? d.toLocaleDateString(locale, { month: 'short', year: '2-digit' }) : d.toLocaleDateString(locale, { day: 'numeric', month: 'short' });
  }
}
