import { signal } from '@angular/core';
import { Observable } from 'rxjs';

export type LoadStatus = 'idle' | 'loading' | 'ready' | 'error';

/**
 * State of one thing fetched from the API: loading, ready or failed, plus its data. Screens hold one
 * per list. This small signals-based store is enough for now; a bigger store such as NgRx would only
 * pay off once several screens must share and sync the same data (for example offline attendance).
 */
export class Loadable<T> {
  readonly status = signal<LoadStatus>('idle');
  readonly data = signal<T | undefined>(undefined);
  readonly error = signal<unknown>(null);

  load(source: Observable<T>): void {
    this.status.set('loading');
    this.error.set(null);
    source.subscribe({
      next: (value) => {
        this.data.set(value);
        this.status.set('ready');
      },
      error: (error) => {
        this.error.set(error);
        this.status.set('error');
      },
    });
  }

  /** Change the loaded data without another request, for example after a successful save. */
  update(change: (current: T) => T): void {
    const current = this.data();
    if (current !== undefined) this.data.set(change(current));
  }
}
