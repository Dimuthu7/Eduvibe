import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { PlansService } from '../../core/api/plans.service';
import { errorCode } from '../../core/api/problem';
import { MESSAGE_CHANNELS, MessageChannel, SmsApi, SmsList, SmsMessage } from '../../core/api/sms.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { formatDateTime } from '../../shared/format';
import { LoadError } from '../../shared/load-error';

/** While a text is waiting, the list is checked again this often, a few times, so the teacher sees it turn to Sent without reloading. */
export const REFRESH_MS = 10_000;
export const REFRESH_TIMES = 6;

const KIND_ICON: Record<SmsMessage['kind'], string> = {
  fee_reminder: 'payments',
  absence_alert: 'event_busy',
  results: 'quiz',
  payment_received: 'receipt_long',
  low_attendance: 'running_with_errors',
};

/**
 * The texts a teacher has sent, newest first, with what each one said, what it cost and whether it arrived. The teacher also chooses here
 * whether their paid messages go by WhatsApp first or by SMS alone, and can stop texts to a number a parent asked them about.
 */
@Component({
  selector: 'app-sms-texts',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LoadError, MatButtonModule, MatButtonToggleModule, MatIconModule, MatProgressBarModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'sms.title' | t }}</h1>
        <p class="page-subtitle">{{ 'sms.subtitle' | t }}</p>
      </div>
    </header>

    @if (locked()) {
      <div class="notice">
        <mat-icon>lock</mat-icon>
        <div>
          <strong>{{ 'sms.locked_title' | t }}</strong>
          <p>
            {{ 'sms.locked_message' | t }}
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
              <p class="test-mode"><mat-icon>science</mat-icon> {{ 'sms.test_mode' | t }}</p>
            } @else if (!data.status.configured) {
              <div class="notice">
                <mat-icon>sms</mat-icon>
                <div>
                  <strong>{{ 'sms.unconfigured_title' | t }}</strong>
                  <p>{{ 'sms.unconfigured_message' | t }}</p>
                </div>
              </div>
            }

            <section class="panel channel">
              <h2 class="section-title first">{{ 'sms.channel_title' | t }}</h2>
              <mat-button-toggle-group
                class="choices"
                [value]="data.status.channel"
                [disabled]="saving()"
                [attr.aria-label]="'sms.channel_title' | t"
                hideSingleSelectionIndicator
                (change)="setChannel($event.value)"
              >
                @for (choice of channels; track choice) {
                  <mat-button-toggle [value]="choice">{{ 'sms.channel.' + choice | t }}</mat-button-toggle>
                }
              </mat-button-toggle-group>
              <p class="muted hint">{{ 'sms.channel_hint' | t }}</p>
            </section>

            <section class="allowance panel">
              <div class="allowance-line">
                <strong>{{ allowance() }}</strong>
                <span class="muted">{{ 'sms.allowance_resets' | t }}</span>
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
                      <div class="row-meta">{{ row.kind }} · {{ row.m.phone }} · {{ row.cost }}</div>
                      <div class="row-meta">{{ row.when }}</div>
                      <p class="words">{{ row.m.text }}</p>
                      @if (row.m.fallback) {
                        <div class="row-meta">{{ 'sms.after_whatsapp' | t }}</div>
                      }
                      @if (row.m.status === 'Failed') {
                        <div class="row-meta failed">{{ row.reason }}</div>
                      } @else if (row.m.status === 'Queued') {
                        <div class="row-meta queued">{{ 'sms.queued_hint' | t }}</div>
                      }
                      @if (row.m.stopped) {
                        <div class="row-meta stopped">{{ 'sms.stopped' | t }}</div>
                      }
                      <button mat-button type="button" class="stop" [disabled]="saving()" (click)="stop(row.m)">
                        {{ (row.m.stopped ? 'sms.start_again' : 'sms.stop') | t }}
                      </button>
                    </div>
                    <span [class]="'state ' + row.m.status.toLowerCase()">{{ row.state }}</span>
                  </div>
                }
              </div>
            } @else {
              <div class="empty">
                <mat-icon>sms</mat-icon>
                <p>{{ 'sms.none' | t }}</p>
              </div>
            }
          }
        }
        @case ('error') {
          <app-load-error [error]="list.error()" (retry)="load()" />
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
    .channel { padding: 0.75rem 1rem; margin-bottom: 1rem; }
    .section-title.first { margin-top: 0; }
    /* The two choices are long in Sinhala and Tamil, so they wrap rather than run off a phone screen. */
    .choices { max-width: 100%; flex-wrap: wrap; }
    .choices mat-button-toggle { flex: 1 1 auto; }
    .hint { margin: 0.5rem 0 0; font: var(--mat-sys-body-small); }
    .allowance { padding: 0.75rem 1rem; margin-bottom: 1rem; display: grid; gap: 0.5rem; }
    .allowance-line { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 0.25rem 1rem; align-items: baseline; }
    .kind { font-size: 24px; width: 24px; height: 24px; flex: none; color: var(--mat-sys-on-surface-variant); }
    .words { margin: 0.25rem 0 0; font: var(--mat-sys-body-small); color: var(--mat-sys-on-surface-variant); overflow-wrap: anywhere; }
    .row-meta.failed { color: var(--mat-sys-error); }
    .row-meta.queued, .row-meta.stopped { color: var(--mat-sys-on-surface-variant); }
    .stop { margin: 0.25rem 0 0 -0.75rem; }
    .state { font: var(--mat-sys-label-medium); white-space: nowrap; color: var(--mat-sys-on-surface-variant); }
    .state.delivered { color: var(--mat-sys-primary); }
    .state.failed { color: var(--mat-sys-error); }
  `,
})
export class SmsTextsPage implements OnInit {
  private readonly api = inject(SmsApi);
  private readonly plans = inject(PlansService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(TranslateService);
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;
  private refreshesLeft = REFRESH_TIMES;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected readonly channels = MESSAGE_CHANNELS;
  protected readonly list = new Loadable<SmsList>();
  protected readonly locked = signal(false);
  protected readonly availableOn = signal<string | null>(null);
  protected readonly saving = signal(false);

  protected readonly rows = computed(() =>
    (this.list.data()?.items ?? []).map((m) => ({
      m,
      icon: KIND_ICON[m.kind] ?? 'sms',
      kind: this.i18n.translate(`whatsapp.kind_${m.kind}`),
      state: this.i18n.translate(`sms.status_${m.status.toLowerCase()}`),
      reason: this.i18n.translate(`sms.error_${m.error ?? 'failed'}`),
      cost: this.i18n.format('sms.parts', { parts: m.parts }),
      when: formatDateTime(m.createdAt),
    })),
  );

  protected readonly allowance = computed(() => {
    const status = this.list.data()?.status;
    if (!status) return '';
    return status.limit === null
      ? this.i18n.format('sms.allowance_unlimited', { used: status.used })
      : this.i18n.format('sms.allowance', { used: status.used, limit: status.limit });
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
    this.refreshesLeft = REFRESH_TIMES;
    this.fetch();
  }

  protected setChannel(channel: MessageChannel): void {
    this.saving.set(true);
    this.api.saveChannel(channel).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success('sms.channel_saved');
        this.patch((data) => ({ ...data, status: { ...data.status, channel } }));
      },
      error: () => this.saving.set(false),
    });
  }

  protected stop(message: SmsMessage): void {
    const on = message.stopped;
    this.saving.set(true);
    this.api.stop(message.id, on).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success(on ? 'sms.started_again' : 'sms.stopped_saved');
        // Every text to that number is affected, not only this one.
        this.patch((data) => ({
          ...data,
          items: data.items.map((m) => (m.phone === message.phone ? { ...m, stopped: !on } : m)),
        }));
      },
      error: () => this.saving.set(false),
    });
  }

  private patch(change: (data: SmsList) => SmsList): void {
    const data = this.list.data();
    if (data) {
      this.list.data.set(change(data));
    }
  }

  private fetch(): void {
    clearTimeout(this.refreshTimer);
    this.api.messages().subscribe({
      next: (data) => {
        this.list.data.set(data);
        this.list.status.set('ready');
        // Asking for the list also nudges the server to send what is waiting, so a quiet re-check usually shows it Sent.
        if (data.items.some((m) => m.status === 'Queued') && this.refreshesLeft-- > 0) {
          this.refreshTimer = setTimeout(() => this.fetch(), REFRESH_MS);
        }
      },
      error: (e) => {
        if (errorCode(e) === 'plan_feature_locked') {
          this.locked.set(true);
          this.plans.mine().subscribe({
            next: (plan) => this.availableOn.set(plan.features.find((f) => f.key === 'sms')?.availableOn ?? null),
            error: () => undefined,
          });
        } else if (this.list.status() !== 'ready') {
          // A failed quiet re-check keeps what is already on screen.
          this.list.error.set(e);
          this.list.status.set('error');
        }
      },
    });
  }
}
