import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { AuthService } from '../../../core/auth/auth.service';
import { HistoryService } from '../../../core/history/history.service';
import { HistorySummary } from '../../../core/history/history.models';
import { LanguageService } from '../../../core/i18n/language.service';
import { SharingService } from '../../../core/sharing/sharing.service';
import { TicketWidgetComponent } from '../../support/ticket-widget.component';
import { WiDatePipe } from '../../../shared/format/locale-pipes';

/** User dashboard: greeting, quick actions, recent analyses, items shared with the user and the support ticket widget. */
@Component({
  selector: 'app-user-dashboard',
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoModule, TicketWidgetComponent, WiDatePipe],
  template: `
    <section class="wi-page dashboard">
      <header class="wi-page__header">
        <div>
          <p class="wi-eyebrow">{{ 'common.organization' | transloco }}</p>
          <h1>{{ 'dashboard.welcome' | transloco: { name: auth.session()?.displayName } }}</h1>
          <p class="wi-muted">{{ 'dashboard.subtitle' | transloco }}</p>
        </div>
        <a mat-flat-button routerLink="/app/ask" data-testid="dashboard-ask"><mat-icon>auto_awesome</mat-icon>{{ 'nav.org.ask' | transloco }}</a>
      </header>
      <div class="tiles">
        <a class="wi-card tile" routerLink="/app/history"><mat-icon>history</mat-icon><strong>{{ historyTotal() ?? '—' }}</strong><span>{{ 'dashboard.tiles.history' | transloco }}</span></a>
        <a class="wi-card tile" routerLink="/app/reports"><mat-icon>description</mat-icon><strong>{{ reportsTotal() ?? '—' }}</strong><span>{{ 'dashboard.tiles.reports' | transloco }}</span></a>
        <a class="wi-card tile" routerLink="/app/shared-with-me"><mat-icon>inbox</mat-icon><strong>{{ sharedTotal() ?? '—' }}</strong><span>{{ 'dashboard.tiles.shared' | transloco }}</span></a>
        <a class="wi-card tile" routerLink="/app/favorites"><mat-icon>star</mat-icon><strong>{{ favoritesTotal() ?? '—' }}</strong><span>{{ 'dashboard.tiles.favorites' | transloco }}</span></a>
      </div>
      <div class="grid">
        <div class="wi-card recent" data-testid="dashboard-recent">
          <h3>{{ 'dashboard.recent.title' | transloco }}</h3>
          <ul>
            @for (h of recent(); track h.historyId) {
              <li><a [routerLink]="['/app/history', h.historyId]"><strong>{{ h.title }}</strong><br /><span class="wi-muted small">{{ 'history.types.' + h.analysisType | transloco }} · {{ language.current() === 'ar' ? h.applicationNameAr : h.applicationName }} · {{ (h.updatedAt || h.createdAt) | wiDate: 'medium' }}</span></a></li>
            } @empty { <li class="wi-muted">{{ 'dashboard.recent.empty' | transloco }}</li> }
          </ul>
        </div>
        <app-ticket-widget area="app" />
      </div>
    </section>
  `,
  styles: `
    .tiles { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 16px; }
    .tile { display: grid; gap: 4px; color: inherit; text-decoration: none; padding: 18px 20px; mat-icon { color: var(--wi-ocean); } strong { font-size: 26px; } span { color: var(--wi-slate); font-size: 13px; } &:hover { border-color: var(--wi-ocean); } }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
    .recent { h3 { margin: 0 0 8px; } ul { list-style: none; margin: 0; padding: 0; } li { padding: 10px 0; border-top: 1px solid #f1f5f9; &:first-child { border-top: 0; } } a { color: inherit; text-decoration: none; } strong { color: var(--wi-navy); } }
    .small { font-size: 12px; }
    @media (max-width: 960px) { .grid, .tiles { grid-template-columns: 1fr 1fr; } }
    @media (max-width: 600px) { .grid, .tiles { grid-template-columns: 1fr; } }
  `,
})
export class UserDashboardComponent {
  readonly auth = inject(AuthService);
  readonly language = inject(LanguageService);
  private readonly history = inject(HistoryService);
  private readonly sharing = inject(SharingService);
  readonly recent = signal<HistorySummary[]>([]);
  readonly historyTotal = signal<number | null>(null);
  readonly reportsTotal = signal<number | null>(null);
  readonly sharedTotal = signal<number | null>(null);
  readonly favoritesTotal = signal<number | null>(null);

  constructor() {
    this.history.list({ page: 1, pageSize: 5 }).subscribe({ next: (r) => { this.recent.set(r.items); this.historyTotal.set(r.totalCount); }, error: () => undefined });
    this.history.list({ page: 1, pageSize: 1, favorite: true }).subscribe({ next: (r) => this.favoritesTotal.set(r.totalCount), error: () => undefined });
    this.history.reports(1, 1).subscribe({ next: (r) => this.reportsTotal.set(r.totalCount), error: () => undefined });
    this.sharing.withMe(1, 1).subscribe({ next: (r) => this.sharedTotal.set(r.totalCount), error: () => undefined });
  }
}
