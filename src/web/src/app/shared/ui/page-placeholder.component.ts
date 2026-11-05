import { Component, inject, input } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule } from '@jsverse/transloco';

/** Honest placeholder for routes whose feature arrives in a later phase (title and phase come from route data). */
@Component({
  selector: 'app-page-placeholder',
  imports: [MatIconModule, TranslocoModule],
  template: `
    <section class="wi-page">
      <header class="wi-page__header">
        <div>
          <p class="wi-eyebrow">{{ 'common.comingSoon' | transloco: { phase: phase() } }}</p>
          <h1>{{ titleKey() | transloco }}</h1>
        </div>
      </header>
      <div class="wi-card placeholder">
        <mat-icon class="placeholder__icon">construction</mat-icon>
        <p class="wi-muted">{{ 'common.comingSoonDetail' | transloco: { phase: phase() } }}</p>
      </div>
    </section>
  `,
  styles: `
    .placeholder { display: flex; align-items: center; gap: 16px; }
    .placeholder__icon { color: var(--wi-aqua); font-size: 36px; width: 36px; height: 36px; }
  `,
})
export class PagePlaceholderComponent {
  private readonly route = inject(ActivatedRoute);
  readonly titleKey = input<string>(this.route.snapshot.data['titleKey'] ?? 'common.appName');
  readonly phase = input<string>(this.route.snapshot.data['phase'] ?? 'later');
}
