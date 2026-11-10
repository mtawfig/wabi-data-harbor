export interface MetricValue {
  code: string;
  value: number;
  collected: boolean;
}

export interface MetricPoint {
  bucket: string;
  value: number;
}

export interface MetricSeries {
  code: string;
  points: MetricPoint[];
}

export interface OrganizationUsageRow {
  organizationId: string;
  organizationCode: string;
  organizationName: string;
  status: string;
  users: number;
  activeUsersInPeriod: number;
  logins: number;
  emailsSent: number;
  emailsFailed: number;
}

export interface PlatformErrorRow {
  at: string;
  eventType: string;
  organizationId?: string;
  detail?: string;
}

export interface PlatformDashboard {
  period: { from: string; to: string };
  kpis: MetricValue[];
  series: MetricSeries[];
  organizationUsage: OrganizationUsageRow[];
  recentErrors: PlatformErrorRow[];
}

export type MetricBucket = 'Day' | 'Week' | 'Month';

export interface HealthCheckEntry {
  name: string;
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  description?: string;
  durationMs: number;
  data: Record<string, unknown>;
}

export interface PlatformHealthReport {
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  checkedAt: string;
  totalDurationMs: number;
  checks: HealthCheckEntry[];
}

export interface AuditLogRow {
  auditLogId: string;
  createdAt: string;
  organizationId?: string;
  actorType: 'Anonymous' | 'PlatformAdministrator' | 'OrganizationUser';
  actorId?: string;
  actorDisplayName: string;
  eventType: string;
  entityType?: string;
  entityId?: string;
  detailsJson?: string;
  correlationId?: string;
  ipAddress?: string;
  /** True when part of the details was withheld: the row belongs to an organization the reader holds no approved grant for. */
  detailsReduced?: boolean;
}

export interface AuditQuery {
  page: number;
  pageSize: number;
  from?: string;
  to?: string;
  eventType?: string | null;
  organizationId?: string | null;
  actorType?: string | null;
  search?: string;
}

export interface Administrator {
  administratorId: string;
  email: string;
  fullName: string;
  isActive: boolean;
  mustChangePassword: boolean;
  isBootstrapAdministrator: boolean;
  preferredLanguage: 'en' | 'ar';
  lastLoginAt?: string;
  createdAt: string;
}
