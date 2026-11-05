import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, tap } from 'rxjs';
import { PagedResult } from '../organizations/organizations.models';
import { GrantStatus, SupportAccessGrant, SupportOperationDefinition, SupportOperationEntry, SupportOperationResult, SupportSession, SupportTargets } from './support-access.models';
import { SupportArea } from './support.models';

/** Support access grants (specification §16): proposals, decisions, support-mode session and scoped execution. */
@Injectable({ providedIn: 'root' })
export class SupportAccessService {
  private readonly http = inject(HttpClient);

  /** The platform administrator's current support-mode session, polled by the shell and refreshed after every action. */
  readonly session = signal<SupportSession | null>(null);

  private static base(area: SupportArea): string {
    return area === 'platform' ? '/api/platform/support' : '/api/support';
  }

  catalog(): Observable<SupportOperationDefinition[]> {
    return this.http.get<SupportOperationDefinition[]>('/api/platform/support/operations');
  }

  targets(ticketId: string): Observable<SupportTargets> {
    return this.http.get<SupportTargets>(`/api/platform/support/tickets/${ticketId}/targets`);
  }

  forTicket(area: SupportArea, ticketId: string): Observable<SupportAccessGrant[]> {
    return this.http.get<SupportAccessGrant[]>(`${SupportAccessService.base(area)}/tickets/${ticketId}/access-grants`);
  }

  list(area: SupportArea, page: number, pageSize: number, status?: GrantStatus | null, pending?: boolean | null, organizationId?: string | null): Observable<PagedResult<SupportAccessGrant>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (status) {
      params = params.set('status', status);
    }
    if (pending) {
      params = params.set('pending', true);
    }
    if (organizationId && area === 'platform') {
      params = params.set('organizationId', organizationId);
    }
    return this.http.get<PagedResult<SupportAccessGrant>>(`${SupportAccessService.base(area)}/access-grants`, { params });
  }

  propose(ticketId: string, operations: SupportOperationEntry[], reason: string, expiryHours: number | null): Observable<SupportAccessGrant> {
    return this.http.post<SupportAccessGrant>(`/api/platform/support/tickets/${ticketId}/access-grants`, { operations, reason, expiryHours });
  }

  approve(grantId: string, note: string | null, expiryHours: number | null): Observable<SupportAccessGrant> {
    return this.http.post<SupportAccessGrant>(`/api/support/access-grants/${grantId}/approve`, { note, expiryHours });
  }

  decline(grantId: string, note: string | null): Observable<SupportAccessGrant> {
    return this.http.post<SupportAccessGrant>(`/api/support/access-grants/${grantId}/decline`, { note });
  }

  revoke(area: SupportArea, grantId: string, note: string | null): Observable<SupportAccessGrant> {
    return this.http.post<SupportAccessGrant>(`${SupportAccessService.base(area)}/access-grants/${grantId}/revoke`, { note });
  }

  complete(grantId: string, note: string | null): Observable<SupportAccessGrant> {
    return this.http.post<SupportAccessGrant>(`/api/platform/support/access-grants/${grantId}/complete`, { note }).pipe(tap(() => this.session.set(null)));
  }

  enter(grantId: string): Observable<SupportSession> {
    return this.http.post<SupportSession>(`/api/platform/support/access-grants/${grantId}/enter`, {}).pipe(tap((s) => this.session.set(s)));
  }

  exit(): Observable<void> {
    return this.http.post<void>('/api/platform/support/session/exit', {}).pipe(tap(() => this.session.set(null)));
  }

  /** Reads the current session; 204 (no session) and errors both clear the signal. */
  refreshSession(): Observable<SupportSession | null> {
    return this.http.get<SupportSession | null>('/api/platform/support/session', { observe: 'response' }).pipe(
      map((response) => (response.status === 200 && response.body ? response.body : null)),
      catchError(() => of(null)),
      tap((session) => this.session.set(session)),
    );
  }

  execute(operation: string, targetReference: string | null, payload: unknown): Observable<SupportOperationResult> {
    return this.http.post<SupportOperationResult>('/api/platform/support/session/execute', { operation, targetReference, payload });
  }
}
