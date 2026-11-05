import { importProvidersFrom } from '@angular/core';
import { TranslocoTestingModule } from '@jsverse/transloco';
import ar from '../../../public/i18n/ar.json';
import en from '../../../public/i18n/en.json';

/** Transloco with the real translation files loaded synchronously for unit tests. */
export function provideTestTransloco() {
  return importProvidersFrom(
    TranslocoTestingModule.forRoot({
      langs: { en, ar },
      translocoConfig: { availableLangs: ['en', 'ar'], defaultLang: 'en', reRenderOnLangChange: true },
      preloadLangs: true,
    }),
  );
}
