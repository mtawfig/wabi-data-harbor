export type TicketType = 'Request' | 'Incident';
export type TicketImpact = 'Low' | 'Medium' | 'High' | 'Critical';
export type TicketStatus = 'New' | 'Read' | 'Started' | 'PendingUserResponse' | 'Cancelled' | 'Resolved';
export type PrincipalType = 'Anonymous' | 'PlatformAdministrator' | 'OrganizationUser';

export const TICKET_TYPES: TicketType[] = ['Request', 'Incident'];
export const TICKET_IMPACTS: TicketImpact[] = ['Low', 'Medium', 'High', 'Critical'];
export const TICKET_STATUSES: TicketStatus[] = ['New', 'Read', 'Started', 'PendingUserResponse', 'Resolved', 'Cancelled'];
export const OPEN_STATUSES: TicketStatus[] = ['New', 'Read', 'Started', 'PendingUserResponse'];

export interface TicketSummary {
  ticketId: string;
  referenceNumber: string;
  organizationId: string;
  organizationCode: string;
  organizationName: string;
  organizationNameAr: string;
  ticketType: TicketType;
  impact: TicketImpact;
  category?: string;
  subject: string;
  status: TicketStatus;
  createdByUserId: string;
  createdByName: string;
  assignedAdministratorId?: string;
  assignedAdministratorName?: string;
  requiresSupportAccess: boolean;
  createdAt: string;
  lastActivityAt: string;
  commentCount: number;
  attachmentCount: number;
}

export interface TicketDetail {
  summary: TicketSummary;
  description: string;
  firstOpenedByAdministratorId?: string;
  firstOpenedAt?: string;
  startedAt?: string;
  pendingResponseAt?: string;
  resolvedAt?: string;
  cancelledAt?: string;
  reopenedCount: number;
  resolutionSummary?: string;
  updatedAt?: string;
  allowedTransitions: TicketStatus[];
  canComment: boolean;
  canAssign: boolean;
}

export interface TicketEvent {
  eventId: string;
  actorType: PrincipalType;
  actorId?: string;
  actorDisplayName: string;
  eventType: string;
  previousStatus?: TicketStatus;
  newStatus?: TicketStatus;
  detailsJson?: string;
  note?: string;
  createdAt: string;
}

export interface TicketComment {
  commentId: string;
  authorType: PrincipalType;
  authorId?: string;
  authorDisplayName: string;
  body: string;
  createdAt: string;
}

export interface TicketAttachment {
  attachmentId: string;
  commentId?: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedByType: PrincipalType;
  uploadedByDisplayName: string;
  createdAt: string;
}

export interface CreateTicketRequest {
  ticketType: TicketType;
  impact: TicketImpact;
  category?: string;
  subject: string;
  description: string;
  requiresSupportAccess: boolean;
}

export interface TicketListQuery {
  page: number;
  pageSize: number;
  search?: string;
  status?: TicketStatus | null;
  open?: boolean | null;
  ticketType?: TicketType | null;
  impact?: TicketImpact | null;
  organizationId?: string | null;
  assignedAdministratorId?: string | null;
  unassigned?: boolean | null;
  sort?: string;
  descending?: boolean;
}

/** Which API prefix a page talks to: the organization area or the platform queue. */
export type SupportArea = 'app' | 'platform';

export const ALLOWED_ATTACHMENT_EXTENSIONS = ['pdf', 'png', 'jpg', 'jpeg', 'xlsx', 'docx', 'csv', 'txt', 'log', 'zip'];
export const MAX_ATTACHMENT_BYTES = 10 * 1024 * 1024;
