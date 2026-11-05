import { DataClassification, DatabaseObjectType } from './applications.models';

/** Row scopes (specification §10). Codes are stable; labels live in i18n. */
export type RowScope = 'All' | 'Department' | 'Section' | 'Self';
export const ROW_SCOPES: RowScope[] = ['All', 'Department', 'Section', 'Self'];

export interface ColumnGrant {
  columnId: string;
  columnName: string;
  dataType: string;
  classification: DataClassification;
  granted: boolean;
}

export interface MappingAvailability {
  employee: boolean;
  department: boolean;
  section: boolean;
}

export interface ObjectGrant {
  objectId: string;
  applicationId: string;
  schemaName: string;
  objectName: string;
  objectType: DatabaseObjectType;
  displayNameEn?: string;
  displayNameAr?: string;
  isApproved: boolean;
  isPresent: boolean;
  granted: boolean;
  allColumns: boolean;
  rowScope?: RowScope | null;
  columns: ColumnGrant[];
  mapping: MappingAvailability;
}

export interface RoleMatrix {
  roleId: string;
  roleCode: string;
  isSystemManagement: boolean;
  applicationId: string;
  objects: ObjectGrant[];
}

export interface SetObjectGrantRequest {
  granted: boolean;
  allColumns?: boolean;
  columnIds?: string[];
  rowScope?: RowScope | null;
}

export interface RowMapping {
  objectId: string;
  employeeColumnId?: string | null;
  departmentColumnId?: string | null;
  sectionColumnId?: string | null;
  columns: ColumnGrant[];
}

export interface SetRowMappingRequest {
  employeeColumnId?: string | null;
  departmentColumnId?: string | null;
  sectionColumnId?: string | null;
}

export interface EffectiveColumn {
  columnId: string;
  columnName: string;
  dataType: string;
  displayNameEn?: string;
  displayNameAr?: string;
  classification: DataClassification;
}

export interface EffectiveObjectAccess {
  applicationId: string;
  applicationCode: string;
  objectId: string;
  schemaName: string;
  objectName: string;
  objectType: DatabaseObjectType;
  displayNameEn?: string;
  displayNameAr?: string;
  columns: EffectiveColumn[];
  rowScopes: RowScope[];
  unmappedScopes: RowScope[];
  yieldsRows: boolean;
  viaRoles: string[];
}

export interface EffectivePermissions {
  userId: string;
  organizationId: string;
  roles: string[];
  context: { employeeNumber?: string | null; departmentCode?: string | null; sectionCode?: string | null };
  objects: EffectiveObjectAccess[];
}
