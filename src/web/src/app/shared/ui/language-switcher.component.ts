import { Component, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule } from '@jsverse/transloco';
import { LanguageService } from '../../core/i18n/language.service';

/**
 * EN/AR toggle used on landing, login, bootstrap, organization and platform shells. It names the language it switches
 * to; on small screens it becomes a compact pill with the globe and that language's short name in its own script
 * ("ع" or "EN"), so it keeps its shape next to the other header buttons.
 */
@Component({
  selector: 'app-language-switcher',
  imports: [MatButtonModule, MatIconModule, TranslocoModule],
  template: `
    <button
      mat-stroked-button
      type="button"
      class="switcher"
      [class.switcher--light]="variant() === 'light'"
      (click)="language.toggle()"
      [attr.aria-label]="'common.language' | transloco"
      [attr.title]="(target() === 'ar' ? 'common.arabic' : 'common.english') | transloco"
      data-testid="language-switcher"
    >
      <span class="switcher__content">
        <mat-icon class="switcher__icon" aria-hidden="true">language</mat-icon>
        <span class="switcher__full">{{ (target() === 'ar' ? 'common.arabic' : 'common.english') | transloco }}</span>
        <span class="switcher__short" [attr.lang]="target()" aria-hidden="true">{{ target() === 'ar' ? 'ع' : 'EN' }}</span>
      </span>
    </button>
  `,
  styles: `
    .switcher { border-radius: 999px; }
    .switcher--light { color: var(--wi-white); border-color: rgb(255 255 255 / 45%); }
    .switcher__content { display: inline-flex; align-items: center; gap: 6px; line-height: 1; }
    .switcher__icon { width: 20px; height: 20px; font-size: 20px; margin: 0; }
    .switcher__short { display: none; font-weight: 600; font-size: 14px; min-width: 1.25em; text-align: center; }
    .switcher__short:lang(ar) { font-family: var(--wi-font-arabic); font-size: 16px; }
    @media (max-width: 600px) {
      .switcher { min-width: 0; height: 36px; padding-inline: 10px 12px; }
      .switcher__content { gap: 4px; }
      .switcher__full { display: none; }
      .switcher__short { display: inline-block; }
    }
  `,
})
export class LanguageSwitcherComponent {
  readonly language = inject(LanguageService);
  readonly variant = input<'dark' | 'light'>('dark');
  /** The language a click switches to. */
  readonly target = computed(() => (this.language.current() === 'en' ? 'ar' : 'en'));
}
