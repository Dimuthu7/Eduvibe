import { HttpErrorResponse } from '@angular/common/http';
import { ErrorHandler, inject, Injectable, Injector } from '@angular/core';
import { ToastService } from '../ui/toast.service';

const MIN_GAP_MS = 5000;

/**
 * Last line of defence: anything nobody caught (a template error, a rejected promise) is logged and
 * the person sees one calm message instead of a frozen screen. HTTP failures are skipped because the
 * HTTP error interceptor already told them.
 */
@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  private readonly injector = inject(Injector);
  private lastShown = 0;

  handleError(error: unknown): void {
    console.error(error);
    if (error instanceof HttpErrorResponse) return;

    const now = Date.now();
    if (now - this.lastShown < MIN_GAP_MS) return;
    this.lastShown = now;
    // Looked up late: the toast needs services that may not exist yet if start-up itself fails.
    this.injector.get(ToastService).error('error.unexpected');
  }
}
