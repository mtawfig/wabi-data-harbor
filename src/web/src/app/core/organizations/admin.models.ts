export interface Department {
  departmentId: string;
  departmentCode: string;
  departmentNameEn: string;
  departmentNameAr: string;
  isActive: boolean;
  sectionCount: number;
  userCount: number;
}

export interface SaveDepartment {
  departmentCode: string;
  departmentNameEn: string;
  departmentNameAr: string;
  isActive: boolean;
}

export interface Section {
  sectionId: string;
  departmentId: string;
  sectionCode: string;
  sectionNameEn: string;
  sectionNameAr: string;
  isActive: boolean;
  userCount: number;
}

export interface SaveSection {
  departmentId: string;
  sectionCode: string;
  sectionNameEn: string;
  sectionNameAr: string;
  isActive: boolean;
}

export interface Role {
  roleId: string;
  roleCode: string;
  nameEn: string;
  nameAr: string;
  description?: string;
  isSystem: boolean;
  isActive: boolean;
  templateVersion?: number;
  memberCount: number;
}

export interface SaveRole {
  roleCode: string;
  nameEn: string;
  nameAr: string;
  description?: string;
  isActive: boolean;
}

export type OrganizationUserStatus = 'PendingInvitation' | 'Active' | 'Inactive';

export interface OrganizationUser {
  userId: string;
  email: string;
  fullName: string;
  employeeNumber?: string;
  phoneNumber?: string;
  departmentId?: string;
  sectionId?: string;
  preferredLanguage: 'en' | 'ar';
  status: OrganizationUserStatus;
  mustChangePassword: boolean;
  lastLoginAt?: string;
  createdAt: string;
  roles: string[];
}

export interface CreateOrganizationUser {
  email: string;
  fullName: string;
  employeeNumber?: string;
  phoneNumber?: string;
  departmentId?: string;
  sectionId?: string;
  preferredLanguage: 'en' | 'ar';
  roleCodes: string[];
}

export type UpdateOrganizationUser = Omit<CreateOrganizationUser, 'email'>;

export interface RoleTemplate {
  templateId: string;
  roleCode: string;
  nameEn: string;
  nameAr: string;
  description?: string;
  isSystem: boolean;
  isActive: boolean;
  sortOrder: number;
  version: number;
  updatedAt: string;
}

export interface SaveRoleTemplate {
  roleCode: string;
  nameEn: string;
  nameAr: string;
  description?: string;
  isActive: boolean;
  sortOrder: number;
}
