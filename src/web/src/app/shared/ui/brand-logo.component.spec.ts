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
    expect(element.querySelector('[data-testid="brand-wordmark"]')!.getAttribute('src')).toBe('brand/wabi-insight-wordmark-dark.svg');
  });

  it('follows the interface language: Arabic name and tagline, and back', () => {
    const { fixture, element, text } = render();
    const language = TestBed.inject(LanguageService);
    language.use('ar', false);
    fixture.detectChanges();
    expect(text('brand-name')).toBe('وابي انسايت');
    expect(text('brand-tagline')).toBe('اسأل بياناتك، وثق بالإجابة');
    expect(element.querySelector('.logo')!.getAttribute('lang')).toBe('ar');
    expect(element.querySelector('[data-testid="brand-wordmark"]')!.getAttribute('src')).toBe('brand/wabi-insight-wordmark-dark_ar.svg');
    fixture.componentRef.setInput('variant', 'light');
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="brand-wordmark"]')!.getAttribute('src')).toBe('brand/wabi-insight-wordmark-light_ar.svg');
    language.use('en', false);
    fixture.detectChanges();
    expect(text('brand-name')).toBe('Wabi Insight');
    expect(element.querySelector('[data-testid="brand-wordmark"]')!.getAttribute('src')).toBe('brand/wabi-insight-wordmark-light.svg');
  });

  it('without the tagline is the mark and the name, not the wordmark file', () => {
    const { fixture, element, text } = render();
    fixture.componentRef.setInput('showTagline', false);
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="brand-wordmark"]')).toBeNull();
    expect(text('brand-name')).toBe('Wabi Insight');
  });
});
