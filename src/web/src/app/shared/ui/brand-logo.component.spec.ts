import { TestBed } from '@angular/core/testing';
import { provideTestTransloco } from '../../testing/transloco-testing';
import { LanguageService } from '../../core/i18n/language.service';
import { BrandLogoComponent } from './brand-logo.component';

describe('BrandLogoComponent', () => {
  function render() {
    TestBed.configureTestingModule({ imports: [BrandLogoComponent], providers: [provideTestTransloco()] });
    const fixture = TestBed.createComponent(BrandLogoComponent);
    fixture.componentRef.setInput('showTagline', true);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const text = (id: string) => element.querySelector(`[data-testid="${id}"]`)?.textContent?.trim();
    return { fixture, element, text };
  }

  it('shows the English name and tagline in English', () => {
    const { element, text } = render();
    TestBed.inject(LanguageService).use('en', false);
    expect(text('brand-name')).toBe('Wabi Insight');
    expect(text('brand-tagline')).toBe('Ask Your Data. Trust the Answer.');
    expect(element.querySelector('.logo')!.classList.contains('logo--ar')).toBe(false);
  });

  it('follows the interface language: Arabic name and tagline, and back', () => {
    const { fixture, element, text } = render();
    const language = TestBed.inject(LanguageService);
    language.use('ar', false);
    fixture.detectChanges();
    expect(text('brand-name')).toBe('وابي تكس انسايت');
    expect(text('brand-tagline')).toBe('اسأل بياناتك، وثق بالإجابة');
    expect(element.querySelector('.logo')!.getAttribute('lang')).toBe('ar');
    language.use('en', false);
    fixture.detectChanges();
    expect(text('brand-name')).toBe('Wabi Insight');
  });
});
