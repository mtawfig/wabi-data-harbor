export type SubscriptionRecurrence = 'Daily' | 'Weekly' | 'Monthly';
export type SubscriptionFormat = 'Pdf' | 'Xlsx';
export type SubscriptionLanguage = 'en' | 'ar' | 'recipient';
export type DeliveryStatus = 'Delivered' | 'NoData' | 'Failed';

export interface SubscriptionAnalysis {
  historyId: string;
  title: string;
  applicationName: string;
  applicationNameAr: string;
  ownerName: string;
  isOwn: boolean;
  currentVersionNumber: number;
  panelCount: number;
  updatedAt: string;
  /** Calendar dates written into the analysis (yyyy-MM-dd). Not empty: every run covers the same period. */
  fixedDates?: string[];
}

export interface SubscriptionRecipient {
  userId: string;
  fullName: string;
  email: string;
  status: string;
}

export interface SubscriptionRunSummary {
  runId: string;
  runAt: string;
  delivered: number;
  noData: number;
  failed: number;
}

export interface ReportSubscription {
  subscriptionId: string;
  name: string;
  historyId: string;
  historyTitle: string;
  recurrenceKind: SubscriptionRecurrence;
  timeOfDay: string;
  dayOfWeek?: number;
  dayOfMonth?: number;
  format: SubscriptionFormat;
  language: SubscriptionLanguage;
  isActive: boolean;
  pausedReason?: string;
  /** Calendar dates written into the analysis (yyyy-MM-dd). Not empty: every run covers the same period. */
  fixedDates?: string[];
  consecutiveFailures: number;
  lastRunAt?: string;
  nextRunAt?: string;
  createdByUserId: string;
  createdByName: string;
  recipients: SubscriptionRecipient[];
  lastRun?: SubscriptionRunSummary;
  canEdit: boolean;
  createdAt: string;
}

export interface SaveReportSubscription {
  name: string;
  historyId: string;
  recurrenceKind: SubscriptionRecurrence;
  timeOfDay: string;
  dayOfWeek?: number | null;
  dayOfMonth?: number | null;
  format: SubscriptionFormat;
  language: SubscriptionLanguage;
  isActive: boolean;
  recipientUserIds: string[];
}

export interface ReportDelivery {
  deliveryId: string;
  subscriptionId: string;
  runId: string;
  recipientUserId: string;
  recipientName: string;
  status: DeliveryStatus;
  isManualRun: boolean;
  fileName?: string;
  sizeBytes: number;
  rowCount: number;
  language: string;
  errorCode?: string;
  createdAt: string;
}

export interface SubscriptionRunResult {
  runId: string;
  delivered: number;
  noData: number;
  failed: number;
  errorCode?: string;
  deliveries: ReportDelivery[];
}

export interface ReceivedReport {
  deliveryId: string;
  subscriptionId: string;
  subscriptionName: string;
  historyTitle: string;
  status: DeliveryStatus;
  fileName?: string;
  contentType?: string;
  sizeBytes: number;
  rowCount: number;
  language: string;
  createdAt: string;
}
