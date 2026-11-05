export type OrganizationStatus = 'Active' | 'Suspended' | 'Deactivated';

export interface OrganizationDetail {
  organizationId: string;
  organizationCode: string;
  organizationName: string;
  organizationNameAr: string;
  description?: string;
  contactEmail?: string;
  contactPhone?: string;
  countryCode?: string;
  timeZone: string;
  defaultLanguage: 'en' | 'ar';
  status: OrganizationStatus;
  createdAt: string;
  updatedAt?: string;
  userCount: number;
  activeUserCount: number;
  applicationCount: number;
  initialAdministratorEmail?: string;
  initialAdministratorActivated: boolean;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface CreateOrganizationRequest {
  organizationCode: string;
  organizationName: string;
  organizationNameAr: string;
  description?: string;
  contactEmail?: string;
  contactPhone?: string;
  countryCode?: string;
  timeZone: string;
  defaultLanguage: 'en' | 'ar';
  initialAdministratorEmail: string;
  initialAdministratorFullName: string;
}

export type UpdateOrganizationRequest = Omit<CreateOrganizationRequest, 'organizationCode' | 'initialAdministratorEmail' | 'initialAdministratorFullName'>;

export interface OrganizationProfile {
  organizationId: string;
  organizationCode: string;
  organizationName: string;
  organizationNameAr: string;
  description?: string;
  contactEmail?: string;
  contactPhone?: string;
  countryCode?: string;
  timeZone: string;
  defaultLanguage: 'en' | 'ar';
  status: OrganizationStatus;
  createdAt: string;
  requireSecondAdminApprovalForSupportAccess: boolean;
}

export type UpdateOrganizationProfileRequest = Omit<OrganizationProfile, 'organizationId' | 'organizationCode' | 'countryCode' | 'status' | 'createdAt'>;

export const TIME_ZONES = ['UTC', 'Asia/Dubai', 'Asia/Riyadh', 'Asia/Qatar', 'Asia/Kuwait', 'Asia/Bahrain', 'Asia/Muscat', 'Africa/Cairo', 'Asia/Amman', 'Europe/London', 'Europe/Berlin', 'America/New_York'];
