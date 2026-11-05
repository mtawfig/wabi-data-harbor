import { Component, input } from '@angular/core';
import { TranslocoModule } from '@jsverse/transloco';
import { OrganizationStatus } from '../../../core/organizations/organizations.models';

@Component({
  selector: 'app-organization-status-chip',
  imports: [TranslocoModule],
  template: `<span class="chip" [attr.data-status]="status()">{{ 'organizations.status.' + status() | transloco }}</span>`,
  styles: `
    .chip { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: 12px; font-weight: 600; background: #e2e8f0; color: var(--wi-slate); }
    .chip[data-status='Active'] { background: #dcfce7; color: #166534; }
    .chip[data-status='Suspended'] { background: #fef3c7; color: #92400e; }
    .chip[data-status='Deactivated'] { background: #fee2e2; color: #991b1b; }
  `,
})
export class OrganizationStatusChipComponent {
  readonly status = input.required<OrganizationStatus>();
}
