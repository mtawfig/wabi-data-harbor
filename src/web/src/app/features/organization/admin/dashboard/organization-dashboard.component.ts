import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import type { EChartsOption } from 'echarts';
import { NgxEchartsDirective } from 'ngx-echarts';
import { LanguageService } from '../../../../core/i18n/language.service';
import { DashboardTableRow, OrganizationDashboard } from '../../../../core/platform/insights.models';
import { InsightsService } from '../../../../core/platform/insights.service';
import { MetricBucket } from '../../../../core/platform/platform.models';
import { baseChartOption } from '../../../../shared/ui/chart-theme';
import { TicketWidgetComponent } from '../../../support/ticket-widget.component';
import { WiDatePipe, WiNumberPipe } from '../../../../shared/format/locale-pipes';

type Preset = 'today' | '7d' | '30d' | 'month' | 'custom';

/** Organization Administrator dashboard (specification §14): organization-only KPIs, trends and tables, read through the insights views. */
@Component({
  selector: 'app-organization-dashboard',
  imports: [FormsModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, RouterLink, TranslocoModule, NgxEchartsDirective, TicketWidgetComponent, WiDatePipe, WiNumberPipe],
  templateUrl: './organization-dashboard.component.html',
  styleUrls: ['../../../platform/dashboard/platform-dashboard.component.scss', './organization-dashboard.component.scss'],
})
export class OrganizationDashboardComponent {
  private readonly insights = inject(InsightsService);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly data = signal<OrganizationDashboard | null>(null);
  readonly loading = signal(false);
  readonly preset = signal<Preset>('30d');
  readonly bucket = signal<MetricBucket>('Day');
  readonly customFrom = signal('');
  readonly customTo = signal('');
  private readonly seriesLabels = toSignal(this.transloco.selectTranslateObject<Record<string, string>>('orgDashboard.series'), { initialValue: {} as Record<string, string> });

  private readonly kpiOrder = ['totalUsers', 'activeUsers', 'usersSignedIn', 'departments', 'configuredApplications', 'approvedObjects', 'logins', 'failedLogins', 'failedOperations', 'ticketsCreated', 'openTickets', 'avgFirstResponseMinutes', 'avgResolutionMinutes', 'supportGrants', 'emailsSent', 'emailsFailed', 'analysesCompleted', 'analysesFailed', 'reportsGenerated', 'aiTokens', 'internalShares', 'externalShares'];
  readonly kpiMap = computed(() => new Map((this.data()?.kpis ?? []).map((k) => [k.code, k])));
  readonly primaryKpis = computed(() => this.kpiOrder.filter((code) => this.kpiMap().get(code)?.collected));
  readonly pendingKpis = computed(() => this.kpiOrder.filter((code) => this.kpiMap().has(code) && !this.kpiMap().get(code)?.collected));

  readonly usersChart = computed(() => this.chart(['activeUsers', 'logins', 'failedLogins'], 'line'));
  readonly ticketsChart = computed(() => this.chart(['ticketsCreated'], 'bar'));
  readonly emailsChart = computed(() => this.chart(['emailsSent', 'emailsFailed'], 'bar'));
  readonly failuresChart = computed(() => this.chart(['failedOperations', 'failedLogins'], 'line'));

  constructor() {
    this.load();
  }

  load(): void {
    const { from, to } = this.range();
    this.loading.set(true);
    this.insights.organizationDashboard(from, to, this.bucket()).subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  setPreset(preset: Preset): void {
    this.preset.set(preset);
    if (preset !== 'custom') {
      this.bucket.set('Day');
      this.load();
    }
  }

  kpi(code: string): number {
    return this.kpiMap().get(code)?.value ?? 0;
  }

  rowLabel(r: DashboardTableRow): string {
    const name = this.language.current() === 'ar' ? r.labelAr || r.label : r.label;
    return name || this.transloco.translate('orgDashboard.noDepartment');
  }

  appName(a: { applicationName: string; applicationNameAr: string }): string {
    return this.language.current() === 'ar' ? a.applicationNameAr : a.applicationName;
  }

  private range(): { from: Date; to: Date } {
    const now = new Date();
    const startOfDay = (d: Date) => new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()));
    switch (this.preset()) {
      case 'today':
        return { from: startOfDay(now), to: now };
      case '7d':
        return { from: startOfDay(new Date(now.getTime() - 6 * 86400000)), to: now };
      case 'month':
        return { from: new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1)), to: now };
      case 'custom': {
        const from = this.customFrom() ? new Date(this.customFrom()) : startOfDay(new Date(now.getTime() - 29 * 86400000));
        const to = this.customTo() ? new Date(new Date(this.customTo()).getTime() + 86399999) : now;
        return { from, to };
      }
      default:
        return { from: startOfDay(new Date(now.getTime() - 29 * 86400000)), to: now };
    }
  }

  private chart(codes: string[], type: 'bar' | 'line'): EChartsOption {
    const series = (this.data()?.series ?? []).filter((s) => codes.includes(s.code));
    const base = baseChartOption(this.language.isRtl());
    const categories = [...new Set(series.flatMap((s) => s.points.map((p) => p.bucket)))].sort();
    return {
      ...base,
      xAxis: { ...(base.xAxis as object), data: categories.map((c) => this.formatBucket(c)) },
      series: series.map((s) => {
        const byBucket = new Map(s.points.map((p) => [p.bucket, p.value]));
        return { name: this.seriesLabels()[s.code] ?? s.code, type, smooth: type === 'line', showSymbol: false, areaStyle: type === 'line' ? { opacity: 0.08 } : undefined, barMaxWidth: 28, data: categories.map((c) => byBucket.get(c) ?? 0) };
      }),
    };
  }

  private formatBucket(iso: string): string {
    const d = new Date(iso);
    const locale = this.language.current() === 'ar' ? 'ar-EG' : 'en-GB';
    return this.bucket() === 'Month' ? d.toLocaleDateString(locale, { month: 'short', year: '2-digit' }) : d.toLocaleDateString(locale, { day: 'numeric', month: 'short' });
  }
}
