import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from '../organizations/organizations.models';
import { Administrator, AuditLogRow, AuditQuery, MetricBucket, PlatformDashboard, PlatformHealthReport } from './platform.models';

/** Platform operations: dashboard metrics, health, audit and platform administrators. */
@Injectable({ providedIn: 'root' })
export class PlatformService {
  private readonly http = inject(HttpClient);

  dashboard(from: Date, to: Date, bucket: MetricBucket, organizationId?: string | null): Observable<PlatformDashboard> {
    let params = new HttpParams().set('from', from.toISOString()).set('to', to.toISOString()).set('bucket', bucket);
    if (organizationId) {
      params = params.set('organizationId', organizationId);
    }
    return this.http.get<PlatformDashboard>('/api/platform/dashboard', { params });
  }

  health(): Observable<PlatformHealthReport> {
    return this.http.get<PlatformHealthReport>('/api/platform/health');
  }

  audit(query: AuditQuery): Observable<PagedResult<AuditLogRow>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    const optional: [string, string | null | undefined][] = [
      ['from', query.from],
      ['to', query.to],
      ['eventType', query.eventType],
      ['organizationId', query.organizationId],
      ['actorType', query.actorType],
      ['search', query.search],
    ];
    for (const [key, value] of optional) {
      if (value) {
        params = params.set(key, value);
      }
    }
    return this.http.get<PagedResult<AuditLogRow>>('/api/platform/audit', { params });
  }

  auditEventTypes(): Observable<string[]> {
    return this.http.get<string[]>('/api/platform/audit/event-types');
  }

  administrators(): Observable<Administrator[]> {
    return this.http.get<Administrator[]>('/api/platform/administrators');
  }

  createAdministrator(email: string, fullName: string, preferredLanguage: 'en' | 'ar'): Observable<{ id: string; deliveredTo: string }> {
    return this.http.post<{ id: string; deliveredTo: string }>('/api/platform/administrators', { email, fullName, preferredLanguage });
  }

  resetAdministratorPassword(id: string): Observable<{ id: string; deliveredTo: string }> {
    return this.http.post<{ id: string; deliveredTo: string }>(`/api/platform/administrators/${id}/reset-password`, {});
  }

  setAdministratorStatus(id: string, isActive: boolean): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`/api/platform/administrators/${id}/status`, { isActive });
  }
}
