import { DestroyRef, effect, inject, Injectable, signal } from '@angular/core';
import { AuthService, needsSetup } from '../auth/auth.service';
import { NotificationsService } from '../api/notifications.service';

/** How often to look for new notifications while the app is open and in view. */
export const CHECK_EVERY_MS = 3 * 60 * 1000;

/**
 * The number on the profile icon. It is fetched when a teacher or student signs in, every few minutes while the page is in view,
 * and when the person comes back to the app, so there is no constant traffic from a phone in a pocket.
 */
@Injectable({ providedIn: 'root' })
export class UnreadNotifications {
  private readonly api = inject(NotificationsService);
  private readonly auth = inject(AuthService);

  readonly count = signal(0);

  constructor() {
    const destroy = inject(DestroyRef);
    effect((onCleanup) => {
      const user = this.auth.user();
      const listens = !!user && !needsSetup(user) && (user.roles.includes('Teacher') || user.roles.includes('Student'));
      if (!listens) {
        this.count.set(0);
        return;
      }

      this.refresh();
      const timer = setInterval(() => document.visibilityState === 'visible' && this.refresh(), CHECK_EVERY_MS);
      const back = () => document.visibilityState === 'visible' && this.refresh();
      document.addEventListener('visibilitychange', back);
      onCleanup(() => {
        clearInterval(timer);
        document.removeEventListener('visibilitychange', back);
      });
    });
    destroy.onDestroy(() => this.count.set(0));
  }

  refresh(): void {
    this.api.unread().subscribe({ next: (r) => this.count.set(r.unread), error: () => undefined });
  }
}
