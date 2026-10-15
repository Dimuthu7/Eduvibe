import { HttpErrorResponse } from '@angular/common/http';

/** The error code the API sends with a rejected request (for example "phone_taken"), if any. */
export function errorCode(error: unknown): string | null {
  return error instanceof HttpErrorResponse ? ((error.error as { code?: string } | null)?.code ?? null) : null;
}

/**
 * Message key for an error the person can fix, such as a wrong password: show it next to the form.
 * Returns '' for network, server, permission and plan-limit failures, which the HTTP error interceptor already
 * announced with a toast, so the screen does not repeat them.
 */
export function inlineError(error: unknown): string {
  const code = errorCode(error);
  return code && !isPlanRejection(error) ? `error.${code}` : '';
}

/**
 * A plan limit reached or a feature locked. Any screen can hit these (adding a student, approving a request, restoring a class),
 * so the HTTP error interceptor announces them rather than each screen.
 */
export function isPlanRejection(error: unknown): boolean {
  const code = errorCode(error) ?? '';
  return code.startsWith('plan_limit_') || code.startsWith('plan_feature_');
}

/** Message key for any error, used when a screen has nothing else to show. */
export function errorKey(error: unknown): string {
  const code = errorCode(error);
  if (code) return `error.${code}`;
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'error.network';
    if (error.status === 403) return 'error.forbidden';
    if (error.status === 429) return 'error.too_many_requests';
    if (error.status >= 500) return 'error.server';
  }
  return 'error.unknown';
}
