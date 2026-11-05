import { MetricBucket } from './platform.models';

export type MetricAggregate = 'Count' | 'CountDistinct' | 'Sum' | 'Average' | 'Min' | 'Max';
export const METRIC_AGGREGATES: MetricAggregate[] = ['Count', 'CountDistinct', 'Sum', 'Average', 'Min', 'Max'];
export type FilterOperator = 'eq' | 'neq' | 'in' | 'gt' | 'gte' | 'lt' | 'lte';
export const FILTER_OPERATORS: FilterOperator[] = ['eq', 'neq', 'in', 'gt', 'gte', 'lt', 'lte'];

export interface MetricFilter {
  column: string;
  operator: FilterOperator;
  values: string[];
}

export interface InsightsColumn {
  columnId: string;
  columnName: string;
  dataType: string;
  isNullable: boolean;
  ordinalPosition: number;
  displayNameEn: string;
  displayNameAr: string;
  description?: string | null;
  isApproved: boolean;
  isPresent: boolean;
}

export interface InsightsObject {
  objectId: string;
  viewName: string;
  displayNameEn: string;
  displayNameAr: string;
  description?: string | null;
  isApproved: boolean;
  isPresent: boolean;
  sortOrder: number;
  updatedAt: string;
  columns: InsightsColumn[];
}

export interface InsightsMetric {
  metricId: string;
  metricCode: string;
  objectId: string;
  viewName: string;
  displayNameEn: string;
  displayNameAr: string;
  description?: string | null;
  aggregate: MetricAggregate;
  columnName?: string | null;
  dateColumn?: string | null;
  filters: MetricFilter[];
  unit: string;
  isApproved: boolean;
  sortOrder: number;
  updatedAt: string;
}

export interface InsightsSourceStatus {
  dedicatedLoginConfigured: boolean;
  reachable: boolean;
  confinedToInsightsSchema?: boolean | null;
  viewCount: number;
  warnings: string[];
  error?: string | null;
  checkedAt: string;
}

export interface InsightsCatalog {
  source: InsightsSourceStatus;
  objects: InsightsObject[];
  metrics: InsightsMetric[];
}

export interface InsightsSyncResult {
  objectsAdded: number;
  objectsRemoved: number;
  columnsAdded: number;
  columnsRemoved: number;
  columnsChanged: number;
}

export interface SaveInsightsMetricRequest {
  metricCode: string;
  objectId: string;
  displayNameEn: string;
  displayNameAr: string;
  description?: string | null;
  aggregate: MetricAggregate;
  columnName?: string | null;
  dateColumn?: string | null;
  filters: MetricFilter[];
  unit: string;
  isApproved: boolean;
  sortOrder: number;
}

export interface MetricQueryRequest {
  from?: string | null;
  to?: string | null;
  bucket?: MetricBucket | null;
  groupBy?: string | null;
  filters?: MetricFilter[] | null;
}

export interface SqlPreview {
  sql: string;
  parameters: Record<string, string>;
}

export interface OrganizationInsightsCatalog {
  objects: InsightsObject[];
  metrics: InsightsMetric[];
}

export interface MetricRow {
  bucket?: string | null;
  group?: string | null;
  value: number;
}

export interface MetricResult {
  metricCode: string;
  unit: string;
  rows: MetricRow[];
  durationMs: number;
}

export interface DashboardTableRow {
  key: string;
  label: string;
  labelAr?: string | null;
  value: number;
  secondary?: number | null;
}

export interface DashboardApplicationRow {
  applicationId: string;
  applicationCode: string;
  applicationName: string;
  applicationNameAr: string;
  databaseType: string;
  isBuiltIn: boolean;
  isApproved: boolean;
  isActive: boolean;
  objectCount: number;
  approvedObjectCount: number;
  lastMetadataRefreshAt?: string | null;
}

export interface OrganizationDashboard {
  period: { from: string; to: string };
  kpis: { code: string; value: number; collected: boolean }[];
  series: { code: string; points: { bucket: string; value: number }[] }[];
  departmentUsage: DashboardTableRow[];
  ticketsByStatus: DashboardTableRow[];
  applications: DashboardApplicationRow[];
}

export interface RoleMetricGrants {
  roleId: string;
  roleCode: string;
  isAutomatic: boolean;
  metrics: InsightsMetric[];
  grantedMetricIds: string[];
}
