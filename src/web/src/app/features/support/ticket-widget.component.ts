import { Component, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { LanguageService } from '../../core/i18n/language.service';
import { SupportArea, TicketSummary } from '../../core/support/support.models';
import { SupportService } from '../../core/support/support.service';
import { TicketImpactComponent, TicketStatusComponent } from './ticket-chips.component';
import { WiDatePipe } from '../../shared/format/locale-pipes';

/** Dashboard widget: the five most recently active open tickets plus the open count. Used on both dashboards. */
@Component({
  selector: 'app-ticket-widget',
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoModule, TicketStatusComponent, TicketImpactComponent, WiDatePipe],
  template: `
    <div class="wi-card widget" data-testid="ticket-widget">
      <div class="widget__head">
        <h3>{{ (area() === 'platform' ? 'support.queueWidget' : 'support.widgetTitle') | transloco }} <span class="count">{{ total() }} {{ 'support.widgetOpen' | transloco }}</span></h3>
        <a mat-button [routerLink]="listLink()">{{ (area() === 'platform' ? 'support.queueAll' : 'support.widgetAll') | transloco }}<mat-icon class="wi-flip-rtl">arrow_forward</mat-icon></a>
      </div>
      @if (items().length === 0) { <p class="wi-muted">{{ 'support.empty' | transloco }}</p> }
      <ul>
        @for (t of items(); track t.ticketId) {
          <li>
            <a [routerLink]="[listLink(), t.ticketId]"><span class="wi-ltr ref">{{ t.referenceNumber }}</span> {{ t.subject }}</a>
            <span class="meta">@if (area() === 'platform') { {{ organizationName(t) }} · }{{ t.lastActivityAt | wiDate: 'short' }}</span>
            <span class="chips"><app-ticket-impact [impact]="t.impact" /><app-ticket-status [status]="t.status" [adminView]="area() === 'platform'" /></span>
          </li>
        }
      </ul>
    </div>
  `,
  styles: `
    .widget__head { display: flex; align-items: center; justify-content: space-between; gap: 8px; h3 { margin: 0; } }
    .count { font-size: 13px; font-weight: 500; color: var(--wi-slate); margin-inline-start: 8px; }
    ul { list-style: none; margin: 12px 0 0; padding: 0; }
    li { display: grid; grid-template-columns: 1fr auto; gap: 4px 12px; padding: 10px 0; border-top: 1px solid var(--wi-border); align-items: center; a { font-weight: 500; color: var(--wi-navy); } .ref { color: var(--wi-ocean); margin-inline-end: 6px; } .meta { grid-column: 1; font-size: 12px; color: var(--wi-slate); } .chips { grid-row: 1 / span 2; display: flex; gap: 6px; } }
  `,
})
export class TicketWidgetComponent {
  private readonly support = inject(SupportService);
  private readonly language = inject(LanguageService);

  readonly area = input<SupportArea>('app');
  readonly items = signal<TicketSummary[]>([]);
  readonly total = signal(0);
  readonly listLink = computed(() => (this.area() === 'platform' ? '/platform/support' : '/app/support'));

  constructor() {
    queueMicrotask(() =>
      this.support.list(this.area(), { page: 1, pageSize: 5, open: true, descending: true }).subscribe({
        next: (r) => {
          this.items.set(r.items);
          this.total.set(r.totalCount);
        },
        error: () => undefined,
      }),
    );
  }

  organizationName(t: TicketSummary): string {
    return this.language.current() === 'ar' ? t.organizationNameAr : t.organizationName;
  }
}
