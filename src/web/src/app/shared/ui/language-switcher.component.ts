import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule } from '@jsverse/transloco';
import { map } from 'rxjs';
import { LanguageService } from '../../core/i18n/language.service';

/** Screens up to this width get the icon-only switcher (the shell's phone layout uses the same breakpoint). */
export const COMPACT_SWITCHER_QUERY = '(max-width: 600px)';

/**
 * EN/AR toggle used on landing, login, bootstrap, organization and platform shells. On larger screens it is an outlined
 * pill naming the language it switches to; on small screens it is a globe icon button, the same kind of button as the
 * header's bell, so it lines up with the other header icons. Its accessible name and tooltip name the target language.
 */
@Component({
  selector: 'app-language-switcher',
  imports: [MatButtonModule, MatIconModule, TranslocoModule],
  host: { '[class.switcher-host--compact]': 'compact()' },
  template: `
    @if (compact()) {
      <button
        mat-icon-button
        type="button"
        class="switcher-icon"
        (click)="language.toggle()"
        [attr.aria-label]="(language.current() === 'en' ? 'common.arabic' : 'common.english') | transloco"
        [attr.title]="(language.current() === 'en' ? 'common.arabic' : 'common.english') | transloco"
        data-testid="language-switcher"
      >
        <mat-icon>language</mat-icon>
      </button>
    } @else {
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
    }
  `,
  styles: `
    :host(.switcher-host--compact) { display: inline-flex; align-items: center; }
    .switcher { border-radius: 999px; }
    .switcher--light { color: var(--wi-white); border-color: rgb(255 255 255 / 45%); }
    /* Flex-centred so the globe sits in the middle whatever the font's line height (Arabic fonts are taller). */
    .switcher-icon { color: inherit; display: inline-flex; align-items: center; justify-content: center; }
  `,
})
export class LanguageSwitcherComponent {
  readonly language = inject(LanguageService);
  readonly variant = input<'dark' | 'light'>('dark');
  readonly compact = toSignal(inject(BreakpointObserver).observe(COMPACT_SWITCHER_QUERY).pipe(map((r) => r.matches)), { initialValue: false });
}
