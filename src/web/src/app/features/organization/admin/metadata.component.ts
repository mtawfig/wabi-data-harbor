import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { Application, CLASSIFICATIONS, ColumnInfo, ColumnSemanticRole, DataClassification, ObjectDetail, ObjectSummary, SEMANTIC_ROLES } from '../../../core/organizations/applications.models';
import { ApplicationsService } from '../../../core/organizations/applications.service';
import { problemMessage } from '../../../core/problem-message';
import { WiDatePipe, WiNumberPipe } from '../../../shared/format/locale-pipes';

type ApprovalFilter = 'all' | 'approved' | 'pending';

interface ColumnDraft {
  displayNameEn: string;
  displayNameAr: string;
  businessDescription: string;
  classification: DataClassification;
  isApproved: boolean;
  semanticRole: ColumnSemanticRole;
}

/** Metadata catalog (specification §17): discovered objects and columns, EN/AR names, descriptions, classification and approval. */
@Component({
  selector: 'app-metadata',
  imports: [FormsModule, MatButtonModule, MatButtonToggleModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatTableModule, TranslocoModule, WiDatePipe, WiNumberPipe],
  templateUrl: './metadata.component.html',
  styleUrl: './metadata.component.scss',
})
export class MetadataComponent {
  private readonly applications = inject(ApplicationsService);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  /** Preselected application id from the Applications page (?application=…). */
  readonly application = input<string | undefined>(undefined);

