import { HttpClient, HttpParams } from '@angular/common/http';
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
  /** False while the plan and its limit are not shown to organizations (P13.57): the plan fields are then empty. */
  planShown?: boolean;
}

export interface AiSpendingFilter {
  from?: string | null;
  to?: string | null;
  userId?: string | null;
  minSpend?: number | null;
  maxSpend?: number | null;
  successfulOnly?: boolean;
}

export interface AiSpendingUserRow {
  userId: string;
  fullName: string;
  employeeNumber?: string | null;
  departmentNameEn?: string | null;
  departmentNameAr?: string | null;
  isActive: boolean;
  requests: number;
  spend: number;
  sharePercent: number;
  /** Tokens sent to and written by the model for this employee's requests (P13.48). */
  inputTokens: number;
  outputTokens: number;
}

export interface AiSpendingMonthlyRow {
  userId: string;
  fullName: string;
  spend: number[];
  total: number;
}

/** The Organization Administrator's spending report: amounts and counts only. */
export interface AiSpendingReport {
  state: AiSpendingState;
  planCode?: string | null;
  planNameEn?: string | null;
  planNameAr?: string | null;
  monthlyAiSpendLimit?: number | null;
  monthSpend: number;
  percentUsed?: number | null;
  /** False while the plan and its limit are not shown to organizations (P13.57). */
  planShown?: boolean;
  from: string;
  to: string;
  totalRequests: number;
  totalSpend: number;
  rows: AiSpendingUserRow[];
  months: string[];
  monthly: AiSpendingMonthlyRow[];
  monthlyTotals: number[];
  users: { userId: string; fullName: string }[];
  totalInputTokens: number;
  totalOutputTokens: number;
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

  /** Per-employee AI spending of the signed-in administrator's organization; empty filters are not sent. */
  report(filter: AiSpendingFilter = {}): Observable<AiSpendingReport> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== null && value !== undefined && value !== '' && value !== false) {
        params = params.set(key, String(value));
      }
    }
    return this.http.get<AiSpendingReport>('/api/ai/spending/report', { params });
  }

  /** The signed-in user's organization against its plan's limit. */
  spending(): Observable<AiSpending> {
    return this.http.get<AiSpending>('/api/ai/spending');
  }
}
