import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, isDevMode, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideTransloco, provideTranslocoTranspiler } from '@jsverse/transloco';
import { provideEchartsCore } from 'ngx-echarts';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { LanguageService, SUPPORTED_LANGUAGES } from './core/i18n/language.service';
import { LocalizedTranspiler } from './core/i18n/localized-transpiler';
import { TranslocoHttpLoader } from './core/i18n/transloco-loader';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding(), withInMemoryScrolling({ anchorScrolling: 'enabled', scrollPositionRestoration: 'enabled' })),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    provideTransloco({
      config: {
        availableLangs: [...SUPPORTED_LANGUAGES],
        defaultLang: 'en',
        fallbackLang: 'en',
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
      loader: TranslocoHttpLoader,
    }),
    provideTranslocoTranspiler(LocalizedTranspiler),
    provideEchartsCore({ echarts: () => import('echarts') }),
    provideAppInitializer(() => {
      inject(LanguageService).initialize();
      return inject(AuthService).initialize();
    }),
  ],
};
