import { ApplicationConfig, ErrorHandler, isDevMode, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from '@angular/material/form-field';
import { MAT_DIALOG_DEFAULT_OPTIONS } from '@angular/material/dialog';
import { provideServiceWorker } from '@angular/service-worker';
import { authInterceptor } from './core/auth/auth.interceptor';
import { GlobalErrorHandler } from './core/errors/global-error-handler';
import { httpErrorInterceptor } from './core/errors/http-error.interceptor';
import { loadingInterceptor } from './core/ui/loading.interceptor';
import { provideAppConfig } from './core/config/app-config';
import { provideTranslations } from './core/i18n/translate.service';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: ErrorHandler, useClass: GlobalErrorHandler },
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([loadingInterceptor, authInterceptor, httpErrorInterceptor])),
    { provide: MAT_FORM_FIELD_DEFAULT_OPTIONS, useValue: { appearance: 'outline', subscriptSizing: 'fixed' } },
    { provide: MAT_DIALOG_DEFAULT_OPTIONS, useValue: { autoFocus: 'first-tabbable', restoreFocus: true } },
    provideAppConfig(),
    provideTranslations(),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
