import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { errorCode, errorKey } from '../api/problem';
import { ToastService } from '../ui/toast.service';

/**
 * Safety net for failures the person cannot fix on the form: no connection, server errors, missing
 * permission and rate limits. Rejections with an error code (wrong password, phone already used)
 * are left for the screen to show beside the form. 401 is handled by the auth interceptor.
 */
export const httpErrorInterceptor: HttpInterceptorFn = (request, next) => {
  const toast = inject(ToastService);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status !== 401 && !errorCode(error)) {
        toast.error(errorKey(error));
      }
      return throwError(() => error);
    }),
  );
};
