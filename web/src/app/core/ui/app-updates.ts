import { DOCUMENT, inject, Injectable } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { filter } from 'rxjs';
import { TranslateService } from '../i18n/translate.service';

/**
 * The installed app keeps running the version it started with until it is closed completely, which on a phone can be days. This looks for a
 * new version when the app opens and each time it comes back to the screen, and offers to reload once one has downloaded.
 */
@Injectable({ providedIn: 'root' })
export class AppUpdates {
  private readonly updates = inject(SwUpdate);
  private readonly snackBar = inject(MatSnackBar);
  private readonly i18n = inject(TranslateService);
  private readonly document = inject(DOCUMENT);

  start(): void {
    if (!this.updates.isEnabled) return;

    this.updates.versionUpdates.pipe(filter((e): e is VersionReadyEvent => e.type === 'VERSION_READY')).subscribe(() => this.offerReload());
    this.updates.unrecoverable.subscribe(() => this.document.location.reload());

    const check = () => void this.updates.checkForUpdate().catch(() => undefined);
    check();
    this.document.addEventListener('visibilitychange', () => {
      if (this.document.visibilityState === 'visible') check();
    });
  }

  private offerReload(): void {
    this.snackBar
      .open(this.i18n.translate('update.ready'), this.i18n.translate('update.reload'), { politeness: 'polite' })
      .onAction()
      .subscribe(() => this.document.location.reload());
  }
}
