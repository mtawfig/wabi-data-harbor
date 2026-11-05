import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
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
import { OrganizationUser, Role } from '../../../core/organizations/admin.models';
import { OrganizationAdminService } from '../../../core/organizations/admin.service';
import { Application } from '../../../core/organizations/applications.models';
import { ApplicationsService } from '../../../core/organizations/applications.service';
import { EffectiveObjectAccess, EffectivePermissions, ObjectGrant, ROW_SCOPES, RoleMatrix, RowMapping, RowScope } from '../../../core/organizations/permissions.models';
import { PermissionsService } from '../../../core/organizations/permissions.service';
import { RoleMetricGrants } from '../../../core/platform/insights.models';
import { InsightsService } from '../../../core/platform/insights.service';
import { problemMessage } from '../../../core/problem-message';

interface GrantDraft {
  granted: boolean;
  allColumns: boolean;
  columnIds: Set<string>;
  rowScope: RowScope | null;
}

/** Permissions model (specification §10): per role and application, which objects, columns and rows may be queried; row-scope mappings; preview per user. */
@Component({
  selector: 'app-permissions',
  imports: [FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatTabsModule, TranslocoModule],
  templateUrl: './permissions.component.html',
  styleUrl: './permissions.component.scss',
})
export class PermissionsComponent {
  private readonly permissions = inject(PermissionsService);
  private readonly applications = inject(ApplicationsService);
  private readonly admin = inject(OrganizationAdminService);
  private readonly insights = inject(InsightsService);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly scopes = ROW_SCOPES;
  readonly apps = signal<Application[]>([]);
  readonly roles = signal<Role[]>([]);
  readonly users = signal<OrganizationUser[]>([]);
  readonly selectedApp = signal<string | null>(null);
  readonly selectedRole = signal<string | null>(null);
  readonly matrix = signal<RoleMatrix | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly search = signal('');
  readonly onlyApproved = signal(true);
  readonly editing = signal<ObjectGrant | null>(null);
  readonly draft = signal<GrantDraft>({ granted: false, allColumns: true, columnIds: new Set(), rowScope: null });
  readonly mapping = signal<RowMapping | null>(null);
  readonly mappingDraft = signal<{ employeeColumnId: string | null; departmentColumnId: string | null; sectionColumnId: string | null }>({ employeeColumnId: null, departmentColumnId: null, sectionColumnId: null });
  readonly previewUser = signal<string | null>(null);
  readonly preview = signal<EffectivePermissions | null>(null);
  readonly previewLoading = signal(false);
  readonly metricGrants = signal<RoleMetricGrants | null>(null);
  readonly metricDraft = signal<Set<string>>(new Set());
  readonly metricsLoading = signal(false);

  readonly selectable = computed(() => this.apps().filter((a) => !a.isBuiltIn));
  readonly editableRoles = computed(() => this.roles().filter((r) => r.isActive));
  readonly role = computed(() => this.roles().find((r) => r.roleId === this.selectedRole()) ?? null);
  readonly app = computed(() => this.apps().find((a) => a.applicationId === this.selectedApp()) ?? null);
  readonly isManagement = computed(() => this.matrix()?.isSystemManagement ?? false);
  readonly isOrganizationAdmin = computed(() => this.role()?.roleCode === 'OrganizationAdmin');
  readonly rows = computed(() => {
    const term = this.search().trim().toLowerCase();
    return (this.matrix()?.objects ?? []).filter((o) => (!this.onlyApproved() || o.isApproved) && (!term || `${o.schemaName}.${o.objectName} ${o.displayNameEn ?? ''} ${o.displayNameAr ?? ''}`.toLowerCase().includes(term)));
  });
  readonly grantedCount = computed(() => (this.matrix()?.objects ?? []).filter((o) => o.granted).length);
  readonly approvedColumns = computed(() => (this.editing()?.columns ?? []));
  readonly draftScopeUnmapped = computed(() => {
    const scope = this.draft().rowScope;
    const m = this.editing()?.mapping;
    if (!scope || !m || scope === 'All') {
      return false;
    }
    return scope === 'Department' ? !m.department : scope === 'Section' ? !m.section : !m.employee;
  });

