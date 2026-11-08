import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { problemMessage } from '../../../core/problem-message';
import { Share } from '../../../core/sharing/sharing.models';
import { SharingService } from '../../../core/sharing/sharing.service';
import { WiDatePipe } from '../../../shared/format/locale-pipes';

/** Shared By Me (specification §24): every share the signed-in user granted, with status, expiry, last view and revocation. */
@Component({
  selector: 'app-shared-by-me',
  imports: [FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule, MatProgressBarModule, MatSlideToggleModule, MatTableModule, TranslocoModule, WiDatePipe],
  templateUrl: './shared-by-me.component.html',
  styleUrl: './sharing-list.scss',
})
export class SharedByMeComponent {
  private readonly sharing = inject(SharingService);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly items = signal<Share[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly search = signal('');
  readonly includeInactive = signal(false);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly columns = ['title', 'recipient', 'status', 'shared', 'expires', 'actions'];

  constructor() {
    queueMicrotask(() => this.load());
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.sharing.byMe(this.page(), this.pageSize(), this.search(), this.includeInactive()).subscribe({
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

  revoke(s: Share): void {
    if (!confirm(this.transloco.translate('sharing.byMe.confirmRevoke', { name: s.recipientName, title: s.historyTitle }))) {
      return;
    }
    this.sharing.revoke(s.historyId, s.shareId).subscribe({
      next: () => {
        this.snack.open(this.transloco.translate('sharing.share.revoked'), undefined, { duration: 3000 });
        this.load();
      },
      error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)),
    });
  }
}
