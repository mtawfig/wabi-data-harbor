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
import { PlansService, ServicePlan } from '../../../core/plans/plans.service';
import { problemMessage } from '../../../core/problem-message';
import { WiNumberPipe } from '../../../shared/format/locale-pipes';

/** Service plans (P13.46): each sets how much an organization's AI requests may cost in a month. */
@Component({
  selector: 'app-plans',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatTableModule, TranslocoModule, WiNumberPipe],
  templateUrl: './plans.component.html',
  styleUrl: '../../organization/admin/admin-catalog.scss',
})
export class PlansComponent {
  private readonly plans = inject(PlansService);
  private readonly fb = inject(FormBuilder);
  private readonly transloco = inject(TranslocoService);

  readonly columns = ['code', 'nameEn', 'nameAr', 'limit', 'organizations', 'active', 'actions'];
  readonly items = signal<ServicePlan[]>([]);
  readonly editing = signal<ServicePlan | null>(null);
  readonly showForm = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    planCode: ['', [Validators.required, Validators.pattern(/^[A-Za-z][A-Za-z0-9-]{1,31}$/)]],
    nameEn: ['', [Validators.required, Validators.maxLength(100)]],
    nameAr: ['', [Validators.required, Validators.maxLength(100)]],
    // Empty means no limit.
    monthlyAiSpendLimit: this.fb.control<number | null>(null, [Validators.min(0.01), Validators.max(1_000_000)]),
    isActive: [true],
    sortOrder: [100, [Validators.min(0), Validators.max(1000)]],
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.plans.list().subscribe((items) => this.items.set(items));
  }

  startCreate(): void {
    this.editing.set(null);
    this.form.reset({ planCode: '', nameEn: '', nameAr: '', monthlyAiSpendLimit: null, isActive: true, sortOrder: 100 });
    this.form.controls.planCode.enable();
    this.showForm.set(true);
    this.error.set(null);
  }

  startEdit(item: ServicePlan): void {
    this.editing.set(item);
    this.form.reset({ planCode: item.planCode, nameEn: item.nameEn, nameAr: item.nameAr, monthlyAiSpendLimit: item.monthlyAiSpendLimit ?? null, isActive: item.isActive, sortOrder: item.sortOrder });
    // The code is the plan's stable identity.
    this.form.controls.planCode.disable();
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
    const request = { ...value, planCode: value.planCode.toUpperCase(), monthlyAiSpendLimit: value.monthlyAiSpendLimit || null };
    const editing = this.editing();
    const call = editing ? this.plans.update(editing.planId, request) : this.plans.create(request);
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
