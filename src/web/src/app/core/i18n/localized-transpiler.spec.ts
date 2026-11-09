import { TestBed } from '@angular/core/testing';
import { TranslocoService, provideTranslocoTranspiler } from '@jsverse/transloco';
import { describe, expect, it } from 'vitest';
import { provideTestTransloco } from '../../testing/transloco-testing';
import { LocalizedTranspiler } from './localized-transpiler';

describe('LocalizedTranspiler', () => {
  it('gives numbers in Arabic sentences Arabic digits and leaves English ones unchanged', () => {
    TestBed.configureTestingModule({ providers: [provideTestTransloco(), provideTranslocoTranspiler(LocalizedTranspiler)] });
    const transloco = TestBed.inject(TranslocoService);

    transloco.setActiveLang('en');
    expect(transloco.translate('ask.truncated', { count: 12500 })).toContain('first 12500 results');

    transloco.setActiveLang('ar');
    expect(transloco.translate('ask.truncated', { count: 12500 })).toContain('١٢٬٥٠٠');
    expect(transloco.translate('ask.truncated', { count: 6 })).toContain('٦');
  });
});
