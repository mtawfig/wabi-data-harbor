import { Component, input } from '@angular/core';
import { TranslocoModule } from '@jsverse/transloco';
import { GrantStatus } from '../../core/support/support-access.models';

@Component({
  selector: 'app-grant-status',
  imports: [TranslocoModule],
  template: `<span class="gchip" [attr.data-status]="status()">{{ 'supportAccess.statuses.' + status() | transloco }}</span>`,
  styles: `
    .gchip { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: 12px; font-weight: 600; background: #e2e8f0; color: var(--wi-slate); white-space: nowrap; }
    .gchip[data-status='Proposed'] { background: #fef3c7; color: #92400e; }
    .gchip[data-status='Approved'] { background: #dbeafe; color: #1e3a8a; }
    .gchip[data-status='Active'] { background: #dcfce7; color: #166534; }
    .gchip[data-status='Completed'] { background: #e0f2fe; color: #075985; }
    .gchip[data-status='Declined'], .gchip[data-status='Revoked'] { background: #fee2e2; color: #991b1b; }
  `,
})
export class GrantStatusComponent {
  readonly status = input.required<GrantStatus>();
}
