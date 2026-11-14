import { AnalysisType, HistoryVersion } from '../history/history.models';

export type DataClassification = 'Internal' | 'Confidential' | 'Restricted';
export type ShareStatus = 'Active' | 'Expired' | 'Revoked';

export interface ShareRecipient {
  userId: string;
  fullName: string;
  email: string;
  departmentNameEn?: string | null;
  departmentNameAr?: string | null;
}

/** A share as its owner sees it. */
export interface Share {
  shareId: string;
  historyId: string;
  historyTitle: string;
  analysisType: AnalysisType;
  dataClassification: DataClassification;
  recipientUserId: string;
  recipientName: string;
  recipientEmail: string;
  permission: 'View';
  message?: string | null;
  status: ShareStatus;
  createdAt: string;
  expiresAt?: string | null;
  revokedAt?: string | null;
  lastViewedAt?: string | null;
}

/** An analysis shared with the signed-in user. */
export interface SharedAnalysis {
  shareId: string;
  historyId: string;
  title: string;
  promptExcerpt: string;
  applicationId: string;
  applicationName: string;
  applicationNameAr: string;
  analysisType: AnalysisType;
  dataClassification: DataClassification;
  outputLanguage: 'en' | 'ar';
  currentVersionNumber: number;
  hasReport: boolean;
  ownerUserId: string;
  ownerName: string;
  message?: string | null;
  sharedAt: string;
  expiresAt?: string | null;
}

export interface SharedAnalysisDetail {
  share: SharedAnalysis;
  originalPrompt: string;
  promptLanguage: 'en' | 'ar';
  versions: HistoryVersion[];
  /** True when the recipient may read this data themselves, so "View all in Excel" is offered (P13.50). */
  canViewAllInExcel?: boolean;
}

export interface SendEmailRequest {
  versionNumber: number;
  recipients: string[];
  subject: string;
  body: string;
  attachReport: boolean;
  disclosureApproved: boolean;
}

export interface ShareEmailResult {
  recipients: number;
  internalRecipients: number;
  externalRecipients: number;
  attachedReport: boolean;
  attachmentFileName?: string | null;
  inAppSharesCreated: number;
}
