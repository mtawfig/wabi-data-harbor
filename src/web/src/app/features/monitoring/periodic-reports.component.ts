import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/i18n/language.service';
import {
  ReportDelivery,
  ReportSubscription,
  SaveReportSubscription,
  SubscriptionAnalysis,
  SubscriptionFormat,
  SubscriptionLanguage,
  SubscriptionRecurrence,
  SubscriptionRunResult,
} from '../../core/monitoring/periodic-reports.models';
import { PeriodicReportsService } from '../../core/monitoring/periodic-reports.service';
import { OrganizationUser } from '../../core/organizations/admin.models';
import { OrganizationAdminService } from '../../core/organizations/admin.service';
import { problemMessage } from '../../core/problem-message';
import { WiDatePipe } from '../../shared/format/locale-pipes';

interface SubscriptionForm {
  subscriptionId: string | null;
  name: string;
  historyId: string | null;
  recurrenceKind: SubscriptionRecurrence;
  time: string;
  dayOfWeek: number;
  dayOfMonth: number;
  format: SubscriptionFormat;
  language: SubscriptionLanguage;
  isActive: boolean;
  recipientUserIds: string[];
}

/**
 * Periodic Reports (specification §35.3, owner decision D6): a saved analysis emailed on a schedule, rendered for each
 * recipient under that recipient's own permissions. Administrators choose recipients; Management users subscribe
 * themselves.
 */
@Component({
  selector: 'app-periodic-reports',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatSlideToggleModule, RouterLink, TranslocoModule, WiDatePipe],
  templateUrl: './periodic-reports.component.html',
  styleUrl: './monitoring.scss',
})
export class PeriodicReportsComponent {
  private readonly reports = inject(PeriodicReportsService);
  private readonly admin = inject(OrganizationAdminService);
  private readonly transloco = inject(TranslocoService);
  private readonly auth = inject(AuthService);
  readonly language = inject(LanguageService);

  readonly isAdmin = this.auth.isOrganizationAdmin;
  readonly days = [1, 2, 3, 4, 5, 6, 7];
  readonly subscriptions = signal<ReportSubscription[]>([]);
  readonly analyses = signal<SubscriptionAnalysis[]>([]);
  readonly users = signal<OrganizationUser[]>([]);
  readonly form = signal<SubscriptionForm | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly run = signal<{ name: string; result: SubscriptionRunResult } | null>(null);
  readonly log = signal<{ subscription: ReportSubscription; items: ReportDelivery[] } | null>(null);
  readonly selectedAnalysis = computed(() => this.analyses().find((a) => a.historyId === this.form()?.historyId) ?? null);

  constructor() {
    this.load();
    this.reports.analyses().subscribe((a) => this.analyses.set(a));
    if (this.isAdmin()) {
      this.admin.listUsers(1, 200, undefined, 'Active').subscribe((p) => this.users.set(p.items));
    }
  }

  load(): void {
    this.loading.set(true);
    this.reports.list().subscribe({
      next: (s) => {
        this.subscriptions.set(s);
        this.loading.set(false);
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  startNew(): void {
    this.error.set(null);
    this.run.set(null);
    this.log.set(null);
    const first = this.analyses()[0];
    this.form.set({
      subscriptionId: null,
      name: first ? first.title : '',
      historyId: first?.historyId ?? null,
      recurrenceKind: 'Weekly',
      time: '07:00',
      dayOfWeek: 1,
      dayOfMonth: 1,
      format: 'Pdf',
      language: 'recipient',
      isActive: true,
      recipientUserIds: [],
    });
  }

  edit(s: ReportSubscription): void {
    this.error.set(null);
    this.run.set(null);
    this.log.set(null);
    this.form.set({
      subscriptionId: s.subscriptionId,
      name: s.name,
      historyId: s.historyId,
      recurrenceKind: s.recurrenceKind,
      time: s.timeOfDay.slice(0, 5),
      dayOfWeek: s.dayOfWeek ?? 1,
      dayOfMonth: s.dayOfMonth ?? 1,
      format: s.format,
      language: s.language,
      isActive: s.isActive,
      recipientUserIds: s.recipients.map((r) => r.userId),
    });
  }

  patch(changes: Partial<SubscriptionForm>): void {
    this.form.update((f) => (f ? { ...f, ...changes } : f));
  }

  save(): void {
    const f = this.form();
    if (!f || !f.historyId || this.busy()) {
      return;
    }
    const request: SaveReportSubscription = {
      name: f.name.trim(),
      historyId: f.historyId,
      recurrenceKind: f.recurrenceKind,
      timeOfDay: f.time.length === 5 ? `${f.time}:00` : f.time,
      dayOfWeek: f.recurrenceKind === 'Weekly' ? f.dayOfWeek : null,
      dayOfMonth: f.recurrenceKind === 'Monthly' ? Number(f.dayOfMonth) : null,
      format: f.format,
      language: f.language,
      isActive: f.isActive,
      recipientUserIds: this.isAdmin() ? f.recipientUserIds : [],
    };
    this.busy.set(true);
    this.error.set(null);
    const call = f.subscriptionId ? this.reports.update(f.subscriptionId, request) : this.reports.create(request);
    call.subscribe({
      next: () => {
        this.busy.set(false);
        this.form.set(null);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  runNow(s: ReportSubscription): void {
    this.busy.set(true);
    this.error.set(null);
    this.reports.runNow(s.subscriptionId).subscribe({
      next: (result) => {
        this.run.set({ name: s.name, result });
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  toggle(s: ReportSubscription): void {
    this.busy.set(true);
    this.error.set(null);
    this.reports.setActive(s.subscriptionId, !s.isActive).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  remove(s: ReportSubscription): void {
    if (!confirm(this.transloco.translate('periodic.deleteConfirm', { name: s.name }))) {
      return;
    }
    this.busy.set(true);
    this.reports.delete(s.subscriptionId).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  openLog(s: ReportSubscription): void {
    this.form.set(null);
    this.run.set(null);
    this.reports.deliveries(s.subscriptionId, 1, 50).subscribe({
      next: (page) => this.log.set({ subscription: s, items: page.items }),
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  schedule(s: { recurrenceKind: SubscriptionRecurrence; timeOfDay: string; dayOfWeek?: number; dayOfMonth?: number }): string {
    const time = s.timeOfDay.slice(0, 5);
    switch (s.recurrenceKind) {
      case 'Weekly':
        return this.transloco.translate('monitoring.rules.weeklyAt', { day: this.transloco.translate(`monitoring.days.${s.dayOfWeek ?? 1}`), time });
      case 'Monthly':
        return this.transloco.translate('periodic.monthlyAt', { day: s.dayOfMonth ?? 1, time });
      default:
        return this.transloco.translate('monitoring.rules.dailyAt', { time });
    }
  }

  analysisLabel(a: SubscriptionAnalysis): string {
    const app = this.language.current() === 'ar' ? a.applicationNameAr || a.applicationName : a.applicationName;
    return a.isOwn ? `${a.title} · ${app}` : `${a.title} · ${app} · ${this.transloco.translate('periodic.sharedBy', { name: a.ownerName })}`;
  }

  analysisName(id: string | null): string {
    const a = this.analyses().find((x) => x.historyId === id);
    return a ? this.analysisLabel(a) : '';
  }

  recipientsLabel(s: ReportSubscription): string {
    return s.recipients.map((r) => r.fullName).join(', ');
  }

  private fail(response: HttpErrorResponse): void {
    this.error.set(problemMessage(response, this.transloco));
    this.busy.set(false);
    this.loading.set(false);
  }
}
