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
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { FILTER_OPERATORS, FilterOperator, InsightsCatalog, InsightsColumn, InsightsMetric, InsightsObject, METRIC_AGGREGATES, MetricFilter, SaveInsightsMetricRequest, SqlPreview } from '../../../core/platform/insights.models';
import { InsightsService } from '../../../core/platform/insights.service';
import { problemMessage } from '../../../core/problem-message';
import { WiDatePipe } from '../../../shared/format/locale-pipes';

interface FilterDraft {
  column: string;
  operator: FilterOperator;
  values: string;
}

/** Platform Insights Configuration (specification §12): read-only source status, the global view/column catalog and metric definitions. */
@Component({
  selector: 'app-insights-config',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatTabsModule, TranslocoModule, WiDatePipe],
  templateUrl: './insights-config.component.html',
  styleUrl: './insights-config.component.scss',
})
export class InsightsConfigComponent {
  private readonly insights = inject(InsightsService);
  private readonly fb = inject(FormBuilder);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly aggregates = METRIC_AGGREGATES;
  readonly operators = FILTER_OPERATORS;
  readonly catalog = signal<InsightsCatalog | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly selectedObject = signal<InsightsObject | null>(null);
  readonly editingColumn = signal<string | null>(null);
  readonly columnDraft = signal({ displayNameEn: '', displayNameAr: '', description: '', isApproved: false });
  readonly objectDraft = signal({ displayNameEn: '', displayNameAr: '', description: '', isApproved: false, sortOrder: 100 });
  readonly showMetricForm = signal(false);
  readonly editingMetric = signal<InsightsMetric | null>(null);
  readonly filters = signal<FilterDraft[]>([]);
  readonly preview = signal<SqlPreview | null>(null);

