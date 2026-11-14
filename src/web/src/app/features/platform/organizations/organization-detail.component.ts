import { HttpErrorResponse } from '@angular/common/http';
import { problemMessage } from '../../../core/problem-message';
import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { CreateOrganizationRequest, OrganizationDetail, OrganizationStatus, UpdateOrganizationRequest } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { OrganizationFormComponent } from './organization-form.component';
import { OrganizationStatusChipComponent } from './organization-status-chip.component';
import { OrganizationPlanComponent } from '../plans/organization-plan.component';
import { WiDatePipe, WiNumberPipe } from '../../../shared/format/locale-pipes';

/** Platform view of one organization: identity, counts, onboarding state and lifecycle actions. No application names. */
@Component({
  selector: 'app-organization-detail',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, RouterLink, TranslocoModule, OrganizationFormComponent, OrganizationStatusChipComponent, OrganizationPlanComponent, WiDatePipe, WiNumberPipe],
  templateUrl: './organization-detail.component.html',
  styleUrl: './organization-detail.component.scss',
})
export class OrganizationDetailComponent {
  private readonly organizations = inject(OrganizationsService);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly id = input.required<string>();
  readonly organization = signal<OrganizationDetail | null>(null);
  readonly editing = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly reason = signal('');

  constructor() {
    queueMicrotask(() => this.load());
  }

  load(): void {
    this.organizations.get(this.id()).subscribe({ next: (o) => this.organization.set(o), error: () => this.organization.set(null) });
  }

  save(request: CreateOrganizationRequest | UpdateOrganizationRequest): void {
    this.run(this.organizations.update(this.id(), request as UpdateOrganizationRequest), 'organizations.saved', () => this.editing.set(false));
  }

  setStatus(status: OrganizationStatus): void {
    this.run(this.organizations.setStatus(this.id(), status, this.reason() || undefined), `organizations.statusChanged.${status}`, () => this.reason.set(''));
  }

  resend(): void {
    this.busy.set(true);
    this.error.set(null);
    this.organizations.resendInitialAdministrator(this.id()).subscribe({
      next: (r) => {
        this.snack.open(this.transloco.translate('organizations.resent', { email: r.deliveredTo }), undefined, { duration: 6000 });
        this.busy.set(false);
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  private run(call: ReturnType<OrganizationsService['update']>, messageKey: string, after: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    call.subscribe({
      next: (o) => {
        this.organization.set(o);
        this.snack.open(this.transloco.translate(messageKey), undefined, { duration: 4000 });
        this.busy.set(false);
        after();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  private fail(response: HttpErrorResponse): void {
    this.error.set(problemMessage(response, this.transloco, 'organizations.errors.unknown'));
    this.busy.set(false);
  }
}
