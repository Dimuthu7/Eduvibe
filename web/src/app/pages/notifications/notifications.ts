import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
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

/** What the signed-in teacher or student has been told, newest first. Tapping one marks it read and opens the screen it is about. */
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
    .item { width: 100%; border: 0; background: transparent; color: inherit; font: inherit; text-align: start; cursor: pointer; align-items: flex-start; }
    .item:hover { background: var(--mat-sys-surface-container); }
    .item.unread { background: var(--mat-sys-surface-container-high); }
    .row-main { display: grid; gap: 0.125rem; }
    .line { display: block; }
    .when { display: block; margin-top: 0.25rem; opacity: 0.8; }
    .dot { flex: none; width: 0.625rem; height: 0.625rem; margin-top: 0.5rem; border-radius: 50%; background: var(--mat-sys-primary); }
    .item:not(.unread) .avatar { background: var(--mat-sys-surface-container-highest); color: var(--mat-sys-on-surface-variant); }
  `,
})
export class Notifications {
  private readonly api = inject(NotificationsService);
  private readonly router = inject(Router);
  private readonly i18n = inject(TranslateService);
  private readonly badge = inject(UnreadNotifications);

  protected readonly list = new Loadable<NotificationList>();
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

  private mark(id: string): void {
    this.list.update((l) => ({ unread: Math.max(0, l.unread - 1), items: l.items.map((n) => (n.id === id ? { ...n, isRead: true } : n)) }));
  }
}
