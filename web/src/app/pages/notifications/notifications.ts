import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AppNotification, NotificationList, NotificationsService } from '../../core/api/notifications.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { UnreadNotifications } from '../../core/state/unread-notifications';
import { formatDateTime } from '../../shared/format';
import { describe } from './describe';

/** Swiping a row this far (a share of its width) closes it; less than that and it springs back. */
const CLOSE_AT = 0.35;
/** How far a finger must move sideways before it counts as a swipe rather than a tap or a scroll. */
const SLOP = 10;

/**
 * What the signed-in teacher or student has been told, newest first. Tapping one marks it read and opens the screen it is about.
 * Each one can be closed with its small cross or by swiping it away; closing removes it from the person's list.
 */
@Component({
  selector: 'app-notifications',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'notifications.title' | t }}</h1>
        <p class="page-subtitle">{{ 'notifications.subtitle' | t }}</p>
      </div>
      @if (unread() > 0) {
        <button mat-stroked-button type="button" (click)="readAll()">
          <mat-icon>done_all</mat-icon>
          {{ 'notifications.read_all' | t }}
        </button>
      }
    </header>

    @switch (list.status()) {
      @case ('ready') {
        @if (rows().length > 0) {
          <div class="panel">
            @for (row of rows(); track row.n.id) {
              <div class="swipe">
                <span class="swipe-hint start" aria-hidden="true"><mat-icon>close</mat-icon></span>
                <span class="swipe-hint end" aria-hidden="true"><mat-icon>close</mat-icon></span>
                <div
                  class="swipe-face"
                  [class.unread]="!row.n.isRead"
                  [class.dragging]="drag()?.id === row.n.id && !drag()?.leaving"
                  [style.transform]="drag()?.id === row.n.id ? 'translateX(' + drag()?.dx + 'px)' : null"
                  [style.opacity]="drag()?.id === row.n.id ? fade() : null"
                  (pointerdown)="down($event, row.n.id)"
                  (pointermove)="move($event)"
                  (pointerup)="up($event, row.n)"
                  (pointercancel)="cancel()"
                >
                  <button type="button" class="panel-row item" [class.unread]="!row.n.isRead" (click)="open(row.n)">
                    <span class="avatar" aria-hidden="true"><mat-icon>{{ row.view.icon }}</mat-icon></span>
                    <span class="row-main">
                      <span class="row-title">{{ row.view.title }}</span>
                      @for (line of row.view.lines; track $index) {
                        <span class="row-meta line">{{ line }}</span>
                      }
                      <span class="row-meta when">{{ row.when }}</span>
                    </span>
                    @if (!row.n.isRead) {
                      <span class="dot" role="img" [attr.aria-label]="'notifications.unread' | t"></span>
                    }
                  </button>
                  <button type="button" class="close" [attr.aria-label]="'notifications.dismiss' | t" (click)="dismiss(row.n)">
                    <mat-icon>close</mat-icon>
                  </button>
                </div>
              </div>
            }
          </div>
        } @else {
          <div class="empty">
            <mat-icon>notifications_none</mat-icon>
            <p>{{ 'notifications.empty' | t }}</p>
          </div>
        }
      }
      @case ('error') {
        <div class="empty">
          <p>{{ 'error.load_failed' | t }}</p>
          <button mat-stroked-button type="button" (click)="load()">{{ 'status.retry' | t }}</button>
        </div>
      }
      @default {
        <div class="skeleton"></div>
        <div class="skeleton"></div>
      }
    }
  `,
  styles: `
    .swipe { position: relative; overflow: hidden; background: var(--mat-sys-surface-container-highest); }
    .swipe + .swipe { border-top: 1px solid var(--mat-sys-outline-variant); }
    .swipe-hint { position: absolute; pointer-events: none; inset-block: 0; display: flex; align-items: center; padding-inline: 1.25rem; color: var(--mat-sys-on-surface-variant); }
    .swipe-hint.start { inset-inline-start: 0; }
    .swipe-hint.end { inset-inline-end: 0; }
    .swipe-face {
      position: relative;
      display: flex;
      align-items: flex-start;
      background: var(--mat-sys-surface-container-low);
      touch-action: pan-y;
      transition: transform 0.18s ease-out, opacity 0.18s ease-out;
    }
    .swipe-face.dragging { transition: none; }
    .item { flex: 1; min-width: 0; border: 0; background: transparent; color: inherit; font: inherit; text-align: start; cursor: pointer; align-items: flex-start; padding-inline-end: 0.5rem; }
    .swipe-face.unread { background: var(--mat-sys-surface-container-high); }
    .item:hover { background: var(--mat-sys-surface-container); }
    .close {
      flex: none; display: grid; place-items: center; width: 2.75rem; height: 2.75rem; margin: 0.25rem 0.25rem 0 0; padding: 0;
      border: 0; border-radius: 50%; background: transparent; color: var(--mat-sys-on-surface-variant); cursor: pointer;
    }
    .close:hover { background: var(--mat-sys-surface-container-highest); }
    .close mat-icon { font-size: 1.125rem; width: 1.125rem; height: 1.125rem; }
    .swipe-hint mat-icon { font-size: 1.5rem; }
    .row-main { display: grid; gap: 0.125rem; }
    .line { display: block; }
    .when { display: block; margin-top: 0.25rem; opacity: 0.8; }
    .dot { flex: none; width: 0.625rem; height: 0.625rem; margin-top: 0.5rem; border-radius: 50%; background: var(--mat-sys-primary); }
    .item:not(.unread) .avatar { background: var(--mat-sys-surface-container-highest); color: var(--mat-sys-on-surface-variant); }
    @media (prefers-reduced-motion: reduce) {
      .swipe-face { transition: none; }
    }
  `,
})
export class Notifications {
  private readonly api = inject(NotificationsService);
  private readonly router = inject(Router);
  private readonly i18n = inject(TranslateService);
  private readonly badge = inject(UnreadNotifications);

  protected readonly list = new Loadable<NotificationList>();

  /** The row a finger is moving, how far it has moved, and whether it has been let go far enough to leave. */
  protected readonly drag = signal<{ id: string; dx: number; leaving: boolean } | null>(null);
  protected readonly fade = computed(() => 1 - Math.min(Math.abs(this.drag()?.dx ?? 0) / this.width, 1) * 0.6);
  private width = 360;
  private touch: { id: string; x: number; y: number; pointer: number; swiping: boolean } | null = null;
  /** A swipe ends with a click on the row under the finger; that click is not a tap. */
  private swiped = false;
  protected readonly unread = computed(() => this.list.data()?.unread ?? 0);
  protected readonly rows = computed(() =>
    (this.list.data()?.items ?? []).map((n) => ({ n, view: describe(n, (key) => this.i18n.translate(key)), when: formatDateTime(n.at) })),
  );

  constructor() {
    this.load();
    // The number on the profile icon follows what this list says, however it changed.
    effect(() => {
      const data = this.list.data();
      if (data) this.badge.count.set(data.unread);
    });
  }

  protected load(): void {
    this.list.load(this.api.list());
  }

  protected open(n: AppNotification): void {
    if (this.swiped) {
      this.swiped = false;
      return;
    }
    if (!n.isRead) {
      this.mark(n.id);
      this.api.read(n.id).subscribe();
    }
    if (n.link) void this.router.navigateByUrl(n.link);
  }

  protected readAll(): void {
    this.api.readAll().subscribe(() => {
      this.list.update((l) => ({ unread: 0, items: l.items.map((n) => ({ ...n, isRead: true })) }));
    });
  }

  /** Closes one: it leaves the list at once, and comes back only if the server could not remove it. */
  protected dismiss(n: AppNotification): void {
    this.drag.set(null);
    this.list.update((l) => ({ unread: n.isRead ? l.unread : Math.max(0, l.unread - 1), items: l.items.filter((i) => i.id !== n.id) }));
    this.api.dismiss(n.id).subscribe({ error: () => this.api.list().subscribe((l) => this.list.data.set(l)) });
  }

  protected down(event: PointerEvent, id: string): void {
    // A mouse keeps the cross; swiping is for fingers and pens.
    if (event.pointerType === 'mouse' || this.drag()?.leaving) return;
    this.swiped = false;
    this.width = (event.currentTarget as HTMLElement).offsetWidth || this.width;
    this.touch = { id, x: event.clientX, y: event.clientY, pointer: event.pointerId, swiping: false };
  }

  protected move(event: PointerEvent): void {
    const t = this.touch;
    if (!t || event.pointerId !== t.pointer) return;
    const dx = event.clientX - t.x;
    if (!t.swiping) {
      if (Math.abs(event.clientY - t.y) > SLOP && Math.abs(event.clientY - t.y) > Math.abs(dx)) {
        this.touch = null; // The person is scrolling the list.
        return;
      }
      if (Math.abs(dx) < SLOP) return;
      t.swiping = true;
      (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    }
    this.drag.set({ id: t.id, dx, leaving: false });
  }

  protected up(event: PointerEvent, n: AppNotification): void {
    const t = this.touch;
    this.touch = null;
    if (!t?.swiping || event.pointerId !== t.pointer) return;
    this.swiped = true;
    const dx = event.clientX - t.x;
    if (Math.abs(dx) < this.width * CLOSE_AT) {
      this.drag.set(null);
      return;
    }
    // Slide the rest of the way off, then remove it.
    this.drag.set({ id: t.id, dx: Math.sign(dx) * this.width, leaving: true });
    setTimeout(() => this.dismiss(n), 180);
  }

  protected cancel(): void {
    this.touch = null;
    this.drag.set(null);
  }

  private mark(id: string): void {
    this.list.update((l) => ({ unread: Math.max(0, l.unread - 1), items: l.items.map((n) => (n.id === id ? { ...n, isRead: true } : n)) }));
  }
}
