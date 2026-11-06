import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { problemMessage } from '../../../core/problem-message';
import { SharedAnalysis } from '../../../core/sharing/sharing.models';
import { SharingService } from '../../../core/sharing/sharing.service';

/** Shared With Me (specification §24): analyses colleagues shared with the signed-in user; opening one shows the stored answer read-only. */
@Component({
  selector: 'app-shared-with-me',
  imports: [DatePipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule, MatProgressBarModule, MatTableModule, TranslocoModule],
  templateUrl: './shared-with-me.component.html',
  styleUrl: './sharing-list.scss',
})
export class SharedWithMeComponent {
  private readonly sharing = inject(SharingService);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly items = signal<SharedAnalysis[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly search = signal('');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly columns = ['title', 'owner', 'application', 'shared', 'expires', 'open'];

  constructor() {
    queueMicrotask(() => this.load());
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.sharing.withMe(this.page(), this.pageSize(), this.search()).subscribe({
      next: (r) => {
        this.items.set(r.items);
        this.total.set(r.totalCount);
        this.loading.set(false);
      },
      error: (e: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(problemMessage(e, this.transloco));
      },
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

  open(item: SharedAnalysis): void {
    void this.router.navigate(['/app/shared-with-me', item.historyId]);
  }
}
