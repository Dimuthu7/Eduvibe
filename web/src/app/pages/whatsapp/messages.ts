import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { PlansService } from '../../core/api/plans.service';
import { errorCode } from '../../core/api/problem';
import { WhatsAppApi, WhatsAppList, WhatsAppMessage } from '../../core/api/whatsapp.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { formatDateTime } from '../../shared/format';

const KIND_ICON: Record<WhatsAppMessage['kind'], string> = {
  fee_reminder: 'payments',
  absence_alert: 'event_busy',
  results: 'quiz',
};

/** What the teacher has sent on WhatsApp, newest first, with each message's status and how much of the month's allowance is used. */
@Component({
  selector: 'app-whatsapp-messages',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'whatsapp.title' | t }}</h1>
        <p class="page-subtitle">{{ 'whatsapp.subtitle' | t }}</p>
      </div>
    </header>

    @if (locked()) {
      <div class="notice">
        <mat-icon>lock</mat-icon>
        <div>
          <strong>{{ 'whatsapp.locked_title' | t }}</strong>
          <p>
            {{ 'whatsapp.locked_message' | t }}
            @if (availableOn()) {
              {{ 'my_plan.available_on' | t }} <strong>{{ availableOn() }}</strong>.
            }
          </p>
        </div>
      </div>
    } @else {
      @switch (list.status()) {
        @case ('ready') {
          @if (list.data(); as data) {
            @if (data.status.simulated) {
              <p class="test-mode"><mat-icon>science</mat-icon> {{ 'whatsapp.test_mode' | t }}</p>
            } @else if (!data.status.configured) {
              <div class="notice">
                <mat-icon>chat_bubble_outline</mat-icon>
                <div>
                  <strong>{{ 'whatsapp.unconfigured_title' | t }}</strong>
                  <p>{{ 'whatsapp.unconfigured_message' | t }}</p>
                </div>
              </div>
            }

            <section class="allowance panel">
              <div class="allowance-line">
                <strong>{{ allowance() }}</strong>
                <span class="muted">{{ 'whatsapp.allowance_resets' | t }}</span>
              </div>
              @if (data.status.limit) {
                <mat-progress-bar mode="determinate" [value]="percent()" [attr.aria-label]="allowance()" />
              }
            </section>

            @if (rows().length > 0) {
              <div class="panel">
                @for (row of rows(); track row.m.id) {
                  <div class="panel-row">
                    <mat-icon class="kind" aria-hidden="true">{{ row.icon }}</mat-icon>
                    <div class="row-main">
                      <div class="row-title">{{ row.m.studentName }}</div>
                      <div class="row-meta">{{ row.kind }} · {{ row.m.phone }}</div>
                      <div class="row-meta">{{ row.when }}</div>
                      @if (row.m.status === 'Failed') {
                        <div class="row-meta failed">{{ row.reason }}</div>
                      }
                    </div>
                    <span [class]="'state ' + row.m.status.toLowerCase()">{{ row.state }}</span>
                  </div>
                }
              </div>
            } @else {
              <div class="empty">
                <mat-icon>chat</mat-icon>
                <p>{{ 'whatsapp.none' | t }}</p>
              </div>
            }
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
    }
  `,
  styles: `
    .notice { display: flex; gap: 0.75rem; align-items: flex-start; padding: 0.75rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); margin-bottom: 1rem; }
    .notice mat-icon { font-size: 24px; width: 24px; height: 24px; flex: none; color: var(--mat-sys-primary); }
    .notice p { margin: 0.25rem 0 0; font: var(--mat-sys-body-medium); }
    .test-mode { display: flex; align-items: center; gap: 0.5rem; margin: 0 0 1rem; padding: 0.5rem 0.75rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); font: var(--mat-sys-body-medium); }
    .test-mode mat-icon { font-size: 20px; width: 20px; height: 20px; flex: none; }
    .allowance { padding: 0.75rem 1rem; margin-bottom: 1rem; display: grid; gap: 0.5rem; }
    .allowance-line { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 0.25rem 1rem; align-items: baseline; }
    .kind { font-size: 24px; width: 24px; height: 24px; flex: none; color: var(--mat-sys-on-surface-variant); }
    .row-meta.failed { color: var(--mat-sys-error); }
    .state { font: var(--mat-sys-label-medium); white-space: nowrap; color: var(--mat-sys-on-surface-variant); }
    .state.delivered, .state.read { color: var(--mat-sys-primary); }
    .state.failed { color: var(--mat-sys-error); }
  `,
})
export class WhatsAppMessages implements OnInit {
  private readonly api = inject(WhatsAppApi);
  private readonly plans = inject(PlansService);
  private readonly i18n = inject(TranslateService);

  protected readonly list = new Loadable<WhatsAppList>();
  protected readonly locked = signal(false);
  protected readonly availableOn = signal<string | null>(null);

  protected readonly rows = computed(() =>
    (this.list.data()?.items ?? []).map((m) => ({
      m,
      icon: KIND_ICON[m.kind] ?? 'chat',
      kind: this.i18n.translate(`whatsapp.kind_${m.kind}`),
      state: this.i18n.translate(`whatsapp.status_${m.status.toLowerCase()}`),
      reason: this.i18n.translate(`whatsapp.error_${m.error ?? 'failed'}`),
      when: formatDateTime(m.createdAt),
    })),
  );

  protected readonly allowance = computed(() => {
    const status = this.list.data()?.status;
    if (!status) return '';
    return status.limit === null
      ? this.i18n.format('whatsapp.allowance_unlimited', { used: status.used })
      : this.i18n.format('whatsapp.allowance', { used: status.used, limit: status.limit });
  });

  protected readonly percent = computed(() => {
    const status = this.list.data()?.status;
    return status?.limit ? Math.min(100, Math.round((status.used / status.limit) * 100)) : 0;
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.locked.set(false);
    this.list.status.set('loading');
    this.api.messages().subscribe({
      next: (data) => {
        this.list.data.set(data);
        this.list.status.set('ready');
      },
      error: (e) => {
        if (errorCode(e) === 'plan_feature_locked') {
          this.locked.set(true);
          this.plans.mine().subscribe({
            next: (plan) => this.availableOn.set(plan.features.find((f) => f.key === 'whatsapp')?.availableOn ?? null),
            error: () => undefined,
          });
        } else {
          this.list.error.set(e);
          this.list.status.set('error');
        }
      },
    });
  }
}
