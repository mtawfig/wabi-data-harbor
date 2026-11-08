import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Role } from '../../../core/organizations/admin.models';
import { OrganizationAdminService } from '../../../core/organizations/admin.service';
import { problemMessage } from '../../../core/problem-message';
import { WiNumberPipe } from '../../../shared/format/locale-pipes';

@Component({
  selector: 'app-roles',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatTableModule, TranslocoModule, WiNumberPipe],
  templateUrl: './roles.component.html',
  styleUrl: './admin-catalog.scss',
})
export class RolesComponent {
  private readonly admin = inject(OrganizationAdminService);
  private readonly fb = inject(FormBuilder);
  private readonly transloco = inject(TranslocoService);

  readonly columns = ['code', 'nameEn', 'nameAr', 'kind', 'members', 'active', 'actions'];
  readonly items = signal<Role[]>([]);
  readonly editing = signal<Role | null>(null);
  readonly showForm = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    roleCode: ['', [Validators.required, Validators.pattern(/^[A-Za-z][A-Za-z0-9_-]{1,63}$/)]],
    nameEn: ['', [Validators.required, Validators.maxLength(200)]],
    nameAr: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.admin.listRoles().subscribe((items) => this.items.set(items));
  }

  startCreate(): void {
    this.editing.set(null);
    this.form.reset({ roleCode: '', nameEn: '', nameAr: '', description: '', isActive: true });
    this.form.controls.roleCode.enable();
    this.form.controls.isActive.enable();
    this.showForm.set(true);
    this.error.set(null);
  }

  startEdit(item: Role): void {
    this.editing.set(item);
    this.form.reset({ roleCode: item.roleCode, nameEn: item.nameEn, nameAr: item.nameAr, description: item.description ?? '', isActive: item.isActive });
    if (item.isSystem) {
      this.form.controls.roleCode.disable();
      this.form.controls.isActive.disable();
    } else {
      this.form.controls.roleCode.enable();
      this.form.controls.isActive.enable();
    }
    this.showForm.set(true);
    this.error.set(null);
  }

  cancel(): void {
    this.showForm.set(false);
  }

  submit(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const value = this.form.getRawValue();
    const request = { ...value, description: value.description || undefined };
    const editing = this.editing();
    const call = editing ? this.admin.updateRole(editing.roleId, request) : this.admin.createRole(request);
    call.subscribe({
      next: () => {
        this.busy.set(false);
        this.showForm.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => {
        this.error.set(problemMessage(response, this.transloco));
        this.busy.set(false);
      },
    });
  }

  remove(item: Role): void {
    if (!confirm(this.transloco.translate('admin.roles.confirmDelete', { code: item.roleCode }))) {
      return;
    }
    this.busy.set(true);
    this.admin.deleteRole(item.roleId).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => {
        this.error.set(problemMessage(response, this.transloco));
        this.busy.set(false);
      },
    });
  }
}
