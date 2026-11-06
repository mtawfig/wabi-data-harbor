import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateOrganizationRequest,
  OrganizationDetail,
  OrganizationProfile,
  OrganizationStatus,
  PagedResult,
  UpdateOrganizationProfileRequest,
  UpdateOrganizationRequest,
} from './organizations.models';

/** Platform Administrator operations on organizations. Responses carry counts, never application names. */
@Injectable({ providedIn: 'root' })
export class OrganizationsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/platform/organizations';

  list(page: number, pageSize: number, search?: string, status?: OrganizationStatus | null): Observable<PagedResult<OrganizationDetail>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search) {
      params = params.set('search', search);
    }
    if (status) {
      params = params.set('status', status);
    }
    return this.http.get<PagedResult<OrganizationDetail>>(this.base, { params });
  }

  get(id: string): Observable<OrganizationDetail> {
    return this.http.get<OrganizationDetail>(`${this.base}/${id}`);
  }

  create(request: CreateOrganizationRequest): Observable<OrganizationDetail> {
    return this.http.post<OrganizationDetail>(this.base, request);
  }

  update(id: string, request: UpdateOrganizationRequest): Observable<OrganizationDetail> {
    return this.http.put<OrganizationDetail>(`${this.base}/${id}`, request);
  }

  setStatus(id: string, status: OrganizationStatus, reason?: string): Observable<OrganizationDetail> {
    return this.http.post<OrganizationDetail>(`${this.base}/${id}/status`, { status, reason });
  }

  resendInitialAdministrator(id: string): Observable<{ organizationId: string; deliveredTo: string }> {
    return this.http.post<{ organizationId: string; deliveredTo: string }>(`${this.base}/${id}/initial-administrator/resend`, {});
  }
}

/** The signed-in user's own organization (tenant context on the API side). */
@Injectable({ providedIn: 'root' })
export class OrganizationProfileService {
  private readonly http = inject(HttpClient);

  get(): Observable<OrganizationProfile> {
    return this.http.get<OrganizationProfile>('/api/organization');
  }

  update(request: UpdateOrganizationProfileRequest): Observable<OrganizationProfile> {
    return this.http.put<OrganizationProfile>('/api/organization', request);
  }

  /** The logo stamped on report pages; the URL carries a version so a new upload is not served from cache. */
  logoUrl(version: number): string {
    return `/api/organization/logo?v=${version}`;
  }

  uploadLogo(file: File): Observable<void> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.put<void>('/api/organization/logo', form);
  }

  removeLogo(): Observable<void> {
    return this.http.delete<void>('/api/organization/logo');
  }
}
