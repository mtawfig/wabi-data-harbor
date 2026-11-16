import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { Router } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { LanguageService } from '../../../../core/i18n/language.service';
import { VerifiedAnswerCounts, VerifiedAnswerListItem } from '../../../../core/verified-answers/verified-answers.models';
import { VerifiedAnswersService } from '../../../../core/verified-answers/verified-answers.service';
import { WiDatePipe, WiNumberPipe } from '../../../../shared/format/locale-pipes';

/** The filters across the top: what waits for a review first, then each status, then everything. */
const FILTERS = ['Waiting', 'Approved', 'Rejected', 'Disabled', 'SetAside', 'All'] as const;
type Filter = (typeof FILTERS)[number];

/**
 * Verified answers (P13.53) for Organization Administrators: answers people marked correct, waiting for approval, and
 * answers reported not correct. Only approved answers are used as examples for similar questions.
 */
@Component({
  selector: 'app-verified-answers-list',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatTableModule, TranslocoModule, WiDatePipe, WiNumberPipe],
  templateUrl: './verified-answers-list.component.html',
  styleUrl: './verified-answers.scss',
})
export class VerifiedAnswersListComponent {
  private readonly answers = inject(VerifiedAnswersService);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);

  readonly filters = FILTERS;
  readonly columns = ['question', 'askedBy', 'verdict', 'status', 'feedbackAt'];
  readonly items = signal<VerifiedAnswerListItem[]>([]);
  readonly counts = signal<VerifiedAnswerCounts | null>(null);
  readonly filter = signal<Filter>('Waiting');
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly loading = signal(false);

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    const filter = this.filter();
    this.answers.list(this.page(), this.pageSize(), filter === 'All' ? null : filter, null).subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.total.set(result.totalCount);
        this.loading.set(false);
        // Counts after the list: listing sets aside answers whose tables or columns changed.
        this.answers.counts().subscribe({ next: (counts) => this.counts.set(counts) });
      },
      error: () => this.loading.set(false),
    });
  }

  count(filter: Filter): number | null {
    const c = this.counts();
    if (!c) {
      return null;
    }
    switch (filter) {
      case 'Waiting':
        return c.waiting;
      case 'Approved':
        return c.approved;
      case 'Rejected':
        return c.rejected;
      case 'Disabled':
        return c.disabled;
      case 'SetAside':
        return c.setAside;
      default:
        return null;
    }
  }

  select(filter: Filter): void {
    this.filter.set(filter);
    this.page.set(1);
    this.load();
  }

  onPage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  application(item: VerifiedAnswerListItem): string {
    return this.language.current() === 'ar' ? item.applicationNameAr || item.applicationName : item.applicationName;
  }

  open(item: VerifiedAnswerListItem): void {
    void this.router.navigate(['/app/admin/verified-answers', item.verifiedAnswerId]);
  }
}
