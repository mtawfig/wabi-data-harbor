import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { Department, Section } from '../../../core/organizations/admin.models';
import { OrganizationAdminService } from '../../../core/organizations/admin.service';
import { problemMessage } from '../../../core/problem-message';

@Component({
  selector: 'app-sections',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatTableModule, TranslocoModule],
  templateUrl: './sections.component.html',
  styleUrl: './admin-catalog.scss',
})
export class SectionsComponent {
  private readonly admin = inject(OrganizationAdminService);
  private readonly fb = inject(FormBuilder);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly columns = ['department', 'code', 'nameEn', 'nameAr', 'users', 'active', 'actions'];
  readonly departments = signal<Department[]>([]);
  readonly items = signal<Section[]>([]);
  readonly filterDepartment = signal<string | null>(null);
  readonly editing = signal<Section | null>(null);
  readonly showForm = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly departmentNames = computed(() => new Map(this.departments().map((d) => [d.departmentId, this.language.current() === 'ar' ? d.departmentNameAr : d.departmentNameEn])));

  readonly form = this.fb.nonNullable.group({
    departmentId: ['', Validators.required],
    sectionCode: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9-]{0,31}$/)]],
    sectionNameEn: ['', [Validators.required, Validators.maxLength(200)]],
    sectionNameAr: ['', [Validators.required, Validators.maxLength(200)]],
    isActive: [true],
  });

  constructor() {
    this.admin.listDepartments().subscribe((d) => this.departments.set(d));
    this.load();
  }

  load(): void {
    this.admin.listSections(this.filterDepartment() ?? undefined).subscribe((items) => this.items.set(items));
  }

  startCreate(): void {
    this.editing.set(null);
    this.form.reset({ departmentId: this.filterDepartment() ?? '', sectionCode: '', sectionNameEn: '', sectionNameAr: '', isActive: true });
    this.form.controls.departmentId.enable();
    this.showForm.set(true);
    this.error.set(null);
  }

  startEdit(item: Section): void {
    this.editing.set(item);
    this.form.reset({ departmentId: item.departmentId, sectionCode: item.sectionCode, sectionNameEn: item.sectionNameEn, sectionNameAr: item.sectionNameAr, isActive: item.isActive });
    this.form.controls.departmentId.disable();
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
    const call = editing ? this.admin.updateSection(editing.sectionId, value) : this.admin.createSection(value);
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
