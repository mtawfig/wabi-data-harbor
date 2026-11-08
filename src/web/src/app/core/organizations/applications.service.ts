import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Application, ColumnInfo, ConnectionTestResult, CreateApplicationRequest, DatabaseType, MetadataRefreshResult, ObjectDetail, ObjectSummary, UpdateApplicationRequest, UpdateColumnRequest, UpdateObjectRequest } from './applications.models';
import { PagedResult } from './organizations.models';

/** Applications and metadata of the signed-in administrator's organization. Connection strings are only ever sent, never read back. */
@Injectable({ providedIn: 'root' })
export class ApplicationsService {
  private readonly http = inject(HttpClient);

  list(includeInactive = true): Observable<Application[]> {
    return this.http.get<Application[]>('/api/applications', { params: new HttpParams().set('includeInactive', includeInactive) });
  }

  get(id: string): Observable<Application> {
    return this.http.get<Application>(`/api/applications/${id}`);
  }

  create(request: CreateApplicationRequest): Observable<Application> {
    return this.http.post<Application>('/api/applications', request);
  }

  update(id: string, request: UpdateApplicationRequest): Observable<Application> {
    return this.http.put<Application>(`/api/applications/${id}`, request);
  }

  setExamples(id: string, examples: { en?: string | null; ar?: string | null }[]): Observable<Application> {
    return this.http.put<Application>(`/api/applications/${id}/examples`, { examples });
  }

  replaceConnection(id: string, connectionString: string, connectionHint?: string): Observable<Application> {
    return this.http.put<Application>(`/api/applications/${id}/connection`, { connectionString, connectionHint: connectionHint || null });
  }

  testConnection(databaseType: DatabaseType, connectionString: string): Observable<ConnectionTestResult> {
    return this.http.post<ConnectionTestResult>('/api/applications/test-connection', { databaseType, connectionString });
  }

  testStored(id: string): Observable<ConnectionTestResult> {
    return this.http.post<ConnectionTestResult>(`/api/applications/${id}/test-connection`, {});
  }

  setApproval(id: string, isApproved: boolean): Observable<Application> {
    return this.http.post<Application>(`/api/applications/${id}/approval`, { isApproved });
  }

  refreshMetadata(applicationId: string): Observable<MetadataRefreshResult> {
    return this.http.post<MetadataRefreshResult>(`/api/metadata/applications/${applicationId}/refresh`, {});
  }

  objects(applicationId: string, page: number, pageSize: number, search?: string, approved?: boolean | null, includeRemoved = false): Observable<PagedResult<ObjectSummary>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize).set('includeRemoved', includeRemoved);
    if (search) {
      params = params.set('search', search);
    }
    if (approved !== null && approved !== undefined) {
      params = params.set('approved', approved);
    }
    return this.http.get<PagedResult<ObjectSummary>>(`/api/metadata/applications/${applicationId}/objects`, { params });
  }

  object(objectId: string): Observable<ObjectDetail> {
    return this.http.get<ObjectDetail>(`/api/metadata/objects/${objectId}`);
  }

  updateObject(objectId: string, request: UpdateObjectRequest): Observable<ObjectDetail> {
    return this.http.put<ObjectDetail>(`/api/metadata/objects/${objectId}`, request);
  }

  approveObject(objectId: string, approve: boolean): Observable<ObjectDetail> {
    return this.http.post<ObjectDetail>(`/api/metadata/objects/${objectId}/approve`, { approve });
  }

  updateColumn(columnId: string, request: UpdateColumnRequest): Observable<ColumnInfo> {
    return this.http.put<ColumnInfo>(`/api/metadata/columns/${columnId}`, request);
  }
}
