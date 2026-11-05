import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { PagedResult } from '../organizations/organizations.models';

export interface AppNotification {
  notificationId: string;
  notificationType: string;
  entityType?: string;
  entityId?: string;
  dataJson?: string;
  readAt?: string;
  createdAt: string;
}

export interface TicketNotificationData {
  ticketId?: string;
  reference?: string;
  subject?: string;
  organizationName?: string;
  organizationNameAr?: string;
  actorName?: string;
  previousStatus?: string;
  newStatus?: string;
}

/** Notification centre of the signed-in principal plus the unread badge shown in the shell header. */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);

  readonly unread = signal(0);

  list(unreadOnly: boolean, page: number, pageSize: number): Observable<PagedResult<AppNotification>> {
    const params = new HttpParams().set('unreadOnly', unreadOnly).set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<AppNotification>>('/api/notifications', { params });
  }

  refreshUnread(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>('/api/notifications/unread-count').pipe(tap((r) => this.unread.set(r.count)));
  }

  markRead(id: string): Observable<void> {
    return this.http.post<void>(`/api/notifications/${id}/read`, {}).pipe(tap(() => this.unread.update((n) => Math.max(0, n - 1))));
  }

  markAllRead(): Observable<{ marked: number }> {
    return this.http.post<{ marked: number }>('/api/notifications/read-all', {}).pipe(tap(() => this.unread.set(0)));
  }

  static data(notification: AppNotification): TicketNotificationData {
    if (!notification.dataJson) {
      return {};
    }
    try {
      return JSON.parse(notification.dataJson) as TicketNotificationData;
    } catch {
      return {};
    }
  }
}
