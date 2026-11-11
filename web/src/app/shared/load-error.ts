import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { errorKey, isPlanRejection } from '../core/api/problem';
import { TranslatePipe } from '../core/i18n/translate.pipe';

/**
 * Message key for a list or page that failed to load. A screen the teacher's plan does not include says so (asking them to
 * check their connection would send them looking for a problem they do not have); a server failure says it was the server.
 */
export function loadErrorKey(error: unknown): string {
  if (isPlanRejection(error)) return errorKey(error);
  if (error instanceof HttpErrorResponse) {
    if (error.status === 403) return 'error.forbidden';
    if (error.status >= 500) return 'error.server';
  }
  return 'error.load_failed';
}

/** Trying again cannot help when the plan or the person's role is the reason. */
export function canRetryLoad(error: unknown): boolean {
  return !isPlanRejection(error) && !(error instanceof HttpErrorResponse && error.status === 403);
}

/** What a screen shows in place of a list it could not load, with a Try again button when trying again can help. */
@Component({
  selector: 'app-load-error',
  imports: [MatButtonModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="empty">
      <p>{{ message() | t }}</p>
      @if (canRetry()) {
        <button mat-stroked-button type="button" (click)="retry.emit()">{{ 'status.retry' | t }}</button>
      }
    </div>
  `,
})
export class LoadError {
  /** The failure, as the Loadable or the request gave it. */
  readonly error = input<unknown>(null);
  readonly retry = output<void>();

  protected readonly message = computed(() => loadErrorKey(this.error()));
  protected readonly canRetry = computed(() => canRetryLoad(this.error()));
}
