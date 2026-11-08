import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { LanguageService } from '../../core/i18n/language.service';
import { formatNumber, formatValue } from '../../shared/format/number-format';
import {
  AlertDetail,
  AlertPage,
  AlertSeverity,
  AlertStatus,
  DigestPreview,
} from '../../core/monitoring/monitoring.models';
import { MonitoringService } from '../../core/monitoring/monitoring.service';
import { problemMessage } from '../../core/problem-message';
import { WiDatePipe, WiNumberPipe } from '../../shared/format/locale-pipes';

type AlertAction = 'acknowledge' | 'resolve' | 'dismiss' | 'reopen';

/**
 * Alert centre (specification §35.2): the alerts of rules the user owns, wrote or receives (administrators: all).
 * Details appear only when the viewer may read the rule's data; every action is a human decision with a note,
 * and suspending a user (administrators, access alerts) asks for confirmation first (owner decision D2).
 */
@Component({
  selector: 'app-alerts',
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatSelectModule,
    TranslocoModule,
    WiDatePipe,
    WiNumberPipe,
  ],
  templateUrl: './alerts.component.html',
  styleUrl: './monitoring.scss',
})
export class AlertsComponent {
  private readonly monitoring = inject(MonitoringService);
  private readonly transloco = inject(TranslocoService);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);

  /** ?alert=… from a notification opens that alert. */
  readonly alert = input<string | undefined>(undefined);

  readonly statuses: AlertStatus[] = ['Open', 'Acknowledged', 'Resolved', 'Dismissed'];
  readonly severities: AlertSeverity[] = ['High', 'Medium', 'Info'];
  readonly page = signal<AlertPage | null>(null);
  readonly status = signal<AlertStatus | null>('Open');
  readonly severity = signal<AlertSeverity | null>(null);
  readonly pageIndex = signal(1);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly selected = signal<AlertDetail | null>(null);
  readonly note = signal('');
  readonly confirmSuspend = signal(false);
  readonly digest = signal<DigestPreview | null>(null);

  readonly summaryRows = computed(() => {
    const s = this.selected()?.alert.summary;
    if (!s) {
      return [];
    }
    // System rules (P14.4) add their own keys: the application, object or report concerned, the budget and the month.
    return (
      [
        'measure',
        'previous',
        'changePercent',
        'threshold',
        'expected',
        'basis',
        'signal',
        'windowHours',
        'application',
        'object',
        'report',
        'failedQueries',
        'connectionTestFailed',
        'objectRemoved',
        'columnsRemoved',
        'budget',
        'spend',
        'month',
        'reason',
      ] as const
    )
      .filter((k) => s[k] !== undefined && s[k] !== null)
      .map((k) => ({
        key: k,
        value: s[k] as string | number | boolean,
        // Measures, thresholds and counts read as numbers in the interface language ("58,000", not "58000").
        display: typeof s[k] === 'number' ? formatNumber(s[k] as number, this.language.current()) : String(s[k]),
        // The AI budget rule measures a share of the monthly budget.
        percent: s['signal'] === 'ai_budget' && (k === 'measure' || k === 'threshold'),
      }));
  });

  /** A number in the interface language with separators and at most two decimals. */
  num(value: unknown): string {
    return formatValue(value, this.language.current());
  }

  constructor() {
    this.load();
    effect(() => {
      const id = this.alert();
      if (id) {
        this.open(id);
      }
    });
  }

  load(): void {
    this.loading.set(true);
    this.monitoring
      .alerts({
        status: this.status(),
        severity: this.severity(),
        page: this.pageIndex(),
        pageSize: 25,
      })
      .subscribe({
        next: (p) => {
          this.page.set(p);
          this.loading.set(false);
        },
        error: (response: HttpErrorResponse) => {
          this.error.set(problemMessage(response, this.transloco));
          this.loading.set(false);
        },
      });
  }

  setStatus(value: AlertStatus | null): void {
    this.status.set(value);
    this.pageIndex.set(1);
    this.load();
  }

  setSeverity(value: AlertSeverity | null): void {
    this.severity.set(value);
    this.pageIndex.set(1);
    this.load();
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex + 1);
    this.load();
  }

  open(alertId: string): void {
    this.error.set(null);
    this.confirmSuspend.set(false);
    this.note.set('');
    this.monitoring.alert(alertId).subscribe({
      next: (d) => this.selected.set(d),
      error: (response: HttpErrorResponse) =>
        this.error.set(problemMessage(response, this.transloco)),
    });
  }

  close(): void {
    this.selected.set(null);
    if (this.alert()) {
      void this.router.navigate([], { queryParams: { alert: null }, queryParamsHandling: 'merge' });
    }
  }

  act(action: AlertAction): void {
    const current = this.selected();
    if (!current || this.busy()) {
      return;
    }
    this.run(this.monitoring.act(current.alert.alertId, action, this.note() || null));
  }

  suspend(): void {
    const current = this.selected();
    if (!current || this.busy()) {
      return;
    }
    this.run(this.monitoring.act(current.alert.alertId, 'suspend-user', this.note() || null));
  }

  previewDigest(): void {
    this.monitoring.digestPreview().subscribe({ next: (d) => this.digest.set(d) });
  }

  private run(call: ReturnType<MonitoringService['act']>): void {
    this.busy.set(true);
    this.error.set(null);
    call.subscribe({
      next: (d) => {
        this.selected.set(d);
        this.note.set('');
        this.confirmSuspend.set(false);
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => {
        this.error.set(problemMessage(response, this.transloco));
        this.busy.set(false);
      },
    });
  }
}
