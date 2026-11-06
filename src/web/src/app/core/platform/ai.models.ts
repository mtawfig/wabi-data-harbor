export type AiProviderType = 'Anthropic' | 'OpenAiCompatible' | 'Gemini';
export const AI_PROVIDER_TYPES: AiProviderType[] = ['Anthropic', 'OpenAiCompatible', 'Gemini'];
export type AiEffort = 'low' | 'medium' | 'high';

export interface AiProvider {
  providerId: string;
  scope: 'Platform' | 'Organization';
  organizationId?: string | null;
  providerType: AiProviderType;
  name: string;
  endpoint?: string | null;
  model: string;
  apiKeyConfigured: boolean;
  apiKeyHint?: string | null;
  inputPricePerMillion: number;
  outputPricePerMillion: number;
  cachedInputPricePerMillion: number;
  maxOutputTokens: number;
  effort?: AiEffort | null;
  isEnabled: boolean;
  isApproved: boolean;
  isDefault: boolean;
  lastTestAt?: string | null;
  lastTestSucceeded?: boolean | null;
  lastTestDetail?: string | null;
  updatedAt: string;
}

/** Write model. apiKey is write-only: empty keeps the stored key. */
export interface SaveAiProviderRequest {
  providerType: AiProviderType;
  name: string;
  endpoint?: string | null;
  model: string;
  apiKey?: string | null;
  inputPricePerMillion: number;
  outputPricePerMillion: number;
  cachedInputPricePerMillion: number;
  maxOutputTokens: number;
  effort?: AiEffort | null;
  isEnabled: boolean;
  isApproved: boolean;
}

export interface AiProviderTestResult {
  succeeded: boolean;
  model?: string | null;
  inputTokens: number;
  outputTokens: number;
  durationMs: number;
  error?: string | null;
}

export interface OrganizationAiSettings {
  selectedProviderId?: string | null;
  platformProviders: AiProvider[];
  ownProviders: AiProvider[];
  allowedTypes: AiProviderType[];
}

export interface PlanError {
  code: string;
  detail: string;
}

export interface AskableApplication {
  applicationId: string;
  applicationCode: string;
  applicationName: string;
  applicationNameAr: string;
  databaseType: string;
  isBuiltIn: boolean;
  objectCount: number;
}

export interface ResultColumn {
  name: string;
  dataType: string;
  isNumeric: boolean;
  isTemporal: boolean;
}

export interface AnalysisResult {
  columns: ResultColumn[];
  rows: (string | number | boolean | null)[][];
  rowCount: number;
  truncated: boolean;
  kpis: { column: string; kind: string; value: number }[];
  series: { name: string; categories: string[]; values: number[] }[];
  durationMs: number;
}

export interface QueryPlanView {
  title: string;
  objective: string;
  assumptions: string[];
  visualization?: { type: 'table' | 'bar' | 'line' | 'pie' | 'kpi'; x?: string | null; y: string[] } | null;
  limit?: number | null;
}

/** One executed query of an answer: a KPI, a chart or a table with its own verified rows and redacted SQL. */
export interface AnalysisPanel {
  title: string;
  plan: QueryPlanView;
  compiledSqlPreview: string;
  result: AnalysisResult;
}

export interface ReportSection {
  title: string;
  body: string;
  panels: number[];
}

export interface GeneratedReport {
  title: string;
  subtitle: string;
  fileName: string;
  sizeBytes: number;
  introduction: string;
  sections: ReportSection[];
  summary: string;
  keyFindings: string[];
  recommendations: { title: string; detail: string }[];
}

export interface EmailDraft {
  subject: string;
  body: string;
  attachmentFileName?: string | null;
}

export interface AskResponse {
  analysisId: string;
  /** Set once the answer is saved to My History (every successful ask); the version it became. */
  historyId?: string | null;
  versionNumber: number;
  objective: string;
  assumptions: string[];
  panels: AnalysisPanel[];
  report?: GeneratedReport | null;
  emailDraft?: EmailDraft | null;
  interpretation?: { narrative: string; highlights: string[]; usage: PlanUsage } | null;
  planUsage: PlanUsage;
  totalEstimatedCost: number;
  totalDurationMs: number;
}

export interface PlanUsage {
  provider: string;
  model: string;
  inputTokens: number;
  outputTokens: number;
  cachedInputTokens: number;
  estimatedCost: number;
  durationMs: number;
}

export interface PlanResponse {
  plan: unknown;
  authorized: { applicationId: string; effectiveLimit: number; objects: { alias: string; objectName: string; rowPredicates: { column: string; value: string }[] }[] };
  usage: { provider: string; model: string; inputTokens: number; outputTokens: number; cachedInputTokens: number; estimatedCost: number; durationMs: number };
}
