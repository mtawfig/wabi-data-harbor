import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { provideTestTransloco } from '../../testing/transloco-testing';
import { TranslocoPaginatorIntl } from './paginator-intl';

describe('TranslocoPaginatorIntl', () => {
  it('labels the paginator in Arabic with Arabic digits, and in English', async () => {
    TestBed.configureTestingModule({ providers: [provideTestTransloco(), TranslocoPaginatorIntl] });
    const transloco = TestBed.inject(TranslocoService);
    const intl = TestBed.inject(TranslocoPaginatorIntl);

    transloco.setActiveLang('ar');
    await new Promise((resolve) => setTimeout(resolve));
    expect(intl.itemsPerPageLabel).toBe('عدد العناصر في الصفحة');
    expect(intl.nextPageLabel).toBe('الصفحة التالية');
    expect(intl.getRangeLabel(4, 25, 1200)).toBe('١٠١ – ١٢٥ من ١٬٢٠٠');

    transloco.setActiveLang('en');
    await new Promise((resolve) => setTimeout(resolve));
    expect(intl.itemsPerPageLabel).toBe('Items per page');
    expect(intl.getRangeLabel(0, 25, 3)).toBe('1 – 3 of 3');
    expect(intl.getRangeLabel(0, 25, 0)).toBe('0 of 0');
  });
});
