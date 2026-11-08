import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { Department, OrganizationUser, OrganizationUserStatus, Role } from '../../../core/organizations/admin.models';
import { OrganizationAdminService } from '../../../core/organizations/admin.service';
import { WiDatePipe } from '../../../shared/format/locale-pipes';

@Component({
  selector: 'app-users-list',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatTableModule, RouterLink, TranslocoModule, WiDatePipe],
  templateUrl: './users-list.component.html',
  styleUrl: './admin-catalog.scss',
})
export class UsersListComponent {
  private readonly admin = inject(OrganizationAdminService);
  private readonly router = inject(Router);
  readonly language = inject(LanguageService);

  readonly columns = ['name', 'email', 'department', 'roles', 'status', 'lastLogin'];
  readonly items = signal<OrganizationUser[]>([]);
  readonly departments = signal<Department[]>([]);
  readonly roles = signal<Role[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly search = signal('');
  readonly status = signal<OrganizationUserStatus | null>(null);
  readonly departmentId = signal<string | null>(null);
  readonly roleCode = signal<string | null>(null);
  readonly loading = signal(false);
  readonly departmentNames = computed(() => new Map(this.departments().map((d) => [d.departmentId, this.language.current() === 'ar' ? d.departmentNameAr : d.departmentNameEn])));
  readonly roleNames = computed(() => new Map(this.roles().map((r) => [r.roleCode, this.language.current() === 'ar' ? r.nameAr : r.nameEn])));

  constructor() {
    this.admin.listDepartments().subscribe((d) => this.departments.set(d));
    this.admin.listRoles().subscribe((r) => this.roles.set(r));
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.admin.listUsers(this.page(), this.pageSize(), this.search() || undefined, this.status(), this.departmentId(), this.roleCode()).subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.total.set(result.totalCount);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  applyFilters(): void {
    this.page.set(1);
    this.load();
  }

  onPage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  open(user: OrganizationUser): void {
    void this.router.navigate(['/app/admin/users', user.userId]);
  }

  roleLabel(code: string): string {
    return this.roleNames().get(code) ?? code;
  }
}
