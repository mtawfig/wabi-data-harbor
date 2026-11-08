import { Component, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { Router } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { LanguageService } from '../../core/i18n/language.service';
import { AppNotification, NotificationsService } from '../../core/notifications/notifications.service';
import { SupportArea } from '../../core/support/support.models';
import { WiDatePipe } from '../../shared/format/locale-pipes';

const ICONS: Record<string, string> = {
  'Ticket.Created': 'add_comment',
  'Ticket.StatusChanged': 'swap_horiz',
  'Ticket.Assigned': 'assignment_ind',
  'Ticket.CommentAdded': 'forum',
  'Ticket.AttachmentAdded': 'attach_file',
  'Analysis.Shared': 'share',
  'Analysis.ShareRevoked': 'link_off',
  'Monitoring.Alert': 'notification_important',
  'Monitoring.RulePaused': 'pause_circle',
  'Reports.SubscriptionPaused': 'schedule_send',
};

/** Notification centre: localized from codes and parameters; clicking opens the related ticket and marks the item read. */
@Component({
  selector: 'app-notification-center',
  imports: [MatButtonModule, MatIconModule, MatPaginatorModule, MatProgressBarModule, MatSlideToggleModule, TranslocoModule, WiDatePipe],
  templateUrl: './notification-center.component.html',
  styles: `
    .toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 16px; margin-bottom: 12px; }
    .list { list-style: none; margin: 0; padding: 0; }
    .item { display: flex; gap: 12px; align-items: flex-start; padding: 12px 8px; border-bottom: 1px solid var(--wi-border); cursor: pointer; &:hover { background: #f8fafc; } &--unread { background: #f0f7ff; } &--unread .text { font-weight: 600; } }
    .icon { flex: 0 0 36px; width: 36px; height: 36px; border-radius: 50%; display: inline-flex; align-items: center; justify-content: center; background: #e0f2fe; color: var(--wi-ocean); mat-icon { font-size: 20px; width: 20px; height: 20px; } }
    .body { flex: 1; }
    .text { margin: 0 0 2px; }
    .small { font-size: 12px; }
    .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--wi-ocean); margin-top: 14px; }
    .empty { padding: 32px; text-align: center; }
  `,
})
export class NotificationCenterComponent {
  private readonly notifications = inject(NotificationsService);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly language = inject(LanguageService);

  readonly area = input<SupportArea>('app');
  readonly items = signal<AppNotification[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly unreadOnly = signal(false);
  readonly loading = signal(false);
  readonly unread = computed(() => this.notifications.unread());

  constructor() {
    queueMicrotask(() => this.load());
  }

  load(): void {
    this.loading.set(true);
    this.notifications.list(this.unreadOnly(), this.page(), this.pageSize()).subscribe({
      next: (r) => {
        this.items.set(r.items);
        this.total.set(r.totalCount);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
    this.notifications.refreshUnread().subscribe();
  }

  toggleUnread(value: boolean): void {
    this.unreadOnly.set(value);
    this.page.set(1);
    this.load();
  }

  onPage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  markAll(): void {
    this.notifications.markAllRead().subscribe(() => this.load());
  }

  text(n: AppNotification): string {
    const d = NotificationsService.data(n);
    const status = d.newStatus ? this.transloco.translate(`support.statuses.${d.newStatus}`) : '';
    return this.transloco.translate(`notifications.types.${n.notificationType}`, { ...d, newStatus: status });
  }

  organization(n: AppNotification): string | undefined {
    const d = NotificationsService.data(n);
    return this.language.current() === 'ar' ? d.organizationNameAr : d.organizationName;
  }

  icon(n: AppNotification): string {
    return ICONS[n.notificationType] ?? 'notifications';
  }

  open(n: AppNotification): void {
    if (!n.readAt) {
      this.notifications.markRead(n.notificationId).subscribe();
      this.items.update((items) => items.map((i) => (i.notificationId === n.notificationId ? { ...i, readAt: new Date().toISOString() } : i)));
    }
    if (n.entityType === 'SupportTicket' && n.entityId) {
      void this.router.navigate([this.area() === 'platform' ? '/platform/support' : '/app/support', n.entityId]);
    } else if (n.notificationType === 'Analysis.Shared' && n.entityId) {
      void this.router.navigate(['/app/shared-with-me', n.entityId]);
    } else if (n.notificationType === 'Analysis.ShareRevoked') {
      void this.router.navigate(['/app/shared-with-me']);
    } else if (n.notificationType === 'Monitoring.Alert' && n.entityId) {
      void this.router.navigate(['/app/alerts'], { queryParams: { alert: n.entityId } });
    } else if (n.notificationType === 'Monitoring.RulePaused') {
      void this.router.navigate(['/app/monitoring/rules']);
    } else if (n.notificationType === 'Reports.SubscriptionPaused') {
      void this.router.navigate(['/app/periodic-reports']);
    }
  }
}