  readonly metricForm = this.fb.nonNullable.group({
    metricCode: ['', [Validators.required, Validators.pattern(/^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+){0,3}$/)]],
    objectId: ['', Validators.required],
    displayNameEn: ['', [Validators.required, Validators.maxLength(200)]],
    displayNameAr: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    aggregate: ['Count' as SaveInsightsMetricRequest['aggregate'], Validators.required],
    columnName: [''],
    dateColumn: [''],
    unit: ['count'],
    isApproved: [false],
    sortOrder: [100, [Validators.min(0), Validators.max(10000)]],
  });

  readonly objects = computed(() => this.catalog()?.objects ?? []);
  readonly metrics = computed(() => this.catalog()?.metrics ?? []);
  readonly source = computed(() => this.catalog()?.source ?? null);
  readonly formObject = signal<InsightsObject | null>(null);
  readonly formColumns = computed(() => (this.formObject()?.columns ?? []).filter((c) => c.isPresent));

  constructor() {
    this.metricForm.controls.objectId.valueChanges.subscribe((id) => this.formObject.set(this.objects().find((o) => o.objectId === id) ?? null));
    this.load();
  }

  name(item: { displayNameEn: string; displayNameAr: string }): string {
    return this.language.current() === 'ar' ? item.displayNameAr : item.displayNameEn;
  }

  load(): void {
    this.busy.set(true);
    this.insights.catalog().subscribe({
      next: (c) => {
        this.catalog.set(c);
        const current = this.selectedObject();
        this.selectedObject.set(current ? (c.objects.find((o) => o.objectId === current.objectId) ?? null) : null);
        this.busy.set(false);
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  sync(): void {
    this.busy.set(true);
    this.error.set(null);
    this.insights.sync().subscribe({
      next: (r) => {
        this.snack.open(this.transloco.translate('platformInsights.synced', r), undefined, { duration: 5000 });
        this.load();
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  openObject(o: InsightsObject): void {
    this.selectedObject.set(o);
    this.objectDraft.set({ displayNameEn: o.displayNameEn, displayNameAr: o.displayNameAr, description: o.description ?? '', isApproved: o.isApproved, sortOrder: o.sortOrder });
    this.editingColumn.set(null);
  }

  saveObject(): void {
    const o = this.selectedObject();
    if (!o) {
      return;
    }
    const d = this.objectDraft();
    this.busy.set(true);
    this.insights.updateObject(o.objectId, { ...d, description: d.description || null }).subscribe({
      next: (updated) => {
        this.replaceObject(updated);
        this.busy.set(false);
        this.snack.open(this.transloco.translate('platformInsights.saved'), undefined, { duration: 2500 });
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  editColumn(c: InsightsColumn): void {
    this.editingColumn.set(c.columnId);
    this.columnDraft.set({ displayNameEn: c.displayNameEn, displayNameAr: c.displayNameAr, description: c.description ?? '', isApproved: c.isApproved });
  }

  saveColumn(c: InsightsColumn): void {
    const d = this.columnDraft();
    this.busy.set(true);
    this.insights.updateColumn(c.columnId, { ...d, description: d.description || null }).subscribe({
      next: (updated) => {
        const o = this.selectedObject();
        if (o) {
          this.replaceObject({ ...o, columns: o.columns.map((x) => (x.columnId === updated.columnId ? updated : x)) });
        }
        this.editingColumn.set(null);
        this.busy.set(false);
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  toggleColumnApproval(c: InsightsColumn, isApproved: boolean): void {
    this.busy.set(true);
    this.insights.updateColumn(c.columnId, { displayNameEn: c.displayNameEn, displayNameAr: c.displayNameAr, description: c.description ?? null, isApproved }).subscribe({
      next: (updated) => {
        const o = this.selectedObject();
        if (o) {
          this.replaceObject({ ...o, columns: o.columns.map((x) => (x.columnId === updated.columnId ? updated : x)) });
        }
        this.busy.set(false);
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  startCreateMetric(): void {
    this.editingMetric.set(null);
    const first = this.objects().find((o) => o.isPresent);
    this.metricForm.reset({ metricCode: '', objectId: first?.objectId ?? '', displayNameEn: '', displayNameAr: '', description: '', aggregate: 'Count', columnName: '', dateColumn: '', unit: 'count', isApproved: false, sortOrder: 100 });
    this.filters.set([]);
    this.preview.set(null);
    this.showMetricForm.set(true);
    this.error.set(null);
  }

  startEditMetric(m: InsightsMetric): void {
    this.editingMetric.set(m);
    this.metricForm.reset({ metricCode: m.metricCode, objectId: m.objectId, displayNameEn: m.displayNameEn, displayNameAr: m.displayNameAr, description: m.description ?? '', aggregate: m.aggregate, columnName: m.columnName ?? '', dateColumn: m.dateColumn ?? '', unit: m.unit, isApproved: m.isApproved, sortOrder: m.sortOrder });
    this.filters.set(m.filters.map((f) => ({ column: f.column, operator: f.operator, values: f.values.join(', ') })));
    this.preview.set(null);
    this.showMetricForm.set(true);
    this.error.set(null);
  }

  addFilter(): void {
    this.filters.update((f) => [...f, { column: this.formColumns()[0]?.columnName ?? '', operator: 'eq', values: '' }]);
  }

  updateFilter(index: number, change: Partial<FilterDraft>): void {
    this.filters.update((f) => f.map((x, i) => (i === index ? { ...x, ...change } : x)));
  }

  removeFilter(index: number): void {
    this.filters.update((f) => f.filter((_, i) => i !== index));
  }

  cancelMetric(): void {
    this.showMetricForm.set(false);
  }

  private metricBody(): SaveInsightsMetricRequest {
    const v = this.metricForm.getRawValue();
    const filters: MetricFilter[] = this.filters().filter((f) => f.column).map((f) => ({ column: f.column, operator: f.operator, values: f.values.split(',').map((x) => x.trim()).filter((x) => x.length > 0) }));
    return { metricCode: v.metricCode.trim(), objectId: v.objectId, displayNameEn: v.displayNameEn, displayNameAr: v.displayNameAr, description: v.description || null, aggregate: v.aggregate, columnName: v.columnName || null, dateColumn: v.dateColumn || null, filters, unit: v.unit || 'count', isApproved: v.isApproved, sortOrder: v.sortOrder };
  }

  submitMetric(): void {
    if (this.metricForm.invalid || this.busy()) {
      this.metricForm.markAllAsTouched();
      return;
    }
    const body = this.metricBody();
    const editing = this.editingMetric();
    this.busy.set(true);
    this.error.set(null);
    (editing ? this.insights.updateMetric(editing.metricId, body) : this.insights.createMetric(body)).subscribe({
      next: () => {
        this.showMetricForm.set(false);
        this.snack.open(this.transloco.translate('platformInsights.saved'), undefined, { duration: 2500 });
        this.load();
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  previewSql(): void {
    const editing = this.editingMetric();
    if (!editing) {
      return;
    }
    const v = this.metricForm.getRawValue();
    this.insights.previewSql(editing.metricId, { bucket: v.dateColumn ? 'Month' : null }).subscribe({
      next: (p) => this.preview.set(p),
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  deleteMetric(m: InsightsMetric): void {
    this.busy.set(true);
    this.insights.deleteMetric(m.metricId).subscribe({ next: () => this.load(), error: (e: HttpErrorResponse) => this.fail(e) });
  }

  toggleMetricApproval(m: InsightsMetric, isApproved: boolean): void {
    this.busy.set(true);
    this.insights.updateMetric(m.metricId, { metricCode: m.metricCode, objectId: m.objectId, displayNameEn: m.displayNameEn, displayNameAr: m.displayNameAr, description: m.description ?? null, aggregate: m.aggregate, columnName: m.columnName ?? null, dateColumn: m.dateColumn ?? null, filters: m.filters, unit: m.unit, isApproved, sortOrder: m.sortOrder }).subscribe({
      next: () => this.load(),
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  approvedCount(o: InsightsObject): number {
    return o.columns.filter((c) => c.isApproved && c.isPresent).length;
  }

  filterSummary(m: InsightsMetric): string {
    return m.filters.map((f) => `${f.column} ${f.operator} ${f.values.join('|')}`).join('; ');
  }

  private replaceObject(updated: InsightsObject): void {
    this.catalog.update((c) => (c ? { ...c, objects: c.objects.map((o) => (o.objectId === updated.objectId ? updated : o)) } : c));
    this.selectedObject.set(updated);
  }

  private fail(e: HttpErrorResponse): void {
    this.error.set(problemMessage(e, this.transloco));
    this.busy.set(false);
  }
}
