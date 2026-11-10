export type GrantStatus = 'Proposed' | 'Approved' | 'Active' | 'Expired' | 'Revoked' | 'Completed' | 'Declined';
export type GrantAction = 'enter' | 'exit' | 'complete' | 'revoke' | 'approve' | 'decline';
export type SupportOperationCode = 'CreateOrganizationUser' | 'ResetOrganizationUserPassword' | 'ConfigureRole' | 'UpdateTablePermissions' | 'RefreshMetadata' | 'DiagnoseApplicationConnection' | 'UpdateOrganizationSettings';
/** `AssignableRole`: the targets are the roles the operation may give to what it creates (optional; none means no role). */
export type SupportTargetType = 'None' | 'OrganizationUser' | 'Role' | 'RoleOrNew' | 'Application' | 'AssignableRole';

/** Operations whose grant targets are the roles they may assign, so a grant can say which roles those are without the catalog. */
export const ROLE_ASSIGNING_OPERATIONS: readonly SupportOperationCode[] = ['CreateOrganizationUser'];

/** A target reference that names the e-mail address of a user the grant lets support create (approved with the grant). */
export const EMAIL_REFERENCE_PREFIX = 'email:';
export const isEmailReference = (reference: string): boolean => reference.toLowerCase().startsWith(EMAIL_REFERENCE_PREFIX);
export const emailOfReference = (reference: string): string => reference.slice(EMAIL_REFERENCE_PREFIX.length);

export const GRANT_STATUSES: GrantStatus[] = ['Proposed', 'Approved', 'Active', 'Completed', 'Declined', 'Revoked', 'Expired'];

export interface SupportOperationDefinition {
  code: SupportOperationCode;
  targetType: SupportTargetType;
  /** True when a proposal must list a target and every run names one of them. */
  requiresTarget: boolean;
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
  /** The same labels with Arabic role names. */
  targetLabelsAr?: Record<string, string>;
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

export interface SupportTargetObject {
  objectId: string;
  applicationCode: string;
  schemaName: string;
  objectName: string;
}

export interface SupportTargets {
  users: SupportTargetUser[];
  roles: SupportTargetRole[];
  objects: SupportTargetObject[];
}

export interface SupportOperationResult {
  operation: string;
  targetReference?: string;
  outcome: string;
  result?: unknown;
  deliveredTo?: string;
}
