import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** A service plan with its monthly AI spending limit in USD (null: no limit). */
export interface ServicePlan {
  planId: string;
  planCode: string;
  nameEn: string;
  nameAr: string;
  monthlyAiSpendLimit?: number | null;
  isActive: boolean;
  sortOrder: number;
  organizationCount: number;
  updatedAt: string;
}

export interface SaveServicePlanRequest {
  planCode: string;
  nameEn: string;
  nameAr: string;
  monthlyAiSpendLimit: number | null;
  isActive: boolean;
  sortOrder: number;
}

export type AiSpendingState = 'unlimited' | 'within' | 'near' | 'reached';

/** An organization's plan and the month's AI spending (Platform Administrators). */
export interface OrganizationPlan {
  organizationId: string;
  planId?: string | null;
  planCode?: string | null;
  nameEn?: string | null;
  nameAr?: string | null;
  monthlyAiSpendLimit?: number | null;
  monthSpend: number;
  state: AiSpendingState;
}

/** What an organization user is told: the state for everyone, the figures for Organization Administrators. */
export interface AiSpending {
  state: AiSpendingState;
  planCode?: string | null;
  nameEn?: string | null;
  nameAr?: string | null;
  monthlyAiSpendLimit?: number | null;
  monthSpend?: number | null;
  percentUsed?: number | null;
}

@Injectable({ providedIn: 'root' })
export class PlansService {
  private readonly http = inject(HttpClient);

  list(): Observable<ServicePlan[]> {
    return this.http.get<ServicePlan[]>('/api/platform/plans');
  }

  create(request: SaveServicePlanRequest): Observable<ServicePlan> {
    return this.http.post<ServicePlan>('/api/platform/plans', request);
  }

  update(id: string, request: SaveServicePlanRequest): Observable<ServicePlan> {
    return this.http.put<ServicePlan>(`/api/platform/plans/${id}`, request);
  }

  organizationPlan(organizationId: string): Observable<OrganizationPlan> {
    return this.http.get<OrganizationPlan>(`/api/platform/plans/organizations/${organizationId}`);
  }

  assign(organizationId: string, planId: string | null): Observable<OrganizationPlan> {
    return this.http.put<OrganizationPlan>(`/api/platform/plans/organizations/${organizationId}`, { planId });
  }

  /** The signed-in user's organization against its plan's limit. */
  spending(): Observable<AiSpending> {
    return this.http.get<AiSpending>('/api/ai/spending');
  }
}
