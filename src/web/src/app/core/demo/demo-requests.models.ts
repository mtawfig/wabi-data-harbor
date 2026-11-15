/** Demo requests from the public landing page (P13.52). Statuses, impressions, industries and sizes are stable codes; labels are translated. */
export const DEMO_STATUSES = ['New', 'Contacted', 'Scheduled', 'Performed', 'Converted', 'NotInterested', 'Cancelled', 'Spam'] as const;
export type DemoRequestStatus = (typeof DEMO_STATUSES)[number];

export const DEMO_IMPRESSIONS = ['VeryPositive', 'Positive', 'Neutral', 'Negative'] as const;
export type DemoImpression = (typeof DEMO_IMPRESSIONS)[number];

export const DEMO_INDUSTRIES = [
  'Aviation', 'Banking', 'Government', 'Healthcare', 'Education', 'Energy', 'Retail', 'Logistics', 'Telecom', 'Manufacturing', 'RealEstate', 'Hospitality', 'Technology', 'Other',
] as const;

export const DEMO_ORGANIZATION_SIZES = ['1-50', '51-200', '201-1000', '1001-5000', '5000+'] as const;

/** What the public form sends. `website` is the trap field: people never see it, robots fill it. */
export interface DemoRequestForm {
  fullName: string;
  email: string;
  organizationName: string;
  countryCode: string;
  consent: boolean;
  jobTitle?: string;
  phone?: string;
  industry?: string;
  organizationSize?: string;
  databaseSystems?: string;
  demoLanguage: string;
  preferredTime?: string;
  message?: string;
  pageLanguage: string;
  website?: string;
}

export interface DemoRequestListItem {
  demoRequestId: string;
  fullName: string;
  email: string;
  organizationName: string;
  countryCode: string;
  jobTitle?: string;
  status: DemoRequestStatus;
  createdAt: string;
  scheduledAt?: string;
  performedAt?: string;
  assignedAdministratorName?: string;
  repeatCount: number;
  erased: boolean;
}

export interface DemoRequestEvent {
  eventId: string;
  eventType: 'Submitted' | 'Repeated' | 'StatusChanged' | 'Updated';
  previousStatus?: DemoRequestStatus;
  newStatus?: DemoRequestStatus;
  note?: string;
  actorDisplayName?: string;
  createdAt: string;
}

/** The follow-up an administrator saves: the status, the schedule and the demo as it was performed. */
export interface DemoRequestFollowUp {
  status: DemoRequestStatus;
  remark?: string | null;
  assignedAdministratorId?: string | null;
  scheduledAt?: string | null;
  meetingLocation?: string | null;
  performedAt?: string | null;
  presentedBy?: string | null;
  attendeeName?: string | null;
  attendeePosition?: string | null;
  attendeeEmail?: string | null;
  attendeePhone?: string | null;
  attendeeMobile?: string | null;
  impression?: DemoImpression | null;
  performedRemark?: string | null;
  nextStep?: string | null;
  followUpDate?: string | null;
}

export interface DemoRequestDetail extends DemoRequestFollowUp {
  demoRequestId: string;
  fullName: string;
  email: string;
  organizationName: string;
  countryCode: string;
  jobTitle?: string;
  phone?: string;
  industry?: string;
  organizationSize?: string;
  databaseSystems?: string;
  demoLanguage: string;
  preferredTime?: string;
  message?: string;
  pageLanguage: string;
  consentAt: string;
  ipAddress?: string;
  userAgent?: string;
  createdAt: string;
  repeatCount: number;
  lastRepeatedAt?: string;
  assignedAdministratorName?: string;
  updatedAt?: string;
  erasedAt?: string;
  events: DemoRequestEvent[];
}

export interface DemoRequestSummary {
  new: number;
  open: number;
  total: number;
}

export interface DemoAssignee {
  administratorId: string;
  fullName: string;
}

export interface DemoRequestPage {
  items: DemoRequestListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}
