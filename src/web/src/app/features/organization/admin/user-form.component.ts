import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { AuthService } from '../../../core/auth/auth.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { Department, OrganizationUser, Role, Section } from '../../../core/organizations/admin.models';
import { OrganizationAdminService } from '../../../core/organizations/admin.service';
import { problemMessage } from '../../../core/problem-message';

/** Create or edit an organization user. Temporary passwords are emailed; this page never shows one. */
@Component({
  selector: 'app-user-form',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, RouterLink, TranslocoModule],
  templateUrl: './user-form.component.html',
  styleUrl: './admin-catalog.scss',
})
export class UserFormComponent {
  private readonly admin = inject(OrganizationAdminService);
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  /** Route parameter; absent on /new. */
  readonly id = input<string | undefined>();
  readonly user = signal<OrganizationUser | null>(null);
  readonly departments = signal<Department[]>([]);
  readonly sections = signal<Section[]>([]);
  readonly roles = signal<Role[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly isCreate = computed(() => !this.id());
  readonly isSelf = computed(() => this.auth.session()?.id === this.id());
  readonly departmentNames = computed(() => new Map(this.departments().map((d) => [d.departmentId, this.language.current() === 'ar' ? d.departmentNameAr : d.departmentNameEn])));
  /** Department chosen in the form, mirrored as a signal so dependent views update. */
  readonly selectedDepartment = signal('');
  readonly sectionsOfDepartment = computed(() => this.sections().filter((s) => s.departmentId === this.selectedDepartment()));

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    employeeNumber: [''],
    phoneNumber: [''],
    departmentId: [''],
    sectionId: [''],
    preferredLanguage: ['en' as 'en' | 'ar', Validators.required],
    roleCodes: [[] as string[]],
  });

  constructor() {
    this.admin.listDepartments(false).subscribe((d) => this.departments.set(d));
    this.admin.listSections(undefined, false).subscribe((s) => this.sections.set(s));
    this.admin.listRoles().subscribe((r) => this.roles.set(r.filter((role) => role.isActive)));
    this.form.controls.departmentId.valueChanges.subscribe((departmentId) => {
      this.selectedDepartment.set(departmentId);
      this.form.controls.sectionId.setValue('');
      if (departmentId) {
        this.form.controls.sectionId.enable();
      } else {
        this.form.controls.sectionId.disable();
      }
    });
    this.form.controls.sectionId.disable();
    queueMicrotask(() => this.loadUser());
  }

  private loadUser(): void {
    const id = this.id();
    if (!id) {
      return;
    }
    this.form.controls.email.disable();
    this.admin.getUser(id).subscribe({
      next: (user) => {
        this.user.set(user);
        this.form.patchValue({
          email: user.email,
          fullName: user.fullName,
          employeeNumber: user.employeeNumber ?? '',
          phoneNumber: user.phoneNumber ?? '',
          departmentId: user.departmentId ?? '',
          preferredLanguage: user.preferredLanguage,
          roleCodes: user.roles,
        });
        this.form.controls.sectionId.setValue(user.sectionId ?? '');
      },
      error: () => void this.router.navigate(['/app/admin/users']),
    });
  }

  roleLabel(role: Role): string {
    return this.language.current() === 'ar' ? role.nameAr : role.nameEn;
  }

  toggleRole(code: string, checked: boolean): void {
    const current = new Set(this.form.controls.roleCodes.value);
    if (checked) {
      current.add(code);
    } else {
      current.delete(code);
    }
    this.form.controls.roleCodes.setValue([...current]);
  }

  submit(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const v = this.form.getRawValue();
    const payload = {
      fullName: v.fullName,
      employeeNumber: v.employeeNumber || undefined,
      phoneNumber: v.phoneNumber || undefined,
      departmentId: v.departmentId || undefined,
      sectionId: v.sectionId || undefined,
      preferredLanguage: v.preferredLanguage,
      roleCodes: v.roleCodes,
    };
    const id = this.id();
    const call = id ? this.admin.updateUser(id, payload) : this.admin.createUser({ ...payload, email: v.email });
    call.subscribe({
      next: (user) => {
        this.snack.open(this.transloco.translate(id ? 'admin.users.saved' : 'admin.users.created', { email: user.email }), undefined, { duration: 6000 });
        void this.router.navigate(['/app/admin/users', user.userId]);
        this.user.set(user);
        this.busy.set(false);
      },
      error: (response: HttpErrorResponse) => {
        this.error.set(problemMessage(response, this.transloco));
        this.busy.set(false);
      },
    });
  }

  setStatus(status: 'Active' | 'Inactive'): void {
    const id = this.id();
    if (!id) {
      return;
    }
    this.busy.set(true);
    this.admin.setUserStatus(id, status).subscribe({
      next: (user) => {
        this.user.set(user);
        this.snack.open(this.transloco.translate(`admin.users.status${status}`), undefined, { duration: 4000 });
        this.busy.set(false);
      },
      error: (response: HttpErrorResponse) => {
        this.error.set(problemMessage(response, this.transloco));
        this.busy.set(false);
      },
    });
  }

  resetPassword(): void {
    const id = this.id();
    if (!id) {
      return;
    }
    this.busy.set(true);
    this.admin.resetUserPassword(id).subscribe({
      next: (r) => {
        this.snack.open(this.transloco.translate('admin.users.resetSent', { email: r.deliveredTo }), undefined, { duration: 6000 });
        this.busy.set(false);
      },
      error: (response: HttpErrorResponse) => {
        this.error.set(problemMessage(response, this.transloco));
        this.busy.set(false);
      },
    });
  }
}
