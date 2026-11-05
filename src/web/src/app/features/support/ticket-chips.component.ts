import { Component, input } from '@angular/core';
import { TranslocoModule } from '@jsverse/transloco';
import { TicketImpact, TicketStatus, TicketType } from '../../core/support/support.models';

/** Status, impact and type chips with stable colours. Codes come from the API; labels from translations. */
@Component({
  selector: 'app-ticket-status',
  imports: [TranslocoModule],
  template: `<span class="tchip" [attr.data-status]="status()">{{ 'support.statuses.' + (status() === 'PendingUserResponse' && adminView() ? 'PendingUserResponseAdmin' : status()) | transloco }}</span>`,
  styles: `
    .tchip { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: 12px; font-weight: 600; background: #e2e8f0; color: var(--wi-slate); white-space: nowrap; }
    .tchip[data-status='New'] { background: #dbeafe; color: #1e3a8a; }
    .tchip[data-status='Read'] { background: #e0f2fe; color: #075985; }
    .tchip[data-status='Started'] { background: #ccfbf1; color: #115e59; }
    .tchip[data-status='PendingUserResponse'] { background: #fef3c7; color: #92400e; }
    .tchip[data-status='Resolved'] { background: #dcfce7; color: #166534; }
    .tchip[data-status='Cancelled'] { background: #f1f5f9; color: #475569; }
  `,
})
export class TicketStatusComponent {
  readonly status = input.required<TicketStatus>();
  /** Platform administrators read "Waiting for user" where the requester reads "Waiting for you". */
  readonly adminView = input(false);
}

@Component({
  selector: 'app-ticket-impact',
  imports: [TranslocoModule],
  template: `<span class="ichip" [attr.data-impact]="impact()">{{ 'support.impacts.' + impact() | transloco }}</span>`,
  styles: `
    .ichip { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: 12px; font-weight: 600; background: #f1f5f9; color: #475569; white-space: nowrap; }
    .ichip[data-impact='Medium'] { background: #e0f2fe; color: #075985; }
    .ichip[data-impact='High'] { background: #ffedd5; color: #9a3412; }
    .ichip[data-impact='Critical'] { background: #fee2e2; color: #991b1b; }
  `,
})
export class TicketImpactComponent {
  readonly impact = input.required<TicketImpact>();
}

@Component({
  selector: 'app-ticket-type',
  imports: [TranslocoModule],
  template: `<span class="ychip" [attr.data-type]="type()">{{ 'support.types.' + type() | transloco }}</span>`,
  styles: `
    .ychip { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: 12px; font-weight: 600; background: #ede9fe; color: #5b21b6; white-space: nowrap; }
    .ychip[data-type='Incident'] { background: #fce7f3; color: #9d174d; }
  `,
})
export class TicketTypeComponent {
  readonly type = input.required<TicketType>();
}
