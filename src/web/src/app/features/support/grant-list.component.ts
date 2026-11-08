import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { Router } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { LanguageService } from '../../core/i18n/language.service';
import { GRANT_STATUSES, GrantStatus, SupportAccessGrant } from '../../core/support/support-access.models';
import { SupportAccessService } from '../../core/support/support-access.service';
import { SupportArea } from '../../core/support/support.models';
import { GrantStatusComponent } from './grant-status.component';
import { WiDatePipe } from '../../shared/format/locale-pipes';

/** Platform Support Access page and the organization administrator's approval list: the same table, different area. */
@Component({
  selector: 'app-grant-list',
  imports: [FormsModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatTableModule, TranslocoModule, GrantStatusComponent, WiDatePipe],
  templateUrl: './grant-list.component.html',
  styleUrl: './support.scss',
})
export class GrantListComponent {
  private readonly access = inject(SupportAccessService);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly area = input<SupportArea>('app');
  readonly isPlatform = computed(() => this.area() === 'platform');
  readonly statuses = GRANT_STATUSES;
  readonly columns = computed(() => (this.isPlatform() ? ['ticket', 'organization', 'operations', 'status', 'assigned', 'expires', 'created'] : ['ticket', 'operations', 'status', 'proposedBy', 'expires', 'created']));

  readonly items = signal<SupportAccessGrant[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly loading = signal(false);
  readonly filter = signal<'pending' | 'all'>('pending');
  readonly status = signal<GrantStatus | null>(null);

  constructor() {
    queueMicrotask(() => this.load());
  }

  load(): void {
    this.loading.set(true);
    this.access.list(this.area(), this.page(), this.pageSize(), this.filter() === 'all' ? this.status() : null, this.filter() === 'pending').subscribe({
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

  open(grant: SupportAccessGrant): void {
    void this.router.navigate([this.isPlatform() ? '/platform/support' : '/app/support', grant.ticketId], { queryParams: { tab: 'access' } });
  }

  organizationName(grant: SupportAccessGrant): string {
    return this.language.current() === 'ar' ? grant.organizationNameAr : grant.organizationName;
  }

  operations(grant: SupportAccessGrant): string {
    return grant.operations.map((o) => this.transloco.translate(`supportAccess.operations.${o.operation}`)).join(' · ');
  }
}
