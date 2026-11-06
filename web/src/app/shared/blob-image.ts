import { DestroyRef, signal } from '@angular/core';

/**
 * One image fetched from the API and held as a blob URL. The API needs the sign-in header, so an image cannot be loaded by putting its
 * address in `src`; it is fetched like any other request and the blob is given a local address instead. The old address is released
 * whenever a new one replaces it and when the screen goes away, so a long session does not leak images.
 */
export class BlobImage {
  readonly url = signal<string | null>(null);

  private current: string | null = null;

  constructor(destroy?: DestroyRef) {
    destroy?.onDestroy(() => this.set(null));
  }

  set(blob: Blob | null): void {
    if (this.current) URL.revokeObjectURL(this.current);
    this.current = blob ? URL.createObjectURL(blob) : null;
    this.url.set(this.current);
  }
}
