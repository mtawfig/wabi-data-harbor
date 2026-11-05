export type GrantStatus = 'Proposed' | 'Approved' | 'Active' | 'Expired' | 'Revoked' | 'Completed' | 'Declined';
export type GrantAction = 'enter' | 'exit' | 'complete' | 'revoke' | 'approve' | 'decline';
export type SupportOperationCode = 'CreateOrganizationUser' | 'ResetOrganizationUserPassword' | 'ConfigureRole' | 'UpdateTablePermissions' | 'RefreshMetadata' | 'DiagnoseApplicationConnection' | 'UpdateOrganizationSettings';
export type SupportTargetType = 'None' | 'OrganizationUser' | 'Role' | 'RoleOrNew' | 'Application';

export const GRANT_STATUSES: GrantStatus[] = ['Proposed', 'Approved', 'Active', 'Completed', 'Declined', 'Revoked', 'Expired'];

export interface SupportOperationDefinition {
  code: SupportOperationCode;
  targetType: SupportTargetType;
  executable: boolean;
  description: string;
}

export interface SupportOperationEntry {
  operation: SupportOperationCode;
  targetReferences: string[];
}

export interface SupportAccessGrant {
  grantId: string;
  ticketId: string;
  ticketReference: string;
  ticketSubject: string;
  organizationId: string;
  organizationName: string;
  organizationNameAr: string;
  status: GrantStatus;
  operations: SupportOperationEntry[];
  targetLabels: Record<string, string>;
  reason: string;
  decisionNote?: string;
  requestedByUserId: string;
  requestedByName: string;
  proposedByAdministratorId: string;
  proposedByName: string;
  approvedByOrganizationAdminUserId?: string;
  approvedByName?: string;
  assignedPlatformAdministratorId: string;
  assignedAdministratorName: string;
  approvedAt?: string;
  expiresAt?: string;
  revokedAt?: string;
  completedAt?: string;
  createdAt: string;
  sessionActive: boolean;
  operationsExecuted: number;
  allowedActions: GrantAction[];
}

export interface SupportSession {
  sessionId: string;
  grant: SupportAccessGrant;
  startedAt: string;
  expiresAt: string;
  operations: SupportOperationEntry[];
}

export interface SupportTargetUser {
  userId: string;
  fullName: string;
  email: string;
  isActive: boolean;
}

export interface SupportTargetRole {
  roleId: string;
  roleCode: string;
  nameEn: string;
  nameAr: string;
  isSystem: boolean;
  isActive: boolean;
}

export interface SupportTargets {
  users: SupportTargetUser[];
  roles: SupportTargetRole[];
}

export interface SupportOperationResult {
  operation: string;
  targetReference?: string;
  outcome: string;
  result?: unknown;
  deliveredTo?: string;
}
