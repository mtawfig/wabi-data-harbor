import { Component, computed, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule } from '@jsverse/transloco';
import { CreateOrganizationRequest, OrganizationDetail, TIME_ZONES, UpdateOrganizationRequest } from '../../../core/organizations/organizations.models';

/** Shared organization form: create mode adds the immutable code and the initial administrator. */
@Component({
  selector: 'app-organization-form',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslocoModule],
  templateUrl: './organization-form.component.html',
  styles: `
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px; }
    .span { grid-column: 1 / -1; }
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 8px; }
    @media (max-width: 760px) { .grid { grid-template-columns: 1fr; } }
  `,
})
export class OrganizationFormComponent {
  private readonly fb = inject(FormBuilder);

  readonly mode = input<'create' | 'edit'>('create');
  readonly initial = input<OrganizationDetail | null>(null);
  readonly busy = input(false);
  readonly saved = output<CreateOrganizationRequest | UpdateOrganizationRequest>();
  readonly cancelled = output<void>();

  readonly timeZones = TIME_ZONES;
  readonly isCreate = computed(() => this.mode() === 'create');

  readonly form = this.fb.nonNullable.group({
    organizationCode: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9-]{1,30}$/)]],
    organizationName: ['', [Validators.required, Validators.maxLength(200)]],
    organizationNameAr: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    contactEmail: ['', Validators.email],
    contactPhone: [''],
    countryCode: ['', Validators.pattern(/^[A-Za-z]{2}$/)],
    timeZone: ['Asia/Dubai', Validators.required],
    defaultLanguage: ['en' as 'en' | 'ar', Validators.required],
    initialAdministratorEmail: ['', [Validators.required, Validators.email]],
    initialAdministratorFullName: ['', Validators.required],
  });

  constructor() {
    queueMicrotask(() => this.applyInitial());
  }

  applyInitial(): void {
    const value = this.initial();
    if (this.mode() === 'edit') {
      this.form.controls.organizationCode.clearValidators();
      this.form.controls.initialAdministratorEmail.clearValidators();
      this.form.controls.initialAdministratorFullName.clearValidators();
      this.form.controls.organizationCode.updateValueAndValidity();
      this.form.controls.initialAdministratorEmail.updateValueAndValidity();
      this.form.controls.initialAdministratorFullName.updateValueAndValidity();
    }
    if (value) {
      this.form.patchValue({
        organizationCode: value.organizationCode,
        organizationName: value.organizationName,
        organizationNameAr: value.organizationNameAr,
        description: value.description ?? '',
        contactEmail: value.contactEmail ?? '',
        contactPhone: value.contactPhone ?? '',
        countryCode: value.countryCode ?? '',
        timeZone: value.timeZone,
        defaultLanguage: value.defaultLanguage,
      });
    }
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const common = {
      organizationName: v.organizationName,
      organizationNameAr: v.organizationNameAr,
      description: v.description || undefined,
      contactEmail: v.contactEmail || undefined,
      contactPhone: v.contactPhone || undefined,
      countryCode: v.countryCode ? v.countryCode.toUpperCase() : undefined,
      timeZone: v.timeZone,
      defaultLanguage: v.defaultLanguage,
    };
    this.saved.emit(
      this.isCreate()
        ? { ...common, organizationCode: v.organizationCode.toUpperCase(), initialAdministratorEmail: v.initialAdministratorEmail, initialAdministratorFullName: v.initialAdministratorFullName }
        : common,
    );
  }
}
