import { effect, inject, Injectable, signal, untracked } from '@angular/core';
import { SwPush } from '@angular/service-worker';
import { firstValueFrom } from 'rxjs';
import { NotificationsService, PushDevice } from '../api/notifications.service';
import { AuthService, needsSetup } from '../auth/auth.service';
import { currentLanguage } from '../i18n/language';

/**
 * Where phone notifications stand on this phone, for the person signed in:
 * - unsupported: this browser cannot do them (or the app is running without its service worker).
 * - needs-install: an iPhone, where the app must first be added to the Home Screen.
 * - unavailable: the server has no sender keys, so they are not offered.
 * - blocked: the person said no in the browser; only its settings can change that.
 * - off: possible, and the person has not turned them on.
 * - on: this phone is registered for this person.
 */
export type PushState = 'checking' | 'unsupported' | 'needs-install' | 'unavailable' | 'blocked' | 'off' | 'on';

const flagKey = (userId: string): string => `gurubook.push.${userId}`;

const isIos = (): boolean =>
  /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);

const isInstalled = (): boolean =>
  window.matchMedia?.('(display-mode: standalone)').matches === true || (navigator as Navigator & { standalone?: boolean }).standalone === true;

const read = (key: string): string | null => {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
};

const write = (key: string, value: string | null): void => {
  try {
    if (value === null) localStorage.removeItem(key);
    else localStorage.setItem(key, value);
  } catch {
    // Storage can be blocked; the choice then lasts until the page reloads.
  }
};

/**
 * Turning phone notifications on and off. A phone is registered per person: turning it on is remembered for that person on that phone, so
 * someone else signing in on the same phone is not switched on without asking. The browser's own permission question appears only when
 * the person presses Turn on, never when the app opens.
 */
@Injectable({ providedIn: 'root' })
export class PushService {
  /** Missing where the app runs without its service worker (and in tests of other screens); notifications are then unsupported. */
  private readonly swPush = inject(SwPush, { optional: true });
  private readonly api = inject(NotificationsService);
  private readonly auth = inject(AuthService);

  readonly state = signal<PushState>('checking');
  private publicKey = '';

  /** Looks at the phone each time someone signs in, and tells the server when the language on screen changes. Called once when the app starts. */
  start(): void {
    effect(() => {
      const user = this.auth.user();
      const listens = !!user && !needsSetup(user) && (user.roles.includes('Teacher') || user.roles.includes('Student'));
      untracked(() => (listens ? void this.refresh() : this.state.set('checking')));
    });

    effect(() => {
      const language = currentLanguage();
      untracked(() => void this.saveLanguage(language));
    });
  }

  async refresh(): Promise<void> {
    const user = this.auth.user();
    if (!user) {
      this.state.set('checking');
      return;
    }

    const swPush = this.swPush;
    if (!swPush?.isEnabled || typeof Notification === 'undefined' || !('PushManager' in window)) {
      this.state.set(isIos() && !isInstalled() ? 'needs-install' : 'unsupported');
      return;
    }

    try {
      const settings = await firstValueFrom(this.api.pushSettings());
      if (!settings.enabled) {
        this.state.set('unavailable');
        return;
      }

      this.publicKey = settings.publicKey;
    } catch {
      this.state.set('unavailable');
      return;
    }

    if (Notification.permission === 'denied') {
      this.state.set('blocked');
      return;
    }

    const subscription = await firstValueFrom(swPush.subscription);
    if (subscription && read(flagKey(user.id)) === 'on') {
      this.state.set('on');
      await this.send(subscription, true);
    } else {
      this.state.set('off');
    }
  }

  /** Asks the browser for permission, then registers this phone. Returns true when it is on. */
  async enable(): Promise<boolean> {
    const user = this.auth.user();
    const swPush = this.swPush;
    if (!user || !swPush || !this.publicKey) return false;

    let subscription: PushSubscription;
    try {
      subscription = await swPush.requestSubscription({ serverPublicKey: this.publicKey });
    } catch {
      // The person said no, or the browser could not subscribe.
      this.state.set(Notification.permission === 'denied' ? 'blocked' : 'off');
      return false;
    }

    try {
      await this.send(subscription, false);
    } catch {
      // The server refused the phone; the error interceptor has already told the person why.
      await swPush.unsubscribe().catch(() => undefined);
      this.state.set('off');
      return false;
    }

    write(flagKey(user.id), 'on');
    this.state.set('on');
    return true;
  }

  async disable(): Promise<void> {
    const user = this.auth.user();
    const swPush = this.swPush;
    if (!swPush) return;
    const subscription = await firstValueFrom(swPush.subscription);
    if (user) write(flagKey(user.id), null);
    if (subscription) await firstValueFrom(this.api.removePushDevice(subscription.endpoint)).catch(() => undefined);
    await swPush.unsubscribe().catch(() => undefined);
    this.state.set('off');
  }

  private async saveLanguage(language: string): Promise<void> {
    if (this.state() !== 'on' || !this.swPush) return;
    const subscription = await firstValueFrom(this.swPush.subscription);
    if (subscription) await this.send(subscription, true, language);
  }

  private async send(subscription: PushSubscription, background: boolean, language: string = currentLanguage()): Promise<void> {
    const json = subscription.toJSON();
    const device: PushDevice = { endpoint: subscription.endpoint, p256dh: json.keys?.['p256dh'] ?? '', auth: json.keys?.['auth'] ?? '', language };
    try {
      await firstValueFrom(this.api.savePushDevice(device, background));
    } catch (e) {
      // Registering in the background (to keep the language current) is best effort; turning it on by hand shows the problem.
      if (!background) throw e;
    }
  }
}
