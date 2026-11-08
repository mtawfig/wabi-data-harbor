import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { OrganizationDetail, OrganizationStatus } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { OrganizationStatusChipComponent } from './organization-status-chip.component';
import { WiDatePipe, WiNumberPipe } from '../../../shared/format/locale-pipes';

@Component({
  selector: 'app-organizations-list',
  imports: [FormsModule, MatButtonModule, MatChipsModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatTableModule, RouterLink, TranslocoModule, OrganizationStatusChipComponent, WiDatePipe, WiNumberPipe],
  templateUrl: './organizations-list.component.html',
  styles: `
    .filters { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; margin-bottom: 12px; }
    .filters mat-form-field { min-width: 220px; }
    table { width: 100%; }
    tr.row { cursor: pointer; }
    tr.row:hover { background: #f1f5f9; }
    .num { text-align: end; }
    .empty { padding: 32px; text-align: center; }
  `,
})
export class OrganizationsListComponent {
  private readonly organizations = inject(OrganizationsService);
  private readonly router = inject(Router);

  readonly columns = ['code', 'name', 'status', 'users', 'applications', 'administrator', 'created'];
  readonly items = signal<OrganizationDetail[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly search = signal('');
  readonly status = signal<OrganizationStatus | null>(null);
  readonly loading = signal(false);

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.organizations.list(this.page(), this.pageSize(), this.search() || undefined, this.status()).subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.total.set(result.totalCount);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  onPage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  applyFilters(): void {
    this.page.set(1);
    this.load();
  }

  open(organization: OrganizationDetail): void {
    void this.router.navigate(['/platform/organizations', organization.organizationId]);
  }
}
