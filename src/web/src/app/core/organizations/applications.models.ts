export type DatabaseType = 'SqlServer' | 'Oracle' | 'PlatformInsights';
export type DatabaseObjectType = 'Table' | 'View';
export type DataClassification = 'Internal' | 'Confidential' | 'Restricted';

export const DATABASE_TYPES: DatabaseType[] = ['SqlServer', 'Oracle'];
export const CLASSIFICATIONS: DataClassification[] = ['Internal', 'Confidential', 'Restricted'];

export interface Application {
  applicationId: string;
  applicationCode: string;
  applicationName: string;
  applicationNameAr: string;
  description?: string;
  databaseType: DatabaseType;
  isBuiltIn: boolean;
  isActive: boolean;
  isApproved: boolean;
  connectionConfigured: boolean;
  connectionHint?: string;
  lastConnectionTestAt?: string;
  lastConnectionTestSucceeded?: boolean;
  lastMetadataRefreshAt?: string;
  objectCount: number;
  approvedObjectCount: number;
  createdAt: string;
  updatedAt?: string;
  exampleQuestions?: { en?: string | null; ar?: string | null }[];
}

export interface CreateApplicationRequest {
  applicationCode: string;
  applicationName: string;
  applicationNameAr: string;
  description?: string;
  databaseType: DatabaseType;
  connectionString?: string;
  connectionHint?: string;
}

export interface UpdateApplicationRequest {
  applicationName: string;
  applicationNameAr: string;
  description?: string;
  isActive: boolean;
}

export interface ConnectionTestResult {
  succeeded: boolean;
  serverVersion?: string;
  databaseName?: string;
  readOnlyVerified?: boolean;
  warnings: string[];
  error?: string;
  durationMs: number;
}

export interface ObjectSummary {
  objectId: string;
  applicationId: string;
  schemaName: string;
  objectName: string;
  objectType: DatabaseObjectType;
  displayNameEn?: string;
  displayNameAr?: string;
  isApproved: boolean;
  isPresent: boolean;
  columnCount: number;
  approvedColumnCount: number;
  discoveredAt: string;
  lastSeenAt: string;
}

export interface ColumnInfo {
  columnId: string;
  columnName: string;
  dataType: string;
  maxLength?: number;
  precision?: number;
  scale?: number;
  isNullable: boolean;
  isPrimaryKey: boolean;
  ordinalPosition: number;
  displayNameEn?: string;
  displayNameAr?: string;
  businessDescription?: string;
  classification: DataClassification;
  isApproved: boolean;
  isPresent: boolean;
  /** Field role confirmed by the administrator (Phase 14): what Monitoring Rule templates bind to. */
  semanticRole?: ColumnSemanticRole;
  /** The role the catalogue suggests from the name and type; shown until the administrator confirms one. */
  suggestedSemanticRole?: ColumnSemanticRole;
}

export type ColumnSemanticRole = 'Unspecified' | 'Identifier' | 'PersonIdentifier' | 'AccountIdentifier' | 'DeviceIdentifier' | 'Timestamp' | 'Amount' | 'Quantity' | 'Status' | 'Location' | 'Contact' | 'Other';

export const SEMANTIC_ROLES: ColumnSemanticRole[] = ['Identifier', 'PersonIdentifier', 'AccountIdentifier', 'DeviceIdentifier', 'Timestamp', 'Amount', 'Quantity', 'Status', 'Location', 'Contact', 'Other'];

export interface Relationship {
  relationshipId: string;
  constraintName: string;
  fromObjectId: string;
  fromObject: string;
  fromColumn: string;
  toObjectId: string;
  toObject: string;
  toColumn: string;
  isPresent: boolean;
}

export interface ObjectDetail {
  summary: ObjectSummary;
  businessDescription?: string;
  columns: ColumnInfo[];
  relationships: Relationship[];
}

export interface MetadataRefreshResult {
  objectsAdded: number;
  objectsUpdated: number;
  objectsRemoved: number;
  columnsAdded: number;
  columnsRemoved: number;
  relationshipsAdded: number;
  relationshipsRemoved: number;
  refreshedAt: string;
}

export interface UpdateObjectRequest {
  displayNameEn?: string;
  displayNameAr?: string;
  businessDescription?: string;
  isApproved: boolean;
}

export interface UpdateColumnRequest extends UpdateObjectRequest {
  classification: DataClassification;
  /** Omitted: unchanged. 'Unspecified': cleared. */
  semanticRole?: ColumnSemanticRole;
}
