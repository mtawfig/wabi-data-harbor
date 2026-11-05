import { Component, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule } from '@jsverse/transloco';
import { LanguageService } from '../../core/i18n/language.service';

/** EN/AR toggle used on landing, login, bootstrap, organization and platform shells. */
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
      data-testid="language-switcher"
    >
      <mat-icon>language</mat-icon>
      {{ language.current() === 'en' ? ('common.arabic' | transloco) : ('common.english' | transloco) }}
    </button>
  `,
  styles: `
    .switcher { border-radius: 999px; }
    .switcher--light { color: var(--wi-white); border-color: rgb(255 255 255 / 45%); }
  `,
})
export class LanguageSwitcherComponent {
  readonly language = inject(LanguageService);
  readonly variant = input<'dark' | 'light'>('dark');
}
