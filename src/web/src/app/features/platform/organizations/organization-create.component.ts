import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { ProblemDetails, problemCode } from '../../../core/auth/session.model';
import { CreateOrganizationRequest, UpdateOrganizationRequest } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { OrganizationFormComponent } from './organization-form.component';

@Component({
  selector: 'app-organization-create',
  imports: [MatIconModule, RouterLink, TranslocoModule, OrganizationFormComponent],
  template: `
    <section class="wi-page">
      <header class="wi-page__header">
        <div>
          <a routerLink="/platform/organizations" class="back"><mat-icon class="wi-flip-rtl">arrow_back</mat-icon>{{ 'organizations.title' | transloco }}</a>
          <h1>{{ 'organizations.create.title' | transloco }}</h1>
          <p class="wi-muted">{{ 'organizations.create.subtitle' | transloco }}</p>
        </div>
      </header>
      @if (error(); as e) {
        <div class="wi-card error" role="alert" data-testid="org-error"><mat-icon>error_outline</mat-icon><span>{{ e }}</span></div>
      }
      <div class="wi-card">
        <app-organization-form mode="create" [busy]="busy()" (saved)="save($event)" (cancelled)="cancel()" />
      </div>
    </section>
  `,
  styles: `
    .back { display: inline-flex; align-items: center; gap: 4px; font-size: 14px; margin-bottom: 8px; }
    .error { display: flex; gap: 8px; align-items: center; background: #fef2f2; color: #991b1b; margin-bottom: 16px; }
  `,
})
export class OrganizationCreateComponent {
  private readonly organizations = inject(OrganizationsService);
  private readonly router = inject(Router);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  save(request: CreateOrganizationRequest | UpdateOrganizationRequest): void {
    this.busy.set(true);
    this.error.set(null);
    this.organizations.create(request as CreateOrganizationRequest).subscribe({
      next: (created) => {
        this.snack.open(this.transloco.translate('organizations.create.success', { email: created.initialAdministratorEmail }), undefined, { duration: 6000 });
        void this.router.navigate(['/platform/organizations', created.organizationId]);
      },
      error: (response: HttpErrorResponse) => {
        const problem = response.error as ProblemDetails | undefined;
        const code = problemCode(problem) ?? 'unknown';
        const detail = problem?.detail ?? '';
        const key = `organizations.errors.${code}`;
        const translated = this.transloco.translate(key);
        this.error.set(translated === key ? detail || this.transloco.translate('organizations.errors.unknown') : translated);
        this.busy.set(false);
      },
    });
  }

  cancel(): void {
    void this.router.navigate(['/platform/organizations']);
  }
}
