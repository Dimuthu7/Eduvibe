import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { InviteLink, StudentsService } from '../../core/api/students.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { ConfirmData, ConfirmDialog } from '../../shared/confirm-dialog';
import { InviteDialog } from './invite-dialog';

/** The sign-up links of one class: make one, send it to students, and switch it off when it has done its job. */
@Component({
  selector: 'app-invite-links',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MatButtonModule, MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    <div class="head">
      <div>
        <h2 class="section-title">{{ 'invites.title' | t }}</h2>
        <p class="muted subtitle">{{ 'invites.subtitle' | t }}</p>
      </div>
      @if (!archived()) {
        <button mat-stroked-button type="button" (click)="create()">
          <mat-icon>add_link</mat-icon>
          {{ 'invites.create' | t }}
        </button>
      }
    </div>

    @switch (links.status()) {
      @case ('ready') {
        @if (shown().length > 0) {
          <div class="panel">
            @for (link of shown(); track link.id) {
              <div class="panel-row" [class.off]="link.status !== 'Active'">
                <span class="avatar" aria-hidden="true"><mat-icon>link</mat-icon></span>
                <div class="row-main">
                  <div class="row-title">
                    {{ 'invites.status.' + link.status | t }}
                  </div>
                  <div class="row-meta">
                    {{ 'invites.until' | t }} {{ link.expiresAt | date: 'mediumDate' }} ·
                    {{ link.usedCount }}@if (link.maxUses) { / {{ link.maxUses }} } {{ 'invites.used' | t }}
                  </div>
                </div>
                @if (link.status === 'Active') {
                  <button mat-icon-button type="button" (click)="copy(link)" [attr.aria-label]="'invites.copy' | t">
                    <mat-icon>content_copy</mat-icon>
                  </button>
                  <a mat-icon-button [href]="whatsapp(link)" target="_blank" rel="noopener" [attr.aria-label]="'students.share_whatsapp' | t">
                    <mat-icon>send</mat-icon>
                  </a>
                  <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="'common.actions' | t">
                    <mat-icon>more_vert</mat-icon>
                  </button>
                  <mat-menu #menu="matMenu">
                    <button mat-menu-item type="button" (click)="confirmRevoke(link)">
                      <mat-icon>link_off</mat-icon>
                      <span>{{ 'invites.revoke' | t }}</span>
                    </button>
                  </mat-menu>
                }
              </div>
            }
          </div>
        } @else {
          <p class="muted">{{ 'invites.none' | t }}</p>
        }
      }
      @case ('error') {
        <p class="muted">{{ 'error.load_failed' | t }}</p>
      }
      @default {
        <div class="skeleton"></div>
      }
    }
  `,
  styles: `
    :host { display: block; }
    .head { display: flex; flex-wrap: wrap; align-items: flex-end; justify-content: space-between; gap: 0.5rem; margin-top: 1.5rem; margin-bottom: 0.75rem; }
    .head .section-title { margin: 0; }
    .subtitle { margin: 0.125rem 0 0; font: var(--mat-sys-body-small); }
    .off .avatar, .off .row-title { opacity: 0.55; }
  `,
})
export class InviteLinks {
  readonly classId = input.required<string>();
  /** Archived classes take no new students, so no new links. */
  readonly archived = input(false);

  private readonly api = inject(StudentsService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(TranslateService);

  protected readonly links = new Loadable<InviteLink[]>();
  /** Links that still work first, then the ones that have ended. */
  protected readonly shown = computed(() => (this.links.data() ?? []).slice(0, 10));

  constructor() {
    queueMicrotask(() => this.links.load(this.api.invites(this.classId())));
  }

  protected url(link: InviteLink): string {
    return `${location.origin}/register/${link.token}`;
  }

  protected whatsapp(link: InviteLink): string {
    const text = this.i18n.translate('invites.share_message').replace('{class}', link.classTitle).replace('{url}', this.url(link));
    return `https://wa.me/?text=${encodeURIComponent(text)}`;
  }

  protected async copy(link: InviteLink): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.url(link));
      this.toast.success('common.copied');
    } catch {
      // Clipboard access can be blocked; the person can still share it by WhatsApp.
      this.toast.error('common.copy_failed');
    }
  }

  protected create(): void {
    this.dialog
      .open<InviteDialog, { classId: string }, InviteLink>(InviteDialog, {
        data: { classId: this.classId() },
        width: '30rem',
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((link) => {
        if (!link) return;
        this.toast.success('invites.created');
        this.links.update((all) => [link, ...all]);
      });
  }

  protected confirmRevoke(link: InviteLink): void {
    this.dialog
      .open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, {
        data: { title: 'invites.revoke', message: 'invites.revoke_confirm', confirm: 'invites.revoke' },
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((yes) => {
        if (!yes) return;
        this.api.revokeInvite(link.id).subscribe((updated) => {
          this.toast.success('invites.revoked');
          this.links.update((all) => all.map((l) => (l.id === updated.id ? updated : l)));
        });
      });
  }
}