  readonly classifications = CLASSIFICATIONS;
  readonly semanticRoles = SEMANTIC_ROLES;
  readonly objectColumns = ['object', 'type', 'names', 'columns', 'status'];
  readonly apps = signal<Application[]>([]);
  readonly selected = signal<string | null>(null);
  readonly items = signal<ObjectSummary[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(50);
  readonly search = signal('');
  readonly filter = signal<ApprovalFilter>('all');
  readonly includeRemoved = signal(false);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly detail = signal<ObjectDetail | null>(null);
  readonly objectDraft = signal({ displayNameEn: '', displayNameAr: '', businessDescription: '', isApproved: false });
  readonly editingColumn = signal<string | null>(null);
  readonly columnDraft = signal<ColumnDraft>({ displayNameEn: '', displayNameAr: '', businessDescription: '', classification: 'Internal', isApproved: false, semanticRole: 'Unspecified' });

  readonly selectedApp = computed(() => this.apps().find((a) => a.applicationId === this.selected()) ?? null);
  readonly selectable = computed(() => this.apps().filter((a) => !a.isBuiltIn));

  constructor() {
    queueMicrotask(() => {
      this.applications.list().subscribe((apps) => {
        this.apps.set(apps);
        const candidates = apps.filter((a) => !a.isBuiltIn);
        const preselected = this.application();
        const first = (preselected && candidates.find((a) => a.applicationId === preselected)) || candidates[0];
        if (first) {
          this.select(first.applicationId);
        }
      });
    });
  }

  name(a: Application): string {
    return this.language.current() === 'ar' ? a.applicationNameAr : a.applicationName;
  }

  select(applicationId: string | null): void {
    this.selected.set(applicationId);
    this.detail.set(null);
    this.page.set(1);
    this.load();
  }

  load(): void {
    const id = this.selected();
    if (!id) {
      this.items.set([]);
      return;
    }
    this.loading.set(true);
    const approved = this.filter() === 'all' ? null : this.filter() === 'approved';
    this.applications.objects(id, this.page(), this.pageSize(), this.search() || undefined, approved, this.includeRemoved()).subscribe({
      next: (r) => {
        this.items.set(r.items);
        this.total.set(r.totalCount);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  apply(): void {
    this.page.set(1);
    this.load();
  }

  onPage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  refresh(): void {
    const id = this.selected();
    if (!id || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.applications.refreshMetadata(id).subscribe({
      next: (r) => {
        this.snack.open(this.transloco.translate('admin.metadata.refreshed', r), undefined, { duration: 8000 });
        this.busy.set(false);
        this.applications.list().subscribe((apps) => this.apps.set(apps));
        this.load();
        if (this.detail()) {
          this.open(this.detail()!.summary.objectId);
        }
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  open(objectId: string): void {
    this.applications.object(objectId).subscribe((d) => {
      this.detail.set(d);
      this.objectDraft.set({ displayNameEn: d.summary.displayNameEn ?? '', displayNameAr: d.summary.displayNameAr ?? '', businessDescription: d.businessDescription ?? '', isApproved: d.summary.isApproved });
      this.editingColumn.set(null);
    });
  }

  close(): void {
    this.detail.set(null);
  }

  setObjectField(name: 'displayNameEn' | 'displayNameAr' | 'businessDescription', value: string): void {
    this.objectDraft.update((d) => ({ ...d, [name]: value }));
  }

  saveObject(): void {
    const d = this.detail();
    if (!d || this.busy()) {
      return;
    }
    const v = this.objectDraft();
    this.busy.set(true);
    this.error.set(null);
    this.applications.updateObject(d.summary.objectId, { displayNameEn: v.displayNameEn || undefined, displayNameAr: v.displayNameAr || undefined, businessDescription: v.businessDescription || undefined, isApproved: v.isApproved }).subscribe({
      next: (updated) => {
        this.detail.set(updated);
        this.snack.open(this.transloco.translate('admin.metadata.saved'), undefined, { duration: 3000 });
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  approveAll(approve: boolean): void {
    const d = this.detail();
    if (!d || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.applications.approveObject(d.summary.objectId, approve).subscribe({
      next: (updated) => {
        this.detail.set(updated);
        this.objectDraft.update((x) => ({ ...x, isApproved: approve }));
        this.snack.open(this.transloco.translate(approve ? 'admin.metadata.approvedAll' : 'admin.metadata.unapprovedAll'), undefined, { duration: 3000 });
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  editColumn(c: ColumnInfo): void {
    this.editingColumn.set(c.columnId);
    this.columnDraft.set({ displayNameEn: c.displayNameEn ?? '', displayNameAr: c.displayNameAr ?? '', businessDescription: c.businessDescription ?? '', classification: c.classification, isApproved: c.isApproved, semanticRole: c.semanticRole ?? 'Unspecified' });
  }

  setColumnField<K extends keyof ColumnDraft>(name: K, value: ColumnDraft[K]): void {
    this.columnDraft.update((d) => ({ ...d, [name]: value }));
  }

  saveColumn(c: ColumnInfo): void {
    if (this.busy()) {
      return;
    }
    const v = this.columnDraft();
    this.busy.set(true);
    this.error.set(null);
    this.applications.updateColumn(c.columnId, { displayNameEn: v.displayNameEn || undefined, displayNameAr: v.displayNameAr || undefined, businessDescription: v.businessDescription || undefined, classification: v.classification, isApproved: v.isApproved, semanticRole: v.semanticRole }).subscribe({
      next: (updated) => {
        this.detail.update((d) => (d ? { ...d, columns: d.columns.map((x) => (x.columnId === updated.columnId ? updated : x)) } : d));
        this.editingColumn.set(null);
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  toggleColumnApproval(c: ColumnInfo, approve: boolean): void {
    this.busy.set(true);
    this.applications.updateColumn(c.columnId, { displayNameEn: c.displayNameEn, displayNameAr: c.displayNameAr, businessDescription: c.businessDescription, classification: c.classification, isApproved: approve }).subscribe({
      next: (updated) => {
        this.detail.update((d) => (d ? { ...d, columns: d.columns.map((x) => (x.columnId === updated.columnId ? updated : x)) } : d));
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  /** Confirms the suggested role in one click (the role picker stays available in the column form). */
  acceptSuggestedRole(c: ColumnInfo): void {
    if (!c.suggestedSemanticRole || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.applications.updateColumn(c.columnId, { displayNameEn: c.displayNameEn, displayNameAr: c.displayNameAr, businessDescription: c.businessDescription, classification: c.classification, isApproved: c.isApproved, semanticRole: c.suggestedSemanticRole }).subscribe({
      next: (updated) => {
        this.detail.update((d) => (d ? { ...d, columns: d.columns.map((x) => (x.columnId === updated.columnId ? updated : x)) } : d));
        this.busy.set(false);
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  typeLabel(c: ColumnInfo): string {
    if (c.maxLength) {
      return `${c.dataType}(${c.maxLength})`;
    }
    if (c.precision && c.dataType !== 'int' && c.dataType !== 'bigint') {
      return `${c.dataType}(${c.precision}${c.scale ? ',' + c.scale : ''})`;
    }
    return c.dataType;
  }

  private fail(response: HttpErrorResponse): void {
    this.error.set(problemMessage(response, this.transloco));
    this.busy.set(false);
  }
}