  constructor() {
    queueMicrotask(() => {
      this.applications.list().subscribe((apps) => {
        this.apps.set(apps);
        const first = apps.find((a) => !a.isBuiltIn);
        if (first) {
          this.selectedApp.set(first.applicationId);
          this.load();
        }
      });
      this.admin.listRoles().subscribe((roles) => {
        this.roles.set(roles);
        const first = roles.find((r) => r.isActive && r.roleCode !== 'OrganizationAdmin') ?? roles[0];
        if (first) {
          this.selectedRole.set(first.roleId);
          this.load();
          this.loadMetricGrants();
        }
      });
      this.admin.listUsers(1, 200, '', 'Active', null, null).subscribe((page) => this.users.set(page.items));
    });
  }

  appName(a: Application): string {
    return this.language.current() === 'ar' ? a.applicationNameAr : a.applicationName;
  }

  roleName(r: Role): string {
    return this.language.current() === 'ar' ? r.nameAr : r.nameEn;
  }

  objectLabel(o: { displayNameEn?: string; displayNameAr?: string }): string {
    return (this.language.current() === 'ar' ? o.displayNameAr || o.displayNameEn : o.displayNameEn || o.displayNameAr) ?? '';
  }

  grantedColumns(o: ObjectGrant): number {
    return o.columns.filter((c) => c.granted).length;
  }

  scopeUnmapped(o: ObjectGrant): boolean {
    const scope = o.rowScope;
    if (!scope || scope === 'All') {
      return false;
    }
    return scope === 'Department' ? !o.mapping.department : scope === 'Section' ? !o.mapping.section : !o.mapping.employee;
  }

  mappingSummary(o: ObjectGrant): string {
    const parts: string[] = [];
    if (o.mapping.employee) {
      parts.push(this.transloco.translate('admin.permissions.scopes.Self'));
    }
    if (o.mapping.department) {
      parts.push(this.transloco.translate('admin.permissions.scopes.Department'));
    }
    if (o.mapping.section) {
      parts.push(this.transloco.translate('admin.permissions.scopes.Section'));
    }
    return parts.length ? parts.join(' · ') : '—';
  }

  columnNames(o: EffectiveObjectAccess): string {
    return o.columns.map((c) => c.columnName).join(', ');
  }

  selectApp(applicationId: string): void {
    this.selectedApp.set(applicationId);
    this.load();
  }

  selectRole(roleId: string): void {
    this.selectedRole.set(roleId);
    this.load();
    this.loadMetricGrants();
  }

  loadMetricGrants(): void {
    const roleId = this.selectedRole();
    if (!roleId) {
      this.metricGrants.set(null);
      return;
    }
    this.metricsLoading.set(true);
    this.insights.roleMetrics(roleId).subscribe({
      next: (g) => {
        this.metricGrants.set(g);
        this.metricDraft.set(new Set(g.grantedMetricIds));
        this.metricsLoading.set(false);
      },
      error: (e: HttpErrorResponse) => {
        this.error.set(problemMessage(e, this.transloco));
        this.metricsLoading.set(false);
      },
    });
  }

  toggleMetric(metricId: string, checked: boolean): void {
    this.metricDraft.update((d) => {
      const next = new Set(d);
      if (checked) {
        next.add(metricId);
      } else {
        next.delete(metricId);
      }
      return next;
    });
  }

  saveMetricGrants(): void {
    const roleId = this.selectedRole();
    if (!roleId) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.insights.setRoleMetrics(roleId, [...this.metricDraft()]).subscribe({
      next: (g) => {
        this.metricGrants.set(g);
        this.metricDraft.set(new Set(g.grantedMetricIds));
        this.busy.set(false);
        this.snack.open(this.transloco.translate('admin.permissions.metricsSaved'), undefined, { duration: 2500 });
      },
      error: (e: HttpErrorResponse) => {
        this.error.set(problemMessage(e, this.transloco));
        this.busy.set(false);
      },
    });
  }

  metricName(m: { displayNameEn: string; displayNameAr: string }): string {
    return this.language.current() === 'ar' ? m.displayNameAr : m.displayNameEn;
  }

