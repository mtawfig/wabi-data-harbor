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
  /** Example questions written by the organization's administrators (EN and/or AR). */
  exampleQuestions?: ApplicationExample[];
}

export interface ApplicationExample {
  en?: string | null;
  ar?: string | null;
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
  /** How many rows the whole list has when it is longer than the rows fetched (P13.50); absent when the list is whole or the count is unknown. */
  totalRows?: number | null;
  kpis: { column: string; kind: string; value: number }[];
  series: { name: string; categories: string[]; values: number[] }[];
  durationMs: number;
}

/** An object the plan reads: the main source or a join. */
export interface PlanObjectView {
  schema?: string;
  object: string;
  alias: string;
}

/** One output column: a plain column, an aggregate (count without a column counts rows) or a date bucket. */
export interface PlanSelectItemView {
  alias: string;
  column?: string | null;
  aggregate?: 'count' | 'count_distinct' | 'sum' | 'avg' | 'min' | 'max' | string | null;
  dateTrunc?: 'day' | 'week' | 'month' | 'quarter' | 'year' | string | null;
}

/** A condition: values (or an `$asker.*` placeholder for the asker's own code) or another column in compareTo. */
export interface PlanConditionView {
  column: string;
  operator: string;
  values?: string[];
  compareTo?: string | null;
}

export interface PlanFilterGroupView {
  logic?: 'and' | 'or' | string;
  conditions?: PlanConditionView[];
  groups?: PlanFilterGroupView[];
}

/** The validated query plan of a panel; the page explains it in plain words ("How this was computed"). */
export interface QueryPlanView {
  title: string;
  objective: string;
  assumptions: string[];
  source?: PlanObjectView | null;
  joins?: PlanObjectView[] | null;
  select?: PlanSelectItemView[] | null;
  filters?: PlanFilterGroupView | null;
  groupBy?: string[] | null;
  having?: PlanConditionView[] | null;
  orderBy?: { alias: string; direction?: 'asc' | 'desc' | string }[] | null;
  visualization?: { type: 'table' | 'bar' | 'line' | 'pie' | 'kpi'; x?: string | null; y: string[] } | null;
  limit?: number | null;
}

/** A catalogue display name in English and Arabic (either may be missing). */
export interface DisplayLabel {
  en?: string | null;
  ar?: string | null;
}

/** A row scope that limited a panel to what the asker may see, with the asker's own value it was bound to. */
export interface PanelRowScope {
  scope: 'Self' | 'Department' | 'Section';
  value: string;
}

/**
 * One executed query of an answer: a KPI, a chart or a table with its own plan and verified rows. The SQL it ran never
 * reaches the browser; the server keeps it for reference and accuracy checks.
 */
export interface AnalysisPanel {
  title: string;
  plan: QueryPlanView;
  result: AnalysisResult;
  /** Row scopes applied to this panel; empty or absent when every row of the queried objects was read (older answers too). */
  rowScopes?: PanelRowScope[] | null;
  /** Catalogue display names keyed as the plan writes them ("alias" and "alias.Column"); absent on older answers. */
  labels?: Record<string, DisplayLabel> | null;
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
  /** `incomplete`: the provider stopped before the explanation was finished; the page says so instead of showing nothing. */
  interpretation?: { narrative: string; highlights: string[]; usage: PlanUsage; incomplete?: boolean } | null;
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
