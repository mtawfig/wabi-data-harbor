import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideTestTransloco } from '../../testing/transloco-testing';
import { LanguageService } from '../../core/i18n/language.service';
import { LanguageSwitcherComponent } from './language-switcher.component';

function render(small: boolean) {
  TestBed.configureTestingModule({
    imports: [LanguageSwitcherComponent],
    providers: [
      provideTestTransloco(),
      { provide: BreakpointObserver, useValue: { observe: () => of<BreakpointState>({ matches: small, breakpoints: {} }) } },
    ],
  });
  const fixture = TestBed.createComponent(LanguageSwitcherComponent);
  fixture.detectChanges();
  return { fixture, button: (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="language-switcher"]')! };
}

describe('LanguageSwitcherComponent', () => {
  it('on small screens is a globe icon button without the language name, named for screen readers', () => {
    const { button } = render(true);
    expect(button.hasAttribute('mat-icon-button')).toBe(true);
    expect(button.textContent?.trim()).toBe('language');
    expect(button.getAttribute('aria-label')).toBe('Arabic');
    expect(button.getAttribute('title')).toBe('Arabic');
  });

  it('on larger screens keeps the outlined pill with the language name', () => {
    const { button } = render(false);
    expect(button.hasAttribute('mat-stroked-button')).toBe(true);
    expect(button.textContent).toContain('Arabic');
  });

  it('switches the language either way', () => {
    const { fixture, button } = render(true);
    const language = TestBed.inject(LanguageService);
    button.click();
    fixture.detectChanges();
    expect(language.current()).toBe('ar');
  });
});