  load(): void {
    const roleId = this.selectedRole();
    const applicationId = this.selectedApp();
    this.editing.set(null);
    this.mapping.set(null);
    if (!roleId || !applicationId) {
      this.matrix.set(null);
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    this.permissions.matrix(roleId, applicationId).subscribe({
      next: (m) => {
        this.matrix.set(m);
        this.loading.set(false);
      },
      error: (e: HttpErrorResponse) => {
        this.error.set(problemMessage(e, this.transloco));
        this.loading.set(false);
      },
    });
  }

  edit(o: ObjectGrant): void {
    this.editing.set(o);
    this.draft.set({ granted: o.granted, allColumns: o.granted ? o.allColumns : true, columnIds: new Set(o.columns.filter((c) => c.granted).map((c) => c.columnId)), rowScope: o.rowScope ?? null });
    this.mapping.set(null);
    this.permissions.mapping(o.objectId).subscribe((m) => {
      this.mapping.set(m);
      this.mappingDraft.set({ employeeColumnId: m.employeeColumnId ?? null, departmentColumnId: m.departmentColumnId ?? null, sectionColumnId: m.sectionColumnId ?? null });
    });
  }

  close(): void {
    this.editing.set(null);
  }

  toggleColumn(columnId: string, checked: boolean): void {
    this.draft.update((d) => {
      const columnIds = new Set(d.columnIds);
      if (checked) {
        columnIds.add(columnId);
      } else {
        columnIds.delete(columnId);
      }
      return { ...d, columnIds };
    });
  }

  patch(change: Partial<GrantDraft>): void {
    this.draft.update((d) => ({ ...d, ...change }));
  }

  saveGrant(): void {
    const o = this.editing();
    const roleId = this.selectedRole();
    if (!o || !roleId) {
      return;
    }
    const d = this.draft();
    this.busy.set(true);
    this.error.set(null);
    this.permissions.setGrant(roleId, o.objectId, { granted: d.granted, allColumns: d.allColumns, columnIds: d.allColumns ? [] : [...d.columnIds], rowScope: d.granted ? d.rowScope : null }).subscribe({
      next: (updated) => {
        this.matrix.update((m) => (m ? { ...m, objects: m.objects.map((x) => (x.objectId === updated.objectId ? updated : x)) } : m));
        this.editing.set(updated);
        this.busy.set(false);
        this.snack.open(this.transloco.translate('admin.permissions.saved'), undefined, { duration: 2500 });
        this.refreshPreview();
      },
      error: (e: HttpErrorResponse) => {
        this.error.set(problemMessage(e, this.transloco));
        this.busy.set(false);
      },
    });
  }

  saveMapping(): void {
    const o = this.editing();
    if (!o) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.permissions.setMapping(o.objectId, this.mappingDraft()).subscribe({
      next: (m) => {
        this.mapping.set(m);
        const availability = { employee: !!m.employeeColumnId, department: !!m.departmentColumnId, section: !!m.sectionColumnId };
        this.matrix.update((x) => (x ? { ...x, objects: x.objects.map((y) => (y.objectId === m.objectId ? { ...y, mapping: availability } : y)) } : x));
        this.editing.update((y) => (y ? { ...y, mapping: availability } : y));
        this.busy.set(false);
        this.snack.open(this.transloco.translate('admin.permissions.mappingSaved'), undefined, { duration: 2500 });
        this.refreshPreview();
      },
      error: (e: HttpErrorResponse) => {
        this.error.set(problemMessage(e, this.transloco));
        this.busy.set(false);
      },
    });
  }

  selectPreviewUser(userId: string | null): void {
    this.previewUser.set(userId);
    this.refreshPreview();
  }

  refreshPreview(): void {
    const userId = this.previewUser();
    if (!userId) {
      this.preview.set(null);
      return;
    }
    this.previewLoading.set(true);
    this.permissions.effective(userId).subscribe({
      next: (p) => {
        this.preview.set(p);
        this.previewLoading.set(false);
      },
      error: (e: HttpErrorResponse) => {
        this.error.set(problemMessage(e, this.transloco));
        this.previewLoading.set(false);
      },
    });
  }
}
