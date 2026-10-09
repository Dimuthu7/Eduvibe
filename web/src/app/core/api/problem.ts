import { HttpErrorResponse } from '@angular/common/http';

/** The error code the API sends (for example "phone_taken"), turned into a translation key. */
export function errorKey(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const code = (error.error as { code?: string } | null)?.code;
    if (code) return `error.${code}`;
    if (error.status === 0) return 'error.network';
    if (error.status === 429) return 'error.too_many_requests';
  }
  return 'error.unknown';
}
