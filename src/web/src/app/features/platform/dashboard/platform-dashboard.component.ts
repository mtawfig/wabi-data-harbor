import { DatePipe, DecimalPipe } from '@angular/common';
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
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import type { EChartsOption } from 'echarts';
import { NgxEchartsDirective } from 'ngx-echarts';
import { LanguageService } from '../../../core/i18n/language.service';
import { OrganizationDetail } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { MetricBucket, MetricSeries, PlatformDashboard } from '../../../core/platform/platform.models';
import { PlatformService } from '../../../core/platform/platform.service';
import { baseChartOption } from '../../../shared/ui/chart-theme';
import { BootstrapStatusComponent } from '../settings/bootstrap-status.component';
import { TicketWidgetComponent } from '../../support/ticket-widget.component';

type Preset = 'today' | '7d' | '30d' | 'month' | 'custom';

/** Platform operations dashboard (specification §13): KPIs, trends, operational tables. Aggregates only. */
@Component({
  selector: 'app-platform-dashboard',
  imports: [DatePipe, DecimalPipe, FormsModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatTableModule, RouterLink, TranslocoModule, NgxEchartsDirective, BootstrapStatusComponent, TicketWidgetComponent],
  templateUrl: './platform-dashboard.component.html',
  styleUrl: './platform-dashboard.component.scss',
})
export class PlatformDashboardComponent {
  private readonly platform = inject(PlatformService);
  private readonly organizations = inject(OrganizationsService);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly data = signal<PlatformDashboard | null>(null);
  readonly loading = signal(false);
  readonly preset = signal<Preset>('30d');
  readonly bucket = signal<MetricBucket>('Day');
  readonly organizationId = signal<string | null>(null);
  readonly customFrom = signal('');
  readonly customTo = signal('');
  readonly organizationOptions = signal<OrganizationDetail[]>([]);
  /** Series labels as a signal so charts re-render once the active language file has loaded. */
  private readonly seriesLabels = toSignal(this.transloco.selectTranslateObject<Record<string, string>>('platformDashboard.series'), { initialValue: {} as Record<string, string> });

  /** Display order of the KPI cards; a code is shown only when the API reports it as collected. */
  private readonly kpiOrder = ['totalOrganizations', 'activeOrganizations', 'suspendedOrganizations', 'totalOrganizationUsers', 'activeUsersInPeriod', 'configuredApplications', 'logins', 'failedLogins', 'emailsSent', 'emailsFailed', 'apiErrors', 'platformAdministrators', 'openTickets', 'criticalIncidents', 'ticketsCreatedInPeriod', 'avgFirstResponseMinutes', 'avgResolutionMinutes', 'aiRequests', 'analysesCompleted', 'analysesFailed', 'reportsGenerated', 'emailDraftsCreated'];
  readonly primaryKpis = computed(() => {
    const map = this.kpiMap();
    return this.kpiOrder.filter((code) => map.get(code)?.collected);
  });
  readonly pendingKpis = computed(() => {
    const map = this.kpiMap();
    return this.kpiOrder.filter((code) => map.has(code) && !map.get(code)?.collected);
  });
  readonly usageColumns = ['organization', 'status', 'users', 'activeUsers', 'logins', 'emails'];
  readonly errorColumns = ['at', 'eventType', 'organization', 'detail'];

  readonly kpiMap = computed(() => new Map((this.data()?.kpis ?? []).map((k) => [k.code, k])));
  readonly organizationNames = computed(() => new Map(this.organizationOptions().map((o) => [o.organizationId, this.language.current() === 'ar' ? o.organizationNameAr : o.organizationName])));

  readonly organizationsChart = computed(() => this.chart(['organizationsRegistered'], 'bar'));
  readonly usersChart = computed(() => this.chart(['activeUsers', 'logins', 'failedLogins'], 'line'));
  readonly emailsChart = computed(() => this.chart(['emailsSent', 'emailsFailed'], 'bar'));
  readonly errorsChart = computed(() => this.chart(['apiErrors', 'failedLogins'], 'line'));
  readonly ticketsChart = computed(() => this.chart(['ticketsCreated', 'ticketsResolved'], 'bar'));

  constructor() {
    this.organizations.list(1, 200).subscribe((r) => this.organizationOptions.set(r.items));
    this.load();
  }

  load(): void {
    const { from, to } = this.range();
    this.loading.set(true);
    this.platform.dashboard(from, to, this.bucket(), this.organizationId()).subscribe({
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

  organizationLabel(id?: string | null): string {
    return (id && this.organizationNames().get(id)) || '—';
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
    const categories = (series[0]?.points ?? []).map((p) => this.formatBucket(p.bucket));
    return {
      ...base,
      xAxis: { ...(base.xAxis as object), data: categories },
      series: series.map((s: MetricSeries) => ({
        name: this.seriesLabels()[s.code] ?? s.code,
        type,
        smooth: type === 'line',
        showSymbol: false,
        areaStyle: type === 'line' ? { opacity: 0.08 } : undefined,
        barMaxWidth: 28,
        data: s.points.map((p) => p.value),
      })),
    };
  }

  private formatBucket(iso: string): string {
    const d = new Date(iso);
    const locale = this.language.current() === 'ar' ? 'ar-EG' : 'en-GB';
    return this.bucket() === 'Month' ? d.toLocaleDateString(locale, { month: 'short', year: '2-digit' }) : d.toLocaleDateString(locale, { day: 'numeric', month: 'short' });
  }
}
