import { ColumnSemanticRole } from '../organizations/applications.models';

export type MonitoringRuleKind = 'Data' | 'Access' | 'System';
export type AlertSeverity = 'Info' | 'Medium' | 'High';
export type AlertStatus = 'Open' | 'Acknowledged' | 'Resolved' | 'Dismissed';
export type RuleEvaluationOutcome = 'NoMatch' | 'Fired' | 'Failed' | 'Skipped';
export type JobRecurrenceKind = 'Interval' | 'Daily' | 'Weekly';
export type AlertEventType =
  'Raised' | 'Repeated' | 'Acknowledged' | 'Resolved' | 'Dismissed' | 'Reopened' | 'UserSuspended';

export interface RuleMeasure {
  aggregate: string;
  columnId?: string | null;
}

export interface RuleFilter {
  columnId: string;
  operator: string;
  values: string[];
}

export interface RuleCondition {
  operator: string;
  value: number;
}

export interface DataRuleDefinition {
  objectId: string;
  timeColumnId?: string | null;
  windowHours: number;
  groupByColumnIds: string[];
  measure: RuleMeasure;
  filters: RuleFilter[];
  condition: RuleCondition;
  comparison: 'none' | 'previous_period';
  maxGroups: number;
}

export interface AccessThreshold {
  mode: 'absolute' | 'baseline';
  value: number;
  statistic?: 'p95' | 'median';
  minActiveDays?: number;
  minimum?: number;
}

export interface AccessRuleDefinition {
  signal: string;
  windowHours: number;
  objectIds: string[];
  minClassification?: 'Internal' | 'Confidential' | 'Restricted' | null;
  roleCodes: string[];
  outsideWorkingHours: boolean;
  newUsersWithinDays?: number | null;
  threshold: AccessThreshold;
}

export type SystemSignal =
  'connection_failures' | 'email_failures' | 'ai_budget' | 'metadata_drift' | 'report_failures';

/** P14.4: the organization's own platform signals; no business data. */
export interface SystemRuleDefinition {
  signal: SystemSignal;
  windowHours: number;
  threshold: number;
}

export interface RuleDefinition {
  data?: DataRuleDefinition | null;
  access?: AccessRuleDefinition | null;
  system?: SystemRuleDefinition | null;
}

export interface TemplateSlot {
  slot: string;
  roles: ColumnSemanticRole[];
}

export interface TemplateColumnOption {
  columnId: string;
  columnName: string;
  displayNameEn?: string;
  displayNameAr?: string;
  role: ColumnSemanticRole;
}

export interface TemplateCandidate {
  objectId: string;
  applicationId: string;
  applicationCode: string;
  schemaName: string;
  objectName: string;
  displayNameEn?: string;
  displayNameAr?: string;
  slots: Record<string, TemplateColumnOption[]>;
}

export interface RuleTemplate {
  code: string;
  kind: MonitoringRuleKind;
  severity: AlertSeverity;
  recurrenceKind: JobRecurrenceKind;
  timeOfDay: string;
  intervalMinutes: number;
  available: boolean;
  unavailableReason?: string;
  slots: TemplateSlot[];
  candidates: TemplateCandidate[];
  defaults: RuleDefinition;
}

export interface RuleRecipientRef {
  userId?: string | null;
  roleId?: string | null;
}

export interface RuleRecipient extends RuleRecipientRef {
  name: string;
}

export interface MonitoringRule {
  ruleId: string;
  name: string;
  description?: string;
  kind: MonitoringRuleKind;
  templateCode?: string;
  definition: RuleDefinition;
  severity: AlertSeverity;
  recurrenceKind: JobRecurrenceKind;
  intervalMinutes: number;
  timeOfDay: string;
  dayOfWeek?: number;
  cooldownHours: number;
  isActive: boolean;
  ownerUserId: string;
  ownerName: string;
  createdByUserId: string;
  recipients: RuleRecipient[];
  objectName?: string;
  lastEvaluatedAt?: string;
  lastOutcome?: RuleEvaluationOutcome;
  lastError?: string;
  nextRunAt?: string;
  openAlerts: number;
  canEdit: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface SaveMonitoringRule {
  name: string;
  description?: string | null;
  kind: MonitoringRuleKind;
  templateCode?: string | null;
  definition: RuleDefinition;
  severity: AlertSeverity;
  recurrenceKind: JobRecurrenceKind;
  intervalMinutes: number;
  timeOfDay: string;
  dayOfWeek?: number | null;
  cooldownHours: number;
  isActive: boolean;
  ownerUserId?: string | null;
  recipients: RuleRecipientRef[];
}

export interface RuleMatch {
  groupKey?: string;
  groupValues?: (string | null)[];
  measure: number;
  previous?: number;
  threshold?: number;
  subjectUserId?: string;
  subjectName?: string;
}

export interface RuleTestResult {
  outcome: RuleEvaluationOutcome;
  matchCount: number;
  rowsExamined: number;
  errorCode?: string;
  redacted: boolean;
  matches: RuleMatch[];
}

export interface OwnerOption {
  userId: string;
  fullName: string;
  email: string;
  roles: string[];
  permittedObjects: number;
}

export interface MonitoringSettings {
  digestTime: string;
  workingHoursStart: string;
  workingHoursEnd: string;
  workingDays: string;
  retentionMonths: number;
  maxRetentionMonths: number;
  timeZone: string;
  monthlyAiBudget?: number | null;
  aiSpendThisMonth?: number;
}

export interface RuleDraftRequest {
  description: string;
  language: string;
  ownerUserId?: string | null;
}

export type DraftBindingPart = 'time' | 'group' | 'measure' | 'filter';

/** One catalogued column the draft binds; shown so the user confirms what the rule reads. */
export interface DraftBinding {
  part: DraftBindingPart;
  columnId: string;
  columnName: string;
  displayNameEn?: string;
  displayNameAr?: string;
  role?: ColumnSemanticRole;
  detail?: string;
}

/** A proposed rule from a plain-language description: nothing is saved until the user saves it. */
export interface RuleDraft {
  rule: SaveMonitoringRule;
  objectLabel: string;
  objectDisplayNameEn?: string;
  objectDisplayNameAr?: string;
  bindings: DraftBinding[];
  explanation: string;
  assumptions: string[];
}

export interface AlertSubject {
  userId: string;
  fullName: string;
  email: string;
  status: string;
}

export interface Alert {
  alertId: string;
  ruleId: string;
  ruleName: string;
  ruleKind?: MonitoringRuleKind;
  severity: AlertSeverity;
  status: AlertStatus;
  groupKey?: string;
  subjectUserId?: string;
  subjectName?: string;
  subject?: AlertSubject;
  title: string;
  summary?: Record<string, unknown>;
  redacted: boolean;
  firstSeenAt: string;
  lastSeenAt: string;
  occurrences: number;
  statusChangedAt?: string;
  statusChangedByName?: string;
  note?: string;
  canAct: boolean;
  canSuspendSubject: boolean;
}

export interface AlertEvent {
  eventType: AlertEventType;
  actorName?: string;
  note?: string;
  createdAt: string;
}

export interface AlertDetail {
  alert: Alert;
  events: AlertEvent[];
}

export interface AlertPage {
  items: Alert[];
  total: number;
  page: number;
  pageSize: number;
  openCount: number;
  openHighCount: number;
}

export interface DigestLine {
  severity: AlertSeverity;
  ruleName: string;
  title: string;
  occurrences: number;
  redacted: boolean;
  groupKey?: string;
  measure?: number;
}

export interface DigestPreview {
  subject: string;
  lines: DigestLine[];
}
