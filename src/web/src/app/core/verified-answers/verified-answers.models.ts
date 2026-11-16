import { AskResponse, DisplayLabel, QueryPlanView } from '../platform/ai.models';

/** Stable codes of a verified answer (P13.53); labels live under `verifiedAnswers.statuses`. */
export const VERIFIED_ANSWER_STATUSES = ['Pending', 'Reported', 'Approved', 'Rejected', 'Disabled', 'SetAside'] as const;
export type VerifiedAnswerStatus = (typeof VERIFIED_ANSWER_STATUSES)[number];
export type AnswerVerdict = 'Correct' | 'NotCorrect';
export type VerifiedAnswerAction = 'approve' | 'reject' | 'disable' | 'enable';

/** The asker's own verdict on one saved answer; absent fields mean no verdict yet. */
export interface AnswerFeedbackState {
  historyId: string;
  versionNumber: number;
  verdict?: AnswerVerdict | null;
  reason?: string | null;
  at?: string | null;
}

export interface VerifiedAnswerListItem {
  verifiedAnswerId: string;
  applicationId: string;
  applicationName: string;
  applicationNameAr: string;
  question: string;
  language: string;
  askedByName: string;
  status: VerifiedAnswerStatus;
  latestVerdict: AnswerVerdict;
  latestReason?: string | null;
  latestFeedbackAt: string;
  reviewedAt?: string | null;
  useCount: number;
}

export interface VerifiedAnswerCounts {
  pending: number;
  reported: number;
  approved: number;
  rejected: number;
  disabled: number;
  setAside: number;
  waiting: number;
}

export interface AnswerFeedbackEntry {
  verdict: AnswerVerdict;
  reason?: string | null;
  userName: string;
  createdAt: string;
}

export interface VerifiedAnswerDetail {
  verifiedAnswerId: string;
  applicationId: string;
  applicationName: string;
  applicationNameAr: string;
  historyId: string;
  versionNumber: number;
  question: string;
  language: string;
  askedByName: string;
  status: VerifiedAnswerStatus;
  latestVerdict: AnswerVerdict;
  latestReason?: string | null;
  latestFeedbackAt: string;
  reviewedByName?: string | null;
  reviewedAt?: string | null;
  reviewNote?: string | null;
  setAsideReason?: string | null;
  useCount: number;
  lastUsedAt?: string | null;
  plan?: { objective: string; queries: QueryPlanView[]; assumptions: string[] } | null;
  /** The stored answer, only when the administrator may read everything it uses with no row limit. */
  result?: AskResponse | null;
  resultHidden: boolean;
  feedback: AnswerFeedbackEntry[];
  /** Catalogue display names per query, so the plan reads in the reviewer's language. */
  panelLabels?: (Record<string, DisplayLabel> | null)[] | null;
}

export interface VerifiedAnswerPage {
  items: VerifiedAnswerListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}
