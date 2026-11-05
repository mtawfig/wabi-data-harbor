import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/i18n/language.service';
import { OrganizationDetail } from '../../core/organizations/organizations.models';
import { OrganizationsService } from '../../core/organizations/organizations.service';
import { SupportArea, TICKET_IMPACTS, TICKET_STATUSES, TICKET_TYPES, TicketImpact, TicketStatus, TicketSummary, TicketType } from '../../core/support/support.models';
import { SupportService } from '../../core/support/support.service';
import { TicketImpactComponent, TicketStatusComponent, TicketTypeComponent } from './ticket-chips.component';

type OpenFilter = 'open' | 'closed' | 'all';
type AssigneeFilter = 'any' | 'me' | 'unassigned';

/** Ticket list for the organization area (My Tickets / organization tickets) and the platform queue. */
@Component({
  selector: 'app-ticket-list',
  imports: [DatePipe, FormsModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatTableModule, RouterLink, TranslocoModule, TicketStatusComponent, TicketImpactComponent, TicketTypeComponent],
  templateUrl: './ticket-list.component.html',
  styleUrl: './support.scss',
})
export class TicketListComponent {
  private readonly support = inject(SupportService);
  private readonly organizations = inject(OrganizationsService);
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly language = inject(LanguageService);

  readonly area = input<SupportArea>('app');
  readonly isPlatform = computed(() => this.area() === 'platform');
  readonly columns = computed(() => (this.isPlatform() ? ['reference', 'subject', 'organization', 'type', 'impact', 'status', 'assigned', 'lastActivity'] : ['reference', 'subject', 'type', 'impact', 'status', 'requester', 'lastActivity']));
  readonly types = TICKET_TYPES;
  readonly impacts = TICKET_IMPACTS;
  readonly statuses = TICKET_STATUSES;

  readonly items = signal<TicketSummary[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly loading = signal(false);
  readonly search = signal('');
  readonly openFilter = signal<OpenFilter>('open');
  readonly status = signal<TicketStatus | null>(null);
  readonly type = signal<TicketType | null>(null);
  readonly impact = signal<TicketImpact | null>(null);
  readonly organizationId = signal<string | null>(null);
  readonly assignee = signal<AssigneeFilter>('any');
  readonly organizationOptions = signal<OrganizationDetail[]>([]);
  readonly organizationNames = computed(() => new Map(this.organizationOptions().map((o) => [o.organizationId, this.language.current() === 'ar' ? o.organizationNameAr : o.organizationName])));

  constructor() {
    queueMicrotask(() => {
      if (this.isPlatform()) {
        this.organizations.list(1, 200).subscribe((r) => this.organizationOptions.set(r.items));
      }
      this.load();
    });
  }

  load(): void {
    this.loading.set(true);
    const open = this.openFilter() === 'all' ? null : this.openFilter() === 'open';
    this.support
      .list(this.area(), {
        page: this.page(),
        pageSize: this.pageSize(),
        search: this.search() || undefined,
        status: this.status(),
        open,
        ticketType: this.type(),
        impact: this.impact(),
        organizationId: this.isPlatform() ? this.organizationId() : null,
        assignedAdministratorId: this.isPlatform() && this.assignee() === 'me' ? this.auth.session()?.id : null,
        unassigned: this.isPlatform() && this.assignee() === 'unassigned' ? true : null,
        descending: true,
      })
      .subscribe({
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

  open(ticket: TicketSummary): void {
    void this.router.navigate([this.isPlatform() ? '/platform/support' : '/app/support', ticket.ticketId]);
  }

  organizationLabel(ticket: TicketSummary): string {
    return this.language.current() === 'ar' ? ticket.organizationNameAr : ticket.organizationName;
  }
}
