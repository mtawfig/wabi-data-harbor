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
import { Router } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { countryName, countryOptions } from '../../../core/demo/countries';
import { DEMO_STATUSES, DemoRequestListItem, DemoRequestSummary } from '../../../core/demo/demo-requests.models';
import { DemoRequestsService } from '../../../core/demo/demo-requests.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { WiDatePipe, WiNumberPipe } from '../../../shared/format/locale-pipes';

/** Demo requests from the landing page (P13.52), newest first, for the Platform Administrators who follow them up. */
@Component({
  selector: 'app-demo-requests-list',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatTableModule, TranslocoModule, WiDatePipe, WiNumberPipe],
  templateUrl: './demo-requests-list.component.html',
  styleUrl: './demo-requests.scss',
})
export class DemoRequestsListComponent {
  private readonly demoRequests = inject(DemoRequestsService);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);

  readonly statuses = DEMO_STATUSES;
  readonly columns = ['received', 'person', 'organization', 'country', 'status', 'scheduled', 'assigned'];
  readonly items = signal<DemoRequestListItem[]>([]);
  readonly summary = signal<DemoRequestSummary | null>(null);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly search = signal('');
  readonly status = signal<string | null>(null);
  readonly country = signal<string | null>(null);
  readonly from = signal('');
  readonly to = signal('');
  readonly loading = signal(false);
  readonly countries = computed(() => countryOptions(this.language.current()));

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.demoRequests.summary().subscribe({ next: (summary) => this.summary.set(summary) });
    this.demoRequests
      .list(this.page(), this.pageSize(), {
        status: this.status(),
        country: this.country(),
        search: this.search().trim() || null,
        from: this.from() ? new Date(`${this.from()}T00:00:00`).toISOString() : null,
        // "To" is inclusive on the page: the API takes the start of the next day.
        to: this.to() ? new Date(new Date(`${this.to()}T00:00:00`).getTime() + 24 * 60 * 60 * 1000).toISOString() : null,
      })
      .subscribe({
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

  countryLabel(code: string): string {
    return countryName(code, this.language.current());
  }

  open(request: DemoRequestListItem): void {
    void this.router.navigate(['/platform/demo-requests', request.demoRequestId]);
  }
}
