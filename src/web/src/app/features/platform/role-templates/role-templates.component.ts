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
import { RoleTemplate } from '../../../core/organizations/admin.models';
import { RoleTemplatesService } from '../../../core/organizations/admin.service';
import { problemMessage } from '../../../core/problem-message';

/** Global Role Templates: copied into every new organization; existing organizations are never changed. */
@Component({
  selector: 'app-role-templates',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatTableModule, TranslocoModule],
  templateUrl: './role-templates.component.html',
  styleUrl: '../../organization/admin/admin-catalog.scss',
})
export class RoleTemplatesComponent {
  private readonly templates = inject(RoleTemplatesService);
  private readonly fb = inject(FormBuilder);
  private readonly transloco = inject(TranslocoService);

  readonly columns = ['order', 'code', 'nameEn', 'nameAr', 'kind', 'version', 'active', 'actions'];
  readonly items = signal<RoleTemplate[]>([]);
  readonly editing = signal<RoleTemplate | null>(null);
  readonly showForm = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    roleCode: ['', [Validators.required, Validators.pattern(/^[A-Za-z][A-Za-z0-9_-]{1,63}$/)]],
    nameEn: ['', [Validators.required, Validators.maxLength(200)]],
    nameAr: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    isActive: [true],
    sortOrder: [100, [Validators.min(0), Validators.max(1000)]],
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.templates.list().subscribe((items) => this.items.set(items));
  }

  startCreate(): void {
    this.editing.set(null);
    this.form.reset({ roleCode: '', nameEn: '', nameAr: '', description: '', isActive: true, sortOrder: 100 });
    this.form.controls.roleCode.enable();
    this.form.controls.isActive.enable();
    this.showForm.set(true);
    this.error.set(null);
  }

  startEdit(item: RoleTemplate): void {
    this.editing.set(item);
    this.form.reset({ roleCode: item.roleCode, nameEn: item.nameEn, nameAr: item.nameAr, description: item.description ?? '', isActive: item.isActive, sortOrder: item.sortOrder });
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
    const call = editing ? this.templates.update(editing.templateId, request) : this.templates.create(request);
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
