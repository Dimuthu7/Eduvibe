import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AppConfig } from '../config/app-config';
import { AuthService } from './auth.service';

const PUBLIC_PATHS = ['/api/identity/login', '/api/identity/refresh', '/api/identity/logout'];

/** Adds the access token to API calls, and on a 401 refreshes it once and retries. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const base = inject(AppConfig).apiBaseUrl;

  const isApi = request.url.startsWith(`${base}/api/`);
  const isPublic = PUBLIC_PATHS.some((path) => request.url.endsWith(path));
  if (!isApi || isPublic) return next(request);

  const withToken = (req: HttpRequest<unknown>) =>
    auth.accessToken ? req.clone({ setHeaders: { Authorization: `Bearer ${auth.accessToken}` } }) : req;

  return next(withToken(request)).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status !== 401 || !auth.hasStoredSession()) return throwError(() => error);
      return from(auth.refresh()).pipe(
        switchMap((ok) => {
          if (!ok) {
            auth.expire();
            return throwError(() => error);
          }
          return next(withToken(request));
        }),
      );
    }),
  );
};
