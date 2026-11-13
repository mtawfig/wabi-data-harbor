import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, isDevMode, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { TranslocoService, provideTransloco, provideTranslocoTranspiler } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { provideEchartsCore } from 'ngx-echarts';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { LanguageService, SUPPORTED_LANGUAGES } from './core/i18n/language.service';
import { LocalizedTranspiler } from './core/i18n/localized-transpiler';
import { TranslocoPaginatorIntl } from './core/i18n/paginator-intl';
import { TranslocoHttpLoader } from './core/i18n/transloco-loader';
import { ResponsiveTablesService } from './core/ui/responsive-tables.service';

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
    { provide: MatPaginatorIntl, useClass: TranslocoPaginatorIntl },
    provideAppInitializer(async () => {
      const language = inject(LanguageService);
      const auth = inject(AuthService);
      const transloco = inject(TranslocoService);
      language.initialize();
      inject(ResponsiveTablesService).start();
      await auth.initialize();
      // Start only once the interface language's texts are loaded, so a message shown at once (an error answered
      // before the first render) is never the raw key.
      await firstValueFrom(transloco.load(language.current())).catch(() => undefined);
    }),
  ],
};
