import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AskResponse } from '../platform/ai.models';
import { CollectionSummary, ExportFormat, HistoryDetail, HistoryListQuery, HistorySummary, PagedResult, ReportFile } from './history.models';

/** My History (specification §23): owner-scoped reads; reopening a version returns the stored answer and never re-queries the source. */
@Injectable({ providedIn: 'root' })
export class HistoryService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/history';

  list(query: HistoryListQuery): Observable<PagedResult<HistorySummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize).set('archived', query.archived ?? false);
    if (query.search) params = params.set('search', query.search);
    if (query.applicationId) params = params.set('applicationId', query.applicationId);
    if (query.from) params = params.set('from', query.from);
    if (query.to) params = params.set('to', query.to);
    if (query.favorite !== null && query.favorite !== undefined) params = params.set('favorite', query.favorite);
    if (query.collectionId) params = params.set('collectionId', query.collectionId);
    if (query.type) params = params.set('type', query.type);
    return this.http.get<PagedResult<HistorySummary>>(this.base, { params });
  }

  get(historyId: string): Observable<HistoryDetail> {
    return this.http.get<HistoryDetail>(`${this.base}/${historyId}`);
  }

  versionResult(historyId: string, versionNumber: number): Observable<AskResponse> {
    return this.http.get<AskResponse>(`${this.base}/${historyId}/versions/${versionNumber}/result`);
  }

  versionReportUrl(historyId: string, versionNumber: number): string {
    return `${this.base}/${historyId}/versions/${versionNumber}/report`;
  }

  rename(historyId: string, title: string): Observable<HistorySummary> {
    return this.http.put<HistorySummary>(`${this.base}/${historyId}`, { title });
  }

  setFavorite(historyId: string, isFavorite: boolean): Observable<HistorySummary> {
    return this.http.post<HistorySummary>(`${this.base}/${historyId}/favorite`, { isFavorite });
  }

  setArchived(historyId: string, isArchived: boolean): Observable<HistorySummary> {
    return this.http.post<HistorySummary>(`${this.base}/${historyId}/archive`, { isArchived });
  }

  delete(historyId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${historyId}`);
  }

  rerun(historyId: string): Observable<AskResponse> {
    return this.http.post<AskResponse>(`${this.base}/${historyId}/rerun`, {});
  }

  collections(): Observable<CollectionSummary[]> {
    return this.http.get<CollectionSummary[]>(`${this.base}/collections`);
  }

  createCollection(name: string, description?: string | null): Observable<CollectionSummary> {
    return this.http.post<CollectionSummary>(`${this.base}/collections`, { name, description: description ?? null });
  }

  updateCollection(collectionId: string, name: string, description?: string | null): Observable<CollectionSummary> {
    return this.http.put<CollectionSummary>(`${this.base}/collections/${collectionId}`, { name, description: description ?? null });
  }

  deleteCollection(collectionId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/collections/${collectionId}`);
  }

  addToCollection(collectionId: string, historyId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/collections/${collectionId}/items`, { historyId });
  }

  removeFromCollection(collectionId: string, historyId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/collections/${collectionId}/items/${historyId}`);
  }

  /** Renders a version to a file and stores it in the reports library; the response is the file itself. */
  export(historyId: string, versionNumber: number, format: ExportFormat): Observable<Blob> {
    return this.http.post(`${this.base}/${historyId}/versions/${versionNumber}/export/${format}`, {}, { responseType: 'blob' });
  }

  reports(page: number, pageSize: number, search?: string, format?: ExportFormat | null): Observable<PagedResult<ReportFile>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search) params = params.set('search', search);
    if (format) params = params.set('format', format);
    return this.http.get<PagedResult<ReportFile>>(`${this.base}/reports`, { params });
  }

  artifactUrl(artifactId: string): string {
    return `${this.base}/artifacts/${artifactId}`;
  }
}
