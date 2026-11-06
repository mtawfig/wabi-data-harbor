import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { problemMessage } from '../../../core/problem-message';
import { DataClassification, ShareEmailResult } from '../../../core/sharing/sharing.models';
import { SharingService } from '../../../core/sharing/sharing.service';

export interface EmailDialogData {
  historyId: string;
  versionNumber: number;
  title: string;
  classification: DataClassification;
  hasReport: boolean;
  draft?: { subject: string; body: string } | null;
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Send a version by email (specification §24, review B11). The owner writes subject and body (pre-filled with the AI
 * email draft when there is one), lists recipients and attaches the PDF. The classification decides the gate:
 * Internal needs the standard confirmation, Confidential the owner's explicit disclosure approval (logged), and
 * Restricted data never leaves the organization. Closes with the send result.
 */
@Component({
  selector: 'app-email-dialog',
  imports: [FormsModule, MatButtonModule, MatCheckboxModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, TranslocoModule],
  template: `
    <h2 mat-dialog-title><mat-icon>outgoing_mail</mat-icon>{{ 'sharing.email.title' | transloco }}</h2>
    <mat-dialog-content>
      <p class="wi-muted title">{{ data.title }} · v{{ data.versionNumber }}</p>
      <div class="banner" [class]="'banner banner--' + data.classification.toLowerCase()" data-testid="email-classification">
        <mat-icon>{{ data.classification === 'Internal' ? 'verified_user' : data.classification === 'Confidential' ? 'privacy_tip' : 'gpp_maybe' }}</mat-icon>
        <div>
          <strong>{{ 'sharing.classifications.' + data.classification | transloco }}</strong>
          <p>{{ 'sharing.email.rules.' + data.classification | transloco }}</p>
        </div>
      </div>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="wi-full-width">
        <mat-label>{{ 'sharing.email.recipients' | transloco }}</mat-label>
        <textarea matInput rows="2" [ngModel]="recipientsText()" (ngModelChange)="recipientsText.set($event)" data-testid="email-recipients" class="wi-ltr" placeholder="name@company.com, other@partner.com"></textarea>
        <mat-hint>{{ 'sharing.email.recipientsHint' | transloco: { count: recipients().length } }}</mat-hint>
      </mat-form-field>
      @if (invalid().length) { <p class="error small" role="alert"><mat-icon>error_outline</mat-icon>{{ 'sharing.email.invalidRecipients' | transloco: { list: invalid().join(', ') } }}</p> }
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="wi-full-width">
        <mat-label>{{ 'sharing.email.subject' | transloco }}</mat-label>
        <input matInput [ngModel]="subject()" (ngModelChange)="subject.set($event)" maxlength="200" data-testid="email-subject" />
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="wi-full-width">
        <mat-label>{{ 'sharing.email.body' | transloco }}</mat-label>
        <textarea matInput rows="8" [ngModel]="body()" (ngModelChange)="body.set($event)" maxlength="8000" data-testid="email-body"></textarea>
      </mat-form-field>
      <mat-checkbox [checked]="attachReport()" (change)="attachReport.set($event.checked)" data-testid="email-attach">{{ (data.hasReport ? 'sharing.email.attachStudy' : 'sharing.email.attachExport') | transloco }}</mat-checkbox>
      <mat-checkbox [checked]="confirmed()" (change)="confirmed.set($event.checked)" data-testid="email-confirm" class="confirm">
        {{ (data.classification === 'Confidential' ? 'sharing.email.approveDisclosure' : 'sharing.email.confirmSend') | transloco }}
      </mat-checkbox>
      @if (sending()) { <mat-progress-bar mode="indeterminate" /> }
      @if (error(); as e) { <p class="error" role="alert" data-testid="email-error"><mat-icon>block</mat-icon>{{ e }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" (click)="ref.close(null)">{{ 'common.cancel' | transloco }}</button>
      <button mat-flat-button type="button" (click)="send()" [disabled]="!canSend()" data-testid="email-send"><mat-icon>send</mat-icon>{{ 'sharing.email.send' | transloco }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    h2 { display: flex; align-items: center; gap: 8px; }
    .title { margin: 0 0 12px; font-weight: 600; color: var(--wi-navy); }
    .small { font-size: 12px; }
    .banner { display: flex; gap: 10px; align-items: flex-start; border-radius: var(--wi-radius-sm); padding: 10px 12px; margin-bottom: 14px; font-size: 13px; p { margin: 2px 0 0; } mat-icon { flex: none; }
      &--internal { background: #eef4fb; color: #0b1f3a; mat-icon { color: var(--wi-ocean); } }
      &--confidential { background: #fff7ed; color: #7c2d12; mat-icon { color: #ea580c; } }
      &--restricted { background: #fef2f2; color: #7f1d1d; mat-icon { color: #dc2626; } } }
    mat-form-field { margin-bottom: 10px; }
    .confirm { display: block; margin-top: 8px; }
    .error { display: flex; gap: 8px; align-items: center; color: #991b1b; margin: 8px 0 0; }
    mat-dialog-content { overflow-x: hidden; }
  `,
})
export class EmailDialogComponent {
  readonly data = inject<EmailDialogData>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<EmailDialogComponent, ShareEmailResult | null>);
  private readonly sharing = inject(SharingService);
  private readonly transloco = inject(TranslocoService);

  readonly recipientsText = signal('');
  readonly subject = signal(this.data.draft?.subject ?? this.transloco.translate('sharing.email.defaultSubject', { title: this.data.title }));
  readonly body = signal(this.data.draft?.body ?? this.transloco.translate('sharing.email.defaultBody', { title: this.data.title }));
  readonly attachReport = signal(true);
  readonly confirmed = signal(false);
  readonly sending = signal(false);
  readonly error = signal<string | null>(null);

  readonly tokens = computed(() => this.recipientsText().split(/[\s,;]+/).map((t) => t.trim()).filter(Boolean));
  readonly recipients = computed(() => [...new Set(this.tokens().filter((t) => EMAIL.test(t)).map((t) => t.toLowerCase()))]);
  readonly invalid = computed(() => this.tokens().filter((t) => !EMAIL.test(t)));
  readonly canSend = computed(() => !this.sending() && this.recipients().length > 0 && this.recipients().length <= 10 && this.invalid().length === 0 && this.subject().trim().length > 0 && this.body().trim().length > 0 && this.confirmed());

  send(): void {
    if (!this.canSend()) return;
    this.sending.set(true);
    this.error.set(null);
    this.sharing
      .sendEmail(this.data.historyId, { versionNumber: this.data.versionNumber, recipients: this.recipients(), subject: this.subject().trim(), body: this.body().trim(), attachReport: this.attachReport(), disclosureApproved: this.data.classification === 'Confidential' && this.confirmed() })
      .subscribe({
        next: (result) => {
          this.sending.set(false);
          this.ref.close(result);
        },
        error: (e: HttpErrorResponse) => {
          this.sending.set(false);
          this.error.set(problemMessage(e, this.transloco));
        },
      });
  }
}
