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
import { Department } from '../../../core/organizations/admin.models';
import { OrganizationAdminService } from '../../../core/organizations/admin.service';
import { problemMessage } from '../../../core/problem-message';

@Component({
  selector: 'app-departments',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatTableModule, TranslocoModule],
  templateUrl: './departments.component.html',
  styleUrl: './admin-catalog.scss',
})
export class DepartmentsComponent {
  private readonly admin = inject(OrganizationAdminService);
  private readonly fb = inject(FormBuilder);
  private readonly transloco = inject(TranslocoService);

  readonly columns = ['code', 'nameEn', 'nameAr', 'sections', 'users', 'active', 'actions'];
  readonly items = signal<Department[]>([]);
  readonly editing = signal<Department | null>(null);
  readonly showForm = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    departmentCode: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9-]{0,31}$/)]],
    departmentNameEn: ['', [Validators.required, Validators.maxLength(200)]],
    departmentNameAr: ['', [Validators.required, Validators.maxLength(200)]],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.admin.listDepartments().subscribe((items) => this.items.set(items));
  }

  startCreate(): void {
    this.editing.set(null);
    this.form.reset({ departmentCode: '', departmentNameEn: '', departmentNameAr: '', isActive: true });
    this.showForm.set(true);
    this.error.set(null);
  }

  startEdit(item: Department): void {
    this.editing.set(item);
    this.form.reset({ departmentCode: item.departmentCode, departmentNameEn: item.departmentNameEn, departmentNameAr: item.departmentNameAr, isActive: item.isActive });
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
    const editing = this.editing();
    const call = editing ? this.admin.updateDepartment(editing.departmentId, value) : this.admin.createDepartment(value);
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
}
