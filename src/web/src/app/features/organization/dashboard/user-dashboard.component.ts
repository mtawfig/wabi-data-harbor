import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { AuthService } from '../../../core/auth/auth.service';
import { TicketWidgetComponent } from '../../support/ticket-widget.component';

/** User dashboard: greeting, support ticket widget (P4.2) and a shortcut to the workspace. Usage widgets arrive with P7.2 / P9. */
@Component({
  selector: 'app-user-dashboard',
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoModule, TicketWidgetComponent],
  template: `
    <section class="wi-page">
      <header class="wi-page__header">
        <div>
          <p class="wi-eyebrow">{{ 'common.organization' | transloco }}</p>
          <h1>{{ 'dashboard.welcome' | transloco: { name: auth.session()?.displayName } }}</h1>
          <p class="wi-muted">{{ 'dashboard.subtitle' | transloco }}</p>
        </div>
        <a mat-flat-button routerLink="/app/ask"><mat-icon>auto_awesome</mat-icon>{{ 'nav.org.ask' | transloco }}</a>
      </header>
      <div class="grid">
        <app-ticket-widget area="app" />
        <div class="wi-card wi-muted">{{ 'common.comingSoonDetail' | transloco: { phase: '7 / 9' } }}</div>
      </div>
    </section>
  `,
  styles: `.grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; } @media (max-width: 960px) { .grid { grid-template-columns: 1fr; } }`,
})
export class UserDashboardComponent {
  readonly auth = inject(AuthService);
}
