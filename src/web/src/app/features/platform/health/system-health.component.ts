import { DatePipe, DecimalPipe, JsonPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslocoModule } from '@jsverse/transloco';
import { PlatformHealthReport } from '../../../core/platform/platform.models';
import { PlatformService } from '../../../core/platform/platform.service';

/** System Health (specification §13.2): live check results with status, duration and data, plus last-checked time. */
@Component({
  selector: 'app-system-health',
  imports: [DatePipe, DecimalPipe, JsonPipe, MatButtonModule, MatIconModule, MatProgressBarModule, MatTableModule, TranslocoModule],
  templateUrl: './system-health.component.html',
  styles: `
    .status { display: inline-flex; align-items: center; gap: 8px; padding: 8px 14px; border-radius: 999px; font-weight: 600; background: #e2e8f0; color: var(--wi-slate); }
    .status[data-status='Healthy'] { background: #dcfce7; color: #166534; }
    .status[data-status='Degraded'] { background: #fef3c7; color: #92400e; }
    .status[data-status='Unhealthy'] { background: #fee2e2; color: #991b1b; }
    .checks { width: 100%; }
    .num { text-align: end; }
    pre { margin: 0; font-size: 12px; white-space: pre-wrap; direction: ltr; text-align: left; }
    .summary { display: flex; flex-wrap: wrap; align-items: center; gap: 16px; margin-bottom: 16px; }
  `,
})
export class SystemHealthComponent {
  private readonly platform = inject(PlatformService);
  readonly report = signal<PlatformHealthReport | null>(null);
  readonly loading = signal(false);
  readonly columns = ['name', 'status', 'description', 'duration', 'data'];

  constructor() {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    this.platform.health().subscribe({ next: (r) => { this.report.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  icon(status: string): string {
    return status === 'Healthy' ? 'check_circle' : status === 'Degraded' ? 'warning' : 'error';
  }
}
