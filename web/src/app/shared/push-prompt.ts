import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../core/auth/auth.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { PushService } from '../core/notifications/push.service';
import { ToastService } from '../core/ui/toast.service';
import { addDays, todayColombo } from './format';

/** After "Not now" the offer stays away for this many days. */
export const SNOOZE_DAYS = 14;

const snoozeKey = (userId: string): string => `gurubook.push-snooze.${userId}`;

/**
 * A friendly step that offers phone notifications at a good moment: on the notifications screen and on a student's or teacher's home
 * screen, never when the app first opens. The browser's permission question appears only when the person presses Turn on.
 * On an iPhone it explains that the app must be on the Home Screen first. "Not now" quiets the offer for two weeks.
 * With `status`, it also shows when notifications are already on (with a way to turn them off) or blocked by the browser.
 */
@Component({
  selector: 'app-push-prompt',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    @switch (view()) {
      @case ('offer') {
        <section class="card" [attr.aria-label]="'push.offer_title' | t">
          <mat-icon aria-hidden="true">notifications_active</mat-icon>
          <div class="text">
            <strong>{{ 'push.offer_title' | t }}</strong>
            <span>{{ 'push.offer_body' | t }}</span>
          </div>
          <div class="actions">
            <button mat-button type="button" (click)="snooze()">{{ 'push.not_now' | t }}</button>
            <button mat-flat-button type="button" [disabled]="busy()" (click)="turnOn()">{{ 'push.turn_on' | t }}</button>
          </div>
        </section>
      }
      @case ('install') {
        <section class="card" [attr.aria-label]="'push.install_title' | t">
          <mat-icon aria-hidden="true">ios_share</mat-icon>
          <div class="text">
            <strong>{{ 'push.install_title' | t }}</strong>
            <span>{{ 'push.install_body' | t }}</span>
          </div>
          <div class="actions">
            <button mat-button type="button" (click)="snooze()">{{ 'push.install_ok' | t }}</button>
          </div>
        </section>
      }
      @case ('blocked') {
        <section class="card quiet" role="status">
          <mat-icon aria-hidden="true">notifications_off</mat-icon>
          <div class="text">
            <strong>{{ 'push.blocked_title' | t }}</strong>
            <span>{{ 'push.blocked_body' | t }}</span>
          </div>
        </section>
      }
      @case ('on') {
        <section class="card quiet" role="status">
          <mat-icon aria-hidden="true">notifications_active</mat-icon>
          <div class="text">
            <strong>{{ 'push.on_title' | t }}</strong>
            <span>{{ 'push.on_body' | t }}</span>
          </div>
          <div class="actions">
            <button mat-button type="button" [disabled]="busy()" (click)="turnOff()">{{ 'push.turn_off' | t }}</button>
          </div>
        </section>
      }
    }
  `,
  styles: `
    :host { display: block; }
    .card { display: grid; grid-template-columns: auto 1fr; gap: 0.25rem 0.75rem; align-items: start; margin-bottom: 1rem; padding: 0.875rem 1rem 0.5rem; border-radius: var(--mat-sys-corner-large); background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); }
    .card.quiet { background: var(--mat-sys-surface-container); color: var(--mat-sys-on-surface); }
    .card > mat-icon { font-size: 1.5rem; width: 1.5rem; height: 1.5rem; margin-top: 0.125rem; }
    .text { display: grid; gap: 0.125rem; font: var(--mat-sys-body-medium); }
    .text strong { font: var(--mat-sys-title-small); }
    .actions { grid-column: 1 / -1; display: flex; justify-content: flex-end; gap: 0.5rem; }
  `,
})
export class PushPrompt {
  /** Also show the steady states (on, blocked), for the notifications screen. */
  readonly status = input(false);

  private readonly push = inject(PushService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  protected readonly busy = signal(false);
  private readonly snoozed = signal(this.readSnooze());

  protected readonly view = computed<'offer' | 'install' | 'blocked' | 'on' | ''>(() => {
    const state = this.push.state();
    const quiet = this.snoozed() && !this.status();
    if (state === 'off') return quiet ? '' : 'offer';
    if (state === 'needs-install') return quiet ? '' : 'install';
    if (this.status() && (state === 'blocked' || state === 'on')) return state;
    return '';
  });

  protected async turnOn(): Promise<void> {
    this.busy.set(true);
    try {
      if (await this.push.enable()) this.toast.success('push.turned_on');
    } finally {
      this.busy.set(false);
    }
  }

  protected async turnOff(): Promise<void> {
    this.busy.set(true);
    try {
      await this.push.disable();
      this.toast.success('push.turned_off');
    } finally {
      this.busy.set(false);
    }
  }

  protected snooze(): void {
    this.snoozed.set(true);
    try {
      localStorage.setItem(snoozeKey(this.auth.user()?.id ?? ''), addDays(todayColombo(), SNOOZE_DAYS));
    } catch {
      // Storage can be blocked; the offer is then hidden until the page reloads.
    }
  }

  private readSnooze(): boolean {
    try {
      const until = localStorage.getItem(snoozeKey(this.auth.user()?.id ?? ''));
      return !!until && todayColombo() < until;
    } catch {
      return false;
    }
  }
}
