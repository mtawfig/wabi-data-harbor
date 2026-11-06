import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from '../history/history.models';
import { AskResponse } from '../platform/ai.models';
import { SendEmailRequest, Share, ShareEmailResult, ShareRecipient, SharedAnalysis, SharedAnalysisDetail } from './sharing.models';

/**
 * Sharing (specification §24): owners share with colleagues of their own organization or send a version by email;
 * recipients read stored results through Shared With Me. The organization is never sent; it comes from the session.
 */
@Injectable({ providedIn: 'root' })
export class SharingService {
  private readonly http = inject(HttpClient);

  recipients(search?: string): Observable<ShareRecipient[]> {
    let params = new HttpParams();
    if (search?.trim()) params = params.set('search', search.trim());
    return this.http.get<ShareRecipient[]>('/api/shares/recipients', { params });
  }

  sharesOf(historyId: string): Observable<Share[]> {
    return this.http.get<Share[]>(`/api/history/${historyId}/shares`);
  }

  share(historyId: string, recipientUserIds: string[], message: string | null, expiresAt: string | null): Observable<Share[]> {
    return this.http.post<Share[]>(`/api/history/${historyId}/shares`, { recipientUserIds, message, expiresAt });
  }

  revoke(historyId: string, shareId: string): Observable<void> {
    return this.http.delete<void>(`/api/history/${historyId}/shares/${shareId}`);
  }

  sendEmail(historyId: string, request: SendEmailRequest): Observable<ShareEmailResult> {
    return this.http.post<ShareEmailResult>(`/api/history/${historyId}/email`, request);
  }

  byMe(page: number, pageSize: number, search?: string, includeInactive = false): Observable<PagedResult<Share>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize).set('includeInactive', includeInactive);
    if (search?.trim()) params = params.set('search', search.trim());
    return this.http.get<PagedResult<Share>>('/api/shares/by-me', { params });
  }

  withMe(page: number, pageSize: number, search?: string): Observable<PagedResult<SharedAnalysis>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search?.trim()) params = params.set('search', search.trim());
    return this.http.get<PagedResult<SharedAnalysis>>('/api/shares/with-me', { params });
  }

  shared(historyId: string): Observable<SharedAnalysisDetail> {
    return this.http.get<SharedAnalysisDetail>(`/api/shares/with-me/${historyId}`);
  }

  sharedResult(historyId: string, versionNumber: number): Observable<AskResponse> {
    return this.http.get<AskResponse>(`/api/shares/with-me/${historyId}/versions/${versionNumber}/result`);
  }

  sharedReportUrl(historyId: string, versionNumber: number): string {
    return `/api/shares/with-me/${historyId}/versions/${versionNumber}/report`;
  }
}
