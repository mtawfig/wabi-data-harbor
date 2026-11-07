import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AlertDetail,
  AlertPage,
  AlertSeverity,
  AlertStatus,
  DigestPreview,
  MonitoringRule,
  MonitoringSettings,
  OwnerOption,
  RuleTemplate,
  RuleTestResult,
  SaveMonitoringRule,
} from './monitoring.models';

/** Monitoring Rules and Alerts (Phase 14, specification §35.1–35.2). */
@Injectable({ providedIn: 'root' })
export class MonitoringService {
  private readonly http = inject(HttpClient);

  templates(ownerUserId?: string | null): Observable<RuleTemplate[]> {
    const params = ownerUserId ? new HttpParams().set('ownerUserId', ownerUserId) : undefined;
    return this.http.get<RuleTemplate[]>('/api/monitoring/templates', { params });
  }

  owners(): Observable<OwnerOption[]> {
    return this.http.get<OwnerOption[]>('/api/monitoring/owners');
  }

  rules(): Observable<MonitoringRule[]> {
    return this.http.get<MonitoringRule[]>('/api/monitoring/rules');
  }

  createRule(request: SaveMonitoringRule): Observable<MonitoringRule> {
    return this.http.post<MonitoringRule>('/api/monitoring/rules', request);
  }

  updateRule(ruleId: string, request: SaveMonitoringRule): Observable<MonitoringRule> {
    return this.http.put<MonitoringRule>(`/api/monitoring/rules/${ruleId}`, request);
  }

  setActive(ruleId: string, active: boolean): Observable<MonitoringRule> {
    return this.http.post<MonitoringRule>(`/api/monitoring/rules/${ruleId}/${active ? 'resume' : 'pause'}`, {});
  }

  deleteRule(ruleId: string): Observable<void> {
    return this.http.delete<void>(`/api/monitoring/rules/${ruleId}`);
  }

  testRule(ruleId: string): Observable<RuleTestResult> {
    return this.http.post<RuleTestResult>(`/api/monitoring/rules/${ruleId}/test`, {});
  }

  testDraft(request: SaveMonitoringRule): Observable<RuleTestResult> {
    return this.http.post<RuleTestResult>('/api/monitoring/rules/test', request);
  }

  settings(): Observable<MonitoringSettings> {
    return this.http.get<MonitoringSettings>('/api/monitoring/settings');
  }

  saveSettings(request: Omit<MonitoringSettings, 'maxRetentionMonths' | 'timeZone'>): Observable<MonitoringSettings> {
    return this.http.put<MonitoringSettings>('/api/monitoring/settings', request);
  }

  alerts(query: { status?: AlertStatus | null; severity?: AlertSeverity | null; ruleId?: string | null; page?: number; pageSize?: number }): Observable<AlertPage> {
    let params = new HttpParams().set('page', query.page ?? 1).set('pageSize', query.pageSize ?? 25);
    if (query.status) {
      params = params.set('status', query.status);
    }
    if (query.severity) {
      params = params.set('severity', query.severity);
    }
    if (query.ruleId) {
      params = params.set('ruleId', query.ruleId);
    }
    return this.http.get<AlertPage>('/api/monitoring/alerts', { params });
  }

  alert(alertId: string): Observable<AlertDetail> {
    return this.http.get<AlertDetail>(`/api/monitoring/alerts/${alertId}`);
  }

  act(alertId: string, action: 'acknowledge' | 'resolve' | 'dismiss' | 'reopen' | 'suspend-user', note?: string | null): Observable<AlertDetail> {
    return this.http.post<AlertDetail>(`/api/monitoring/alerts/${alertId}/${action}`, { note: note ?? null });
  }

  digestPreview(): Observable<DigestPreview> {
    return this.http.get<DigestPreview>('/api/monitoring/alerts/digest-preview');
  }
}
