export type AnalysisType = 'Answer' | 'Dashboard' | 'Report';
export type AnalysisHistoryStatus = 'Completed' | 'Failed';

export interface HistorySummary {
  historyId: string;
  title: string;
  promptExcerpt: string;
  applicationId: string;
  applicationCode: string;
  applicationName: string;
  applicationNameAr: string;
  analysisType: AnalysisType;
  status: AnalysisHistoryStatus;
  dataClassification: 'Internal' | 'Confidential' | 'Restricted';
  outputLanguage: 'en' | 'ar';
  currentVersionNumber: number;
  isFavorite: boolean;
  isArchived: boolean;
  hasReport: boolean;
  estimatedCost: number;
  createdAt: string;
  updatedAt?: string | null;
}

export interface HistoryVersion {
  versionId: string;
  versionNumber: number;
  status: AnalysisHistoryStatus;
  failureStage?: string | null;
  failureCode?: string | null;
  modelName?: string | null;
  inputTokens: number;
  outputTokens: number;
  estimatedCost: number;
  durationMs: number;
  panelCount: number;
  rowCount: number;
  hasResult: boolean;
  hasReport: boolean;
  reportFileName?: string | null;
  createdAt: string;
}

export interface CollectionSummary {
  collectionId: string;
  name: string;
  description?: string | null;
  itemCount: number;
  createdAt: string;
  updatedAt?: string | null;
}

export interface HistoryDetail {
  summary: HistorySummary;
  originalPrompt: string;
  promptLanguage: 'en' | 'ar';
  versions: HistoryVersion[];
  collections: CollectionSummary[];
}

export interface HistoryListQuery {
  page: number;
  pageSize: number;
  search?: string;
  applicationId?: string | null;
  from?: string | null;
  to?: string | null;
  favorite?: boolean | null;
  archived?: boolean;
  collectionId?: string | null;
  type?: AnalysisType | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export type ExportFormat = 'Pdf' | 'Docx' | 'Xlsx';

export interface ReportFile {
  artifactId: string;
  historyId: string;
  versionNumber: number;
  historyTitle: string;
  kind: 'ReportPdf' | 'ExportPdf' | 'ExportDocx' | 'ExportXlsx';
  format: ExportFormat;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  language: 'en' | 'ar';
  createdAt: string;
}
