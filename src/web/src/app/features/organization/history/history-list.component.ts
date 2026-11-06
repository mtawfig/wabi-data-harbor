import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { HistoryService } from '../../../core/history/history.service';
import { AnalysisType, CollectionSummary, HistorySummary } from '../../../core/history/history.models';
import { LanguageService } from '../../../core/i18n/language.service';
import { AskableApplication } from '../../../core/platform/ai.models';
import { AiService } from '../../../core/platform/ai.service';
import { problemMessage } from '../../../core/problem-message';

/**
 * My History and Favorites (specification §23): the signed-in user's saved analyses with search, application,
 * type, date and collection filters, favorites, archive, soft delete and paging. Opening a row shows the stored
 * answer; nothing here touches a business source.
 */
@Component({
  selector: 'app-history-list',
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatMenuModule, MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatSlideToggleModule, MatTableModule, TranslocoModule],
  templateUrl: './history-list.component.html',
  styleUrl: './history-list.component.scss',
})
export class HistoryListComponent {
  private readonly history = inject(HistoryService);
  private readonly ai = inject(AiService);
  private readonly router = inject(Router);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  /** 'favorites' pins the favorite filter on; 'history' shows everything. */
  readonly mode = input<'history' | 'favorites'>('history');

  readonly items = signal<HistorySummary[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly search = signal('');
  readonly applicationId = signal<string | null>(null);
  readonly type = signal<AnalysisType | null>(null);
  readonly archived = signal(false);
  readonly collectionId = signal<string | null>(null);
  readonly from = signal('');
  readonly to = signal('');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly applications = signal<AskableApplication[]>([]);
  readonly collections = signal<CollectionSummary[]>([]);
  readonly newCollectionName = signal('');
  readonly columns = ['title', 'application', 'type', 'version', 'updated', 'actions'];
  readonly types: AnalysisType[] = ['Answer', 'Dashboard', 'Report'];
  readonly isFavorites = computed(() => this.mode() === 'favorites');

  constructor() {
    this.ai.askableApplications().subscribe({ next: (apps) => this.applications.set(apps), error: () => undefined });
    this.loadCollections();
    queueMicrotask(() => this.load());
  }

  appName(id: string): string {
    const app = this.applications().find((a) => a.applicationId === id);
    return app ? (this.language.current() === 'ar' ? app.applicationNameAr : app.applicationName) : '';
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.history
      .list({ page: this.page(), pageSize: this.pageSize(), search: this.search().trim() || undefined, applicationId: this.applicationId(), type: this.type(), archived: this.archived(), favorite: this.isFavorites() ? true : null, collectionId: this.collectionId(), from: this.from() ? new Date(this.from()).toISOString() : null, to: this.to() ? new Date(new Date(this.to()).getTime() + 86_400_000).toISOString() : null })
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.total.set(result.totalCount);
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

  open(item: HistorySummary): void {
    void this.router.navigate(['/app/history', item.historyId]);
  }

  toggleFavorite(item: HistorySummary, event: Event): void {
    event.stopPropagation();
    this.history.setFavorite(item.historyId, !item.isFavorite).subscribe({
      next: (updated) => {
        if (this.isFavorites() && !updated.isFavorite) {
          this.items.update((list) => list.filter((h) => h.historyId !== item.historyId));
          this.total.update((n) => n - 1);
        } else {
          this.items.update((list) => list.map((h) => (h.historyId === item.historyId ? updated : h)));
        }
      },
      error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)),
    });
  }

  toggleArchive(item: HistorySummary): void {
    this.history.setArchived(item.historyId, !item.isArchived).subscribe({ next: () => this.load(), error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)) });
  }

  remove(item: HistorySummary): void {
    if (!confirm(this.transloco.translate('history.confirmDelete', { title: item.title }))) {
      return;
    }
    this.history.delete(item.historyId).subscribe({
      next: () => {
        this.snack.open(this.transloco.translate('history.deleted'), undefined, { duration: 3000 });
        this.load();
      },
      error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)),
    });
  }

  addToCollection(item: HistorySummary, collection: CollectionSummary): void {
    this.history.addToCollection(collection.collectionId, item.historyId).subscribe({
      next: () => {
        this.snack.open(this.transloco.translate('history.addedToCollection', { name: collection.name }), undefined, { duration: 3000 });
        this.loadCollections();
      },
      error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)),
    });
  }

  createCollection(): void {
    const name = this.newCollectionName().trim();
    if (!name) {
      return;
    }
    this.history.createCollection(name).subscribe({
      next: () => {
        this.newCollectionName.set('');
        this.loadCollections();
      },
      error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)),
    });
  }

  deleteCollection(collection: CollectionSummary): void {
    if (!confirm(this.transloco.translate('history.confirmDeleteCollection', { name: collection.name }))) {
      return;
    }
    this.history.deleteCollection(collection.collectionId).subscribe({
      next: () => {
        if (this.collectionId() === collection.collectionId) {
          this.collectionId.set(null);
          this.load();
        }
        this.loadCollections();
      },
      error: (e: HttpErrorResponse) => this.error.set(problemMessage(e, this.transloco)),
    });
  }

  private loadCollections(): void {
    this.history.collections().subscribe({ next: (c) => this.collections.set(c), error: () => undefined });
  }
}
