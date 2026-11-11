import { Injectable, OnDestroy, inject } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TranslocoService } from '@jsverse/transloco';
import { Subscription, switchMap } from 'rxjs';
import { formatNumberValue } from '../../shared/format/locale-format';

/**
 * Every list's paginator in the interface language (F2): "Items per page", "1 – 25 of 120" with Arabic digits in
 * Arabic, and the button labels screen readers announce. Re-renders when the language changes.
 */
@Injectable()
export class TranslocoPaginatorIntl extends MatPaginatorIntl implements OnDestroy {
  private readonly transloco = inject(TranslocoService);
  private readonly subscription: Subscription;

  constructor() {
    super();
    // Labels are read once the translations of the new language have loaded.
    this.subscription = this.transloco.langChanges$.pipe(switchMap((language) => this.transloco.selectTranslation(language))).subscribe(() => this.update());
  }

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    const language = this.transloco.getActiveLang();
    const n = (value: number) => formatNumberValue(value, '1.0-0', language, true);
    if (length === 0 || pageSize === 0) {
      return this.transloco.translate('common.paginator.empty', { length: n(length) });
    }
    const start = page * pageSize;
    const end = Math.min(start + pageSize, length);
    return this.transloco.translate('common.paginator.range', { start: n(start + 1), end: n(end), length: n(length) });
  };

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
  }

  private update(): void {
    this.itemsPerPageLabel = this.transloco.translate('common.paginator.itemsPerPage');
    this.nextPageLabel = this.transloco.translate('common.paginator.next');
    this.previousPageLabel = this.transloco.translate('common.paginator.previous');
    this.firstPageLabel = this.transloco.translate('common.paginator.first');
    this.lastPageLabel = this.transloco.translate('common.paginator.last');
    this.changes.next();
  }
}
