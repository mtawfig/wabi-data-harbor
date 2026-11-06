import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { ProblemDetails } from '../../../core/auth/session.model';
import { OrganizationProfile, TIME_ZONES } from '../../../core/organizations/organizations.models';
import { OrganizationProfileService } from '../../../core/organizations/organizations.service';
import { problemMessage } from '../../../core/problem-message';
import { OrganizationAiSettingsComponent } from './organization-ai-settings.component';

/** Organization Settings: profile (P2.1) and the support-access approval policy (P5.2). Integrations and defaults arrive in later phases. */
@Component({
  selector: 'app-organization-settings',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, TranslocoModule, OrganizationAiSettingsComponent],
  templateUrl: './organization-settings.component.html',
  styles: `
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px; }
    .span { grid-column: 1 / -1; }
    .actions { display: flex; justify-content: flex-end; margin-top: 8px; }
    .error { display: flex; gap: 8px; align-items: center; background: #fef2f2; color: #991b1b; margin-bottom: 16px; }
    @media (max-width: 760px) { .grid { grid-template-columns: 1fr; } }
  `,
})
export class OrganizationSettingsComponent {
  private readonly profiles = inject(OrganizationProfileService);
  private readonly fb = inject(FormBuilder);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly profile = signal<OrganizationProfile | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly timeZones = TIME_ZONES;
  readonly logoVersion = signal(Date.now());
  readonly logoUrl = () => this.profiles.logoUrl(this.logoVersion());

  readonly form = this.fb.nonNullable.group({
    organizationName: ['', [Validators.required, Validators.maxLength(200)]],
    organizationNameAr: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    contactEmail: ['', Validators.email],
    contactPhone: [''],
    timeZone: ['UTC', Validators.required],
    defaultLanguage: ['en' as 'en' | 'ar', Validators.required],
    requireSecondAdminApprovalForSupportAccess: [false],
  });

  constructor() {
    this.profiles.get().subscribe((p) => {
      this.profile.set(p);
      this.form.patchValue({
        organizationName: p.organizationName,
        organizationNameAr: p.organizationNameAr,
        description: p.description ?? '',
        contactEmail: p.contactEmail ?? '',
        contactPhone: p.contactPhone ?? '',
        timeZone: p.timeZone,
        defaultLanguage: p.defaultLanguage,
        requireSecondAdminApprovalForSupportAccess: p.requireSecondAdminApprovalForSupportAccess ?? false,
      });
    });
  }

  submit(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const v = this.form.getRawValue();
    this.profiles.update({ ...v, description: v.description || undefined, contactEmail: v.contactEmail || undefined, contactPhone: v.contactPhone || undefined }).subscribe({
      next: (p) => {
        this.profile.set(p);
        this.snack.open(this.transloco.translate('organizations.saved'), undefined, { duration: 4000 });
        this.busy.set(false);
      },
      error: (response: HttpErrorResponse) => {
        const problem = response.error as ProblemDetails | undefined;
        this.error.set(problem?.detail || this.transloco.translate('organizations.errors.unknown'));
        this.busy.set(false);
      },
    });
  }

  uploadLogo(files: FileList | null): void {
    const file = files?.item(0);
    if (!file) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.profiles.uploadLogo(file).subscribe({
      next: () => {
        this.busy.set(false);
        this.logoVersion.set(Date.now());
        this.profile.update((p) => (p ? { ...p, hasLogo: true } : p));
        this.snack.open(this.transloco.translate('organizations.settings.logoSaved'), undefined, { duration: 3000 });
      },
      error: (e: HttpErrorResponse) => {
        this.busy.set(false);
        this.error.set(this.describe(e));
      },
    });
  }

  removeLogo(): void {
    this.busy.set(true);
    this.profiles.removeLogo().subscribe({
      next: () => {
        this.busy.set(false);
        this.profile.update((p) => (p ? { ...p, hasLogo: false } : p));
      },
      error: (e: HttpErrorResponse) => {
        this.busy.set(false);
        this.error.set(this.describe(e));
      },
    });
  }

  private describe(e: HttpErrorResponse): string {
    return problemMessage(e, this.transloco);
  }
}
