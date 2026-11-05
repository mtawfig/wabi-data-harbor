import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { InsightsCatalog, InsightsColumn, InsightsMetric, InsightsObject, InsightsSourceStatus, InsightsSyncResult, MetricQueryRequest, MetricResult, OrganizationDashboard, OrganizationInsightsCatalog, RoleMetricGrants, SaveInsightsMetricRequest, SqlPreview } from './insights.models';
import { MetricBucket } from './platform.models';

/** Platform Insights Configuration (platform side) and the organization-side approved catalog and metric queries. */
@Injectable({ providedIn: 'root' })
export class InsightsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/platform/insights-catalog';

  catalog(): Observable<InsightsCatalog> {
    return this.http.get<InsightsCatalog>(this.base);
  }

  source(): Observable<InsightsSourceStatus> {
    return this.http.get<InsightsSourceStatus>(`${this.base}/source`);
  }

  sync(): Observable<InsightsSyncResult> {
    return this.http.post<InsightsSyncResult>(`${this.base}/sync`, {});
  }

  updateObject(id: string, body: { displayNameEn: string; displayNameAr: string; description?: string | null; isApproved: boolean; sortOrder: number }): Observable<InsightsObject> {
    return this.http.put<InsightsObject>(`${this.base}/objects/${id}`, body);
  }

  updateColumn(id: string, body: { displayNameEn: string; displayNameAr: string; description?: string | null; isApproved: boolean }): Observable<InsightsColumn> {
    return this.http.put<InsightsColumn>(`${this.base}/columns/${id}`, body);
  }

  createMetric(body: SaveInsightsMetricRequest): Observable<InsightsMetric> {
    return this.http.post<InsightsMetric>(`${this.base}/metrics`, body);
  }

  updateMetric(id: string, body: SaveInsightsMetricRequest): Observable<InsightsMetric> {
    return this.http.put<InsightsMetric>(`${this.base}/metrics/${id}`, body);
  }

  deleteMetric(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/metrics/${id}`);
  }

  previewSql(id: string, request: MetricQueryRequest): Observable<SqlPreview> {
    return this.http.post<SqlPreview>(`${this.base}/metrics/${id}/sql`, request);
  }

  /** Organization side: approved catalog and a metric query, always scoped to the caller's organization by the API. */
  organizationCatalog(): Observable<OrganizationInsightsCatalog> {
    return this.http.get<OrganizationInsightsCatalog>('/api/insights/catalog');
  }

  queryMetric(code: string, request: MetricQueryRequest): Observable<MetricResult> {
    return this.http.post<MetricResult>(`/api/insights/metrics/${encodeURIComponent(code)}/query`, request);
  }

  organizationDashboard(from: Date, to: Date, bucket: MetricBucket): Observable<OrganizationDashboard> {
    const params = new HttpParams().set('from', from.toISOString()).set('to', to.toISOString()).set('bucket', bucket);
    return this.http.get<OrganizationDashboard>('/api/insights/dashboard', { params });
  }

  roleMetrics(roleId: string): Observable<RoleMetricGrants> {
    return this.http.get<RoleMetricGrants>(`/api/insights/roles/${roleId}/metrics`);
  }

  setRoleMetrics(roleId: string, metricIds: string[]): Observable<RoleMetricGrants> {
    return this.http.put<RoleMetricGrants>(`/api/insights/roles/${roleId}/metrics`, { metricIds });
  }
}
