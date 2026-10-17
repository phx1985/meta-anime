import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
  provideAppInitializer,
  inject,
} from '@angular/core';
import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { routes } from './app.routes';
import { apiKeyInterceptor } from './interceptors/api-key.interceptor';
import { authInterceptor } from './interceptors/auth.interceptor';
import { malAuthInterceptor } from './interceptors/mal-auth.interceptor';
import { RuntimeConfigService } from './services/runtime-config.service';
import { UserPreferencesService } from './services/user-preferences.service';
import { FilterPresetService } from './services/filter-preset.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideHttpClient(withXhr(), withInterceptors([apiKeyInterceptor, authInterceptor, malAuthInterceptor])),
    provideRouter(
      routes,
      withInMemoryScrolling({ anchorScrolling: 'enabled', scrollPositionRestoration: 'top' }),
    ),
    provideTranslateService({ fallbackLang: 'en' }),
    provideTranslateHttpLoader({ prefix: './assets/i18n/', suffix: '.json' }),
    // Sequential init: all inject() calls MUST happen synchronously before any await
    // so they run inside Angular's injection context. RuntimeConfig loads first to
    // ensure the API key is set before any authenticated requests fire.
    provideAppInitializer(async () => {
      const runtime = inject(RuntimeConfigService);
      const prefs = inject(UserPreferencesService);
      const filters = inject(FilterPresetService);

      await runtime.load();
      await Promise.all([prefs.load(), filters.load()]);
    }),
  ],
};
