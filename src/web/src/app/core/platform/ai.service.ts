import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AiProvider, AiProviderTestResult, AiProviderType, AskResponse, AskableApplication, OrganizationAiSettings, PlanResponse, SaveAiProviderRequest } from './ai.models';

/** AI provider configuration (platform and organization) and the planning endpoint. API keys are only ever sent, never read back. */
@Injectable({ providedIn: 'root' })
export class AiService {
  private readonly http = inject(HttpClient);

  platformProviders(): Observable<AiProvider[]> {
    return this.http.get<AiProvider[]>('/api/platform/ai-providers');
  }

  createPlatformProvider(body: SaveAiProviderRequest): Observable<AiProvider> {
    return this.http.post<AiProvider>('/api/platform/ai-providers', body);
  }

  updatePlatformProvider(id: string, body: SaveAiProviderRequest): Observable<AiProvider> {
    return this.http.put<AiProvider>(`/api/platform/ai-providers/${id}`, body);
  }

  deletePlatformProvider(id: string): Observable<void> {
    return this.http.delete<void>(`/api/platform/ai-providers/${id}`);
  }

  setDefault(id: string): Observable<AiProvider> {
    return this.http.post<AiProvider>(`/api/platform/ai-providers/${id}/default`, {});
  }

  testPlatformProvider(id: string): Observable<AiProviderTestResult> {
    return this.http.post<AiProviderTestResult>(`/api/platform/ai-providers/${id}/test`, {});
  }

  allowedTypes(): Observable<AiProviderType[]> {
    return this.http.get<AiProviderType[]>('/api/platform/ai-providers/allowed-types');
  }

  setAllowedTypes(types: AiProviderType[]): Observable<AiProviderType[]> {
    return this.http.put<AiProviderType[]>('/api/platform/ai-providers/allowed-types', { types });
  }

  organizationSettings(): Observable<OrganizationAiSettings> {
    return this.http.get<OrganizationAiSettings>('/api/ai-providers');
  }

  createOrganizationProvider(body: SaveAiProviderRequest): Observable<AiProvider> {
    return this.http.post<AiProvider>('/api/ai-providers', body);
  }

  updateOrganizationProvider(id: string, body: SaveAiProviderRequest): Observable<AiProvider> {
    return this.http.put<AiProvider>(`/api/ai-providers/${id}`, body);
  }

  deleteOrganizationProvider(id: string): Observable<void> {
    return this.http.delete<void>(`/api/ai-providers/${id}`);
  }

  selectProvider(providerId: string | null): Observable<OrganizationAiSettings> {
    return this.http.put<OrganizationAiSettings>('/api/ai-providers/selection', { providerId });
  }

  testOrganizationProvider(id: string): Observable<AiProviderTestResult> {
    return this.http.post<AiProviderTestResult>(`/api/ai-providers/${id}/test`, {});
  }

  plan(applicationId: string, question: string, language: 'en' | 'ar'): Observable<PlanResponse> {
    return this.http.post<PlanResponse>('/api/ai/plan', { applicationId, question, language });
  }

  askableApplications(): Observable<AskableApplication[]> {
    return this.http.get<AskableApplication[]>('/api/ai/applications');
  }

  ask(applicationId: string, question: string, language: 'en' | 'ar', interpret = true): Observable<AskResponse> {
    return this.http.post<AskResponse>('/api/ai/ask', { applicationId, question, language, interpret });
  }

  /** Download URL of the PDF report of an analysis the signed-in user ran (served by the API with the session cookie). */
  reportUrl(analysisId: string): string {
    return `/api/ai/analyses/${analysisId}/report`;
  }
}
