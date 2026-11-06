import { DatePipe } from '@angular/common';
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
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { problemMessage } from '../../../core/problem-message';
import { Share, ShareRecipient } from '../../../core/sharing/sharing.models';
import { SharingService } from '../../../core/sharing/sharing.service';

export interface ShareDialogData {
  historyId: string;
  title: string;
}

/**
 * Share an analysis with colleagues (specification §24): pick active users of the organization, add a note and an
 * optional expiry; see and revoke existing shares. Recipients get VIEW only. Closes with `true` when anything changed.
 */
@Component({
  selector: 'app-share-dialog',
  imports: [DatePipe, FormsModule, MatButtonModule, MatCheckboxModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, TranslocoModule],
  template: `
    <h2 mat-dialog-title><mat-icon>share</mat-icon>{{ 'sharing.share.title' | transloco }}</h2>
    <mat-dialog-content>
      <p class="wi-muted title">{{ data.title }}</p>
      <p class="wi-muted small">{{ 'sharing.share.hint' | transloco }}</p>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="wi-full-width">
        <mat-label>{{ 'sharing.share.searchRecipients' | transloco }}</mat-label>
        <input matInput [ngModel]="search()" (ngModelChange)="onSearch($event)" data-testid="share-search" autocomplete="off" />
        <mat-icon matSuffix>search</mat-icon>
      </mat-form-field>
      @if (searching()) { <mat-progress-bar mode="indeterminate" /> }
      <ul class="recipients" data-testid="share-recipients">
        @for (r of candidates(); track r.userId) {
          <li>
            <mat-checkbox [checked]="selected().has(r.userId)" (change)="toggle(r, $event.checked)" [attr.data-testid]="'share-recipient-' + r.userId">
              <span class="name">{{ r.fullName }}</span>
              <span class="wi-muted small wi-ltr">{{ r.email }}</span>
              @if (department(r); as d) { <span class="wi-muted small"> · {{ d }}</span> }
            </mat-checkbox>
          </li>
        } @empty {
          @if (!searching()) { <li class="wi-muted small">{{ 'sharing.share.noRecipients' | transloco }}</li> }
        }
      </ul>
      <div class="row">
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow">
          <mat-label>{{ 'sharing.share.message' | transloco }}</mat-label>
          <input matInput [ngModel]="message()" (ngModelChange)="message.set($event)" maxlength="1000" data-testid="share-message" />
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="expiry">
          <mat-label>{{ 'sharing.share.expiry' | transloco }}</mat-label>
          <mat-select [value]="expiryDays()" (valueChange)="expiryDays.set($event)" data-testid="share-expiry">
            <mat-option [value]="0">{{ 'sharing.share.expiryOptions.none' | transloco }}</mat-option>
            @for (d of [7, 30, 90]; track d) { <mat-option [value]="d">{{ 'sharing.share.expiryOptions.days' | transloco: { days: d } }}</mat-option> }
          </mat-select>
        </mat-form-field>
      </div>
      @if (error(); as e) { <p class="error" role="alert"><mat-icon>error_outline</mat-icon>{{ e }}</p> }

      <h3>{{ 'sharing.share.existing' | transloco }}</h3>
      <ul class="existing" data-testid="share-existing">
        @for (s of shares(); track s.shareId) {
          <li [class.inactive]="s.status !== 'Active'">
            <div>
              <strong>{{ s.recipientName }}</strong> <span class="wi-muted small wi-ltr">{{ s.recipientEmail }}</span><br />
              <span class="chip" [class.chip--muted]="s.status !== 'Active'">{{ 'sharing.statuses.' + s.status | transloco }}</span>
              <span class="wi-muted small"> · {{ s.createdAt | date: 'mediumDate' }}@if (s.expiresAt) { · {{ 'sharing.share.expiresOn' | transloco: { date: (s.expiresAt | date: 'mediumDate') } }}}@if (s.lastViewedAt) { · {{ 'sharing.share.viewed' | transloco: { date: (s.lastViewedAt | date: 'medium') } }}}</span>
            </div>
            @if (s.status === 'Active') { <button mat-button type="button" class="danger" (click)="revoke(s)" [attr.data-testid]="'share-revoke-' + s.shareId"><mat-icon>link_off</mat-icon>{{ 'sharing.share.revoke' | transloco }}</button> }
          </li>
        } @empty { <li class="wi-muted small">{{ 'sharing.share.noneYet' | transloco }}</li> }
      </ul>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" (click)="close()">{{ 'common.cancel' | transloco }}</button>
      <button mat-flat-button type="button" (click)="share()" [disabled]="selected().size === 0 || saving()" data-testid="share-submit"><mat-icon>send</mat-icon>{{ 'sharing.share.submit' | transloco: { count: selected().size } }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    h2 { display: flex; align-items: center; gap: 8px; }
    .title { margin: 0 0 4px; font-weight: 600; color: var(--wi-navy); }
    .small { font-size: 12px; }
    .recipients { list-style: none; margin: 8px 0 12px; padding: 0; max-height: 220px; overflow: auto; border: 1px solid var(--wi-border); border-radius: var(--wi-radius-sm); li { padding: 2px 8px; border-bottom: 1px solid #f1f5f9; &:last-child { border-bottom: 0; } } .name { font-weight: 600; margin-inline-end: 8px; } }
    .row { display: flex; gap: 12px; flex-wrap: wrap; .grow { flex: 1 1 260px; } .expiry { width: 180px; } }
    h3 { font-size: 14px; margin: 16px 0 6px; }
    .existing { list-style: none; margin: 0; padding: 0; li { display: flex; justify-content: space-between; align-items: center; gap: 8px; padding: 8px 0; border-top: 1px solid #f1f5f9; &.inactive { opacity: .7; } } }
    .chip { display: inline-block; padding: 1px 8px; border-radius: 999px; background: #dcfce7; color: #166534; font-size: 11px; font-weight: 600; &--muted { background: #f1f5f9; color: #475569; } }
    .error { display: flex; gap: 8px; align-items: center; color: #991b1b; margin: 4px 0 0; }
    .danger { color: #991b1b; }
    mat-dialog-content { overflow-x: hidden; }
  `,
})
export class ShareDialogComponent {
  readonly data = inject<ShareDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<ShareDialogComponent, boolean>);
  private readonly sharing = inject(SharingService);
  private readonly transloco = inject(TranslocoService);
  private readonly language = inject(LanguageService);

  readonly search = signal('');
  readonly candidates = signal<ShareRecipient[]>([]);
  readonly selected = signal(new Map<string, ShareRecipient>());
  readonly message = signal('');
  readonly expiryDays = signal(0);
  readonly shares = signal<Share[]>([]);
  readonly searching = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly changed = signal(false);
  readonly activeCount = computed(() => this.shares().filter((s) => s.status === 'Active').length);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.loadCandidates('');
    this.loadShares();
  }

  department(r: ShareRecipient): string | null | undefined {
    return this.language.current() === 'ar' ? r.departmentNameAr : r.departmentNameEn;
  }

  onSearch(value: string): void {
    this.search.set(value);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.loadCandidates(value), 250);
  }

  toggle(r: ShareRecipient, checked: boolean): void {
    this.selected.update((map) => {
      const next = new Map(map);
      if (checked) next.set(r.userId, r);
      else next.delete(r.userId);
      return next;
    });
  }

  share(): void {
    if (this.selected().size === 0 || this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    const days = this.expiryDays();
    const expiresAt = days > 0 ? new Date(Date.now() + days * 86_400_000).toISOString() : null;
    this.sharing.share(this.data.historyId, [...this.selected().keys()], this.message().trim() || null, expiresAt).subscribe({
      next: () => {
        this.saving.set(false);
        this.changed.set(true);
        this.selected.set(new Map());
        this.message.set('');
        this.loadShares();
      },
      error: (e: HttpErrorResponse) => {
        this.saving.set(false);
        this.error.set(problemMessage(e, this.transloco));
      },
    });
  }

  revoke(s: Share): void {
    this.sharing.revoke(this.data.historyId, s.shareId).subscribe({
      next: () => {
        this.changed.set(true);
        this.loadShares();
      },
      error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)),
    });
  }

  close(): void {
    this.ref.close(this.changed());
  }

  private loadCandidates(search: string): void {
    this.searching.set(true);
    this.sharing.recipients(search).subscribe({ next: (r) => { this.candidates.set(r); this.searching.set(false); }, error: (e: HttpErrorResponse) => { this.searching.set(false); this.error.set(problemMessage(e, this.transloco)); } });
  }

  private loadShares(): void {
    this.sharing.sharesOf(this.data.historyId).subscribe({ next: (s) => this.shares.set(s), error: () => undefined });
  }
}
