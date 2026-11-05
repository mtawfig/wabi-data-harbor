import { TestBed } from '@angular/core/testing';
import { provideTestTransloco } from '../../testing/transloco-testing';
import { LanguageService } from './language.service';

describe('LanguageService', () => {
  let service: LanguageService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideTestTransloco()] });
    service = TestBed.inject(LanguageService);
  });

  it('switches to Arabic with RTL direction and persists the choice', () => {
    service.use('ar');

    expect(service.current()).toBe('ar');
    expect(service.direction()).toBe('rtl');
    expect(document.documentElement.getAttribute('dir')).toBe('rtl');
    expect(document.documentElement.getAttribute('lang')).toBe('ar');
    expect(localStorage.getItem('wi.lang')).toBe('ar');
  });

  it('restores the stored language on initialize', () => {
    localStorage.setItem('wi.lang', 'ar');

    service.initialize();

    expect(service.current()).toBe('ar');
    expect(service.isRtl()).toBe(true);
  });

  it('toggles back to English with LTR', () => {
    service.use('ar');
    service.toggle();

    expect(service.current()).toBe('en');
    expect(document.documentElement.getAttribute('dir')).toBe('ltr');
  });

  it('ignores unsupported languages', () => {
    service.use('fr' as never);
    expect(service.current()).toBe('en');
  });
});
