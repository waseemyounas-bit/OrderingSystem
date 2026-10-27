import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { correlationInterceptor } from './core/activity-log';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // withComponentInputBinding: route params (:id) and query params (?cid=) become component input()s
    provideRouter(routes, withComponentInputBinding()),
    // Every HttpClient call passes through the correlation-id interceptor
    provideHttpClient(withFetch(), withInterceptors([correlationInterceptor])),
  ],
};
