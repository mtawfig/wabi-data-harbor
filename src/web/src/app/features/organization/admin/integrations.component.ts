import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';

/**
 * Integrations (P11.2): Google Drive and Microsoft OneDrive publishing (P10.2) is deferred to the next version by
 * owner decision; this page says so honestly and points to the delivery channels that exist today.
 */
@Component({
  selector: 'app-integrations',
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoModule],
  template: `
    <section class="wi-page integrations">
      <header class="wi-page__header">
        <div>
          <p class="wi-eyebrow">{{ 'nav.org.admin.title' | transloco }}</p>
          <h1>{{ 'integrations.title' | transloco }}</h1>
          <p class="wi-muted">{{ 'integrations.subtitle' | transloco }}</p>
        </div>
      </header>
      <div class="integrations__grid">
        <article class="wi-card provider" data-testid="integration-drive">
          <div class="provider__head"><mat-icon>cloud</mat-icon><h3>Google Drive</h3><span class="chip">{{ 'integrations.nextVersion' | transloco }}</span></div>
          <p class="wi-muted">{{ 'integrations.drive' | transloco }}</p>
        </article>
        <article class="wi-card provider" data-testid="integration-onedrive">
          <div class="provider__head"><mat-icon>cloud_queue</mat-icon><h3>Microsoft OneDrive</h3><span class="chip">{{ 'integrations.nextVersion' | transloco }}</span></div>
          <p class="wi-muted">{{ 'integrations.onedrive' | transloco }}</p>
        </article>
        <article class="wi-card provider provider--available">
          <div class="provider__head"><mat-icon>share</mat-icon><h3>{{ 'integrations.available.title' | transloco }}</h3><span class="chip chip--on">{{ 'integrations.available.badge' | transloco }}</span></div>
          <ul>
            <li><mat-icon>inbox</mat-icon>{{ 'integrations.available.inApp' | transloco }}</li>
            <li><mat-icon>outgoing_mail</mat-icon>{{ 'integrations.available.email' | transloco }}</li>
            <li><mat-icon>download</mat-icon>{{ 'integrations.available.exports' | transloco }}</li>
          </ul>
          <a mat-stroked-button routerLink="/app/history"><mat-icon>history</mat-icon>{{ 'nav.org.history' | transloco }}</a>
        </article>
      </div>
      <p class="wi-muted small"><mat-icon>info</mat-icon>{{ 'integrations.note' | transloco }}</p>
    </section>
  `,
  styles: `
    .integrations__grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; }
    .provider { &__head { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; margin-bottom: 8px; mat-icon { color: var(--wi-ocean); } h3 { margin: 0; } } p { margin: 0; font-size: 14px; } ul { list-style: none; margin: 0 0 14px; padding: 0; display: grid; gap: 8px; font-size: 14px; li { display: flex; gap: 8px; align-items: center; } mat-icon { color: var(--wi-aqua); font-size: 20px; width: 20px; height: 20px; } } }
    .chip { display: inline-block; padding: 2px 10px; border-radius: 999px; background: #f1f5f9; color: #475569; font-size: 12px; font-weight: 600; &--on { background: #dcfce7; color: #166534; } }
    .small { display: flex; gap: 6px; align-items: center; font-size: 13px; margin: 16px 0 0; mat-icon { font-size: 18px; width: 18px; height: 18px; } }
    @media (max-width: 960px) { .integrations__grid { grid-template-columns: 1fr; } }
  `,
})
export class IntegrationsComponent {}
