import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { Subject } from 'rxjs';

export type AppLanguage = 'en' | 'ar';
export type TextDirection = 'ltr' | 'rtl';

export const SUPPORTED_LANGUAGES: readonly AppLanguage[] = ['en', 'ar'];
const STORAGE_KEY = 'wi.lang';

/**
 * Runtime language switching with RTL support. The person's own choice is kept in this browser and, once they are
 * signed in, saved on their account (C10), so it survives a reload, a new tab and the print view. Sets <html lang dir>
 * so Angular Material and CSS logical properties follow the direction automatically.
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly document = inject(DOCUMENT);
  private readonly transloco = inject(TranslocoService);

  readonly current = signal<AppLanguage>('en');
  readonly direction = computed<TextDirection>(() => (this.current() === 'ar' ? 'rtl' : 'ltr'));
  readonly isRtl = computed(() => this.direction() === 'rtl');
  /** Emits when the person picks a language with the switcher (not when the app applies a stored one). */
  readonly chosen = new Subject<AppLanguage>();

  initialize(): void {
    const stored = this.readStored();
    const browser = this.document.defaultView?.navigator.language?.toLowerCase().startsWith('ar') ? 'ar' : 'en';
    this.use(stored ?? browser, false);
  }

  use(language: AppLanguage, persist = true): void {
    if (!SUPPORTED_LANGUAGES.includes(language)) {
      return;
    }

    this.current.set(language);
    this.transloco.setActiveLang(language);
    const html = this.document.documentElement;
    html.setAttribute('lang', language);
    html.setAttribute('dir', language === 'ar' ? 'rtl' : 'ltr');

    if (persist) {
      try {
        this.document.defaultView?.localStorage.setItem(STORAGE_KEY, language);
      } catch {
        // Storage can be unavailable (private mode); the choice simply does not persist.
      }
    }
  }

  toggle(): void {
    this.use(this.current() === 'en' ? 'ar' : 'en');
    this.chosen.next(this.current());
  }

  /** The language this browser last chose, or null when it never chose one. */
  storedChoice(): AppLanguage | null {
    return this.readStored();
  }

  private readStored(): AppLanguage | null {
    try {
      const value = this.document.defaultView?.localStorage.getItem(STORAGE_KEY);
      return value === 'ar' || value === 'en' ? value : null;
    } catch {
      return null;
    }
  }
}
