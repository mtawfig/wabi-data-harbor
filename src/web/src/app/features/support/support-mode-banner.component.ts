import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { interval } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';
import { LanguageService } from '../../core/i18n/language.service';
import { SupportAccessService } from '../../core/support/support-access.service';
import { WiDatePipe } from '../../shared/format/locale-pipes';

/** Visible support-mode banner (specification §16.3): organization, ticket, remaining time and an explicit exit. */
@Component({
  selector: 'app-support-mode-banner',
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoModule, WiDatePipe],
  template: `
    @if (access.session(); as s) {
      <div class="banner" role="status" data-testid="support-mode-banner">
        <mat-icon>key</mat-icon>
        <span class="text">
          <strong>{{ 'supportAccess.banner.title' | transloco }}</strong>
          {{ 'supportAccess.banner.detail' | transloco: { organization: organizationName(), reference: s.grant.ticketReference } }}
          · {{ 'supportAccess.banner.remaining' | transloco: { minutes: remaining() } }} ({{ s.expiresAt | wiDate: 'shortTime' }})
        </span>
        <a mat-button [routerLink]="['/platform/support', s.grant.ticketId]">{{ 'supportAccess.banner.openTicket' | transloco }}</a>
        <button mat-stroked-button type="button" (click)="exit()" [disabled]="busy()" data-testid="support-mode-exit"><mat-icon>logout</mat-icon>{{ 'supportAccess.actions.exit' | transloco }}</button>
      </div>
    }
  `,
  styles: `
    .banner { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; padding: 10px 24px; background: #fef3c7; color: #78350f; border-bottom: 1px solid #fcd34d; font-size: 14px; --mdc-text-button-label-text-color: #78350f; --mdc-outlined-button-label-text-color: #78350f; --mdc-outlined-button-outline-color: #b45309; --mat-icon-color: #b45309; }
    .text { flex: 1; min-width: 240px; }
  `,
})
export class SupportModeBannerComponent {
  readonly access = inject(SupportAccessService);
  private readonly language = inject(LanguageService);
  readonly busy = signal(false);
  private readonly tick = toSignal(interval(30_000), { initialValue: 0 });
  readonly remaining = computed(() => {
    this.tick();
    const s = this.access.session();
    return s ? Math.max(0, Math.round((new Date(s.expiresAt).getTime() - Date.now()) / 60000)) : 0;
  });

  organizationName(): string {
    const g = this.access.session()?.grant;
    return g ? (this.language.current() === 'ar' ? g.organizationNameAr : g.organizationName) : '';
  }

  exit(): void {
    this.busy.set(true);
    this.access.exit().subscribe({ next: () => this.busy.set(false), error: () => this.busy.set(false) });
  }
}
