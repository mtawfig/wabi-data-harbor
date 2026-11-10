import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from '../organizations/organizations.models';
import { ReceivedReport, ReportDelivery, ReportSubscription, SaveReportSubscription, SubscriptionAnalysis, SubscriptionRunResult } from './periodic-reports.models';

/** Periodic Reports (Phase 14, specification §35.3): subscriptions for authors, "sent to me" for every user. */
@Injectable({ providedIn: 'root' })
export class PeriodicReportsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/subscriptions';

  list(): Observable<ReportSubscription[]> {
    return this.http.get<ReportSubscription[]>(this.base);
  }

  /** The analyses a report can be built on. With a subscription id: the ones its creator may use (an administrator editing someone else's report). */
  analyses(subscriptionId?: string): Observable<SubscriptionAnalysis[]> {
    return this.http.get<SubscriptionAnalysis[]>(`${this.base}/analyses`, subscriptionId ? { params: { subscriptionId } } : {});
  }

  create(request: SaveReportSubscription): Observable<ReportSubscription> {
    return this.http.post<ReportSubscription>(this.base, request);
  }

  update(id: string, request: SaveReportSubscription): Observable<ReportSubscription> {
    return this.http.put<ReportSubscription>(`${this.base}/${id}`, request);
  }

  setActive(id: string, active: boolean): Observable<ReportSubscription> {
    return this.http.post<ReportSubscription>(`${this.base}/${id}/${active ? 'resume' : 'pause'}`, {});
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  runNow(id: string): Observable<SubscriptionRunResult> {
    return this.http.post<SubscriptionRunResult>(`${this.base}/${id}/run-now`, {});
  }

  deliveries(id: string, page = 1, pageSize = 25): Observable<PagedResult<ReportDelivery>> {
    return this.http.get<PagedResult<ReportDelivery>>(`${this.base}/${id}/deliveries`, { params: new HttpParams().set('page', page).set('pageSize', pageSize) });
  }

  received(page = 1, pageSize = 25): Observable<PagedResult<ReceivedReport>> {
    return this.http.get<PagedResult<ReceivedReport>>(`${this.base}/received`, { params: new HttpParams().set('page', page).set('pageSize', pageSize) });
  }

  receivedFileUrl(deliveryId: string): string {
    return `${this.base}/received/${deliveryId}/file`;
  }
}
