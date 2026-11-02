import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { FeeTiming, MonthStatus } from '../core/api/fees.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';

const ICONS: Record<MonthStatus, string> = {
  Paid: 'check_circle',
  PartlyPaid: 'timelapse',
  Unpaid: 'error',
  FreeCard: 'card_giftcard',
};

const TIMING_ICONS: Record<Exclude<FeeTiming, ''>, string> = {
  Overdue: 'event_busy',
  DueToday: 'today',
  DueSoon: 'schedule',
};

/** A small pill saying whether a month is paid, partly paid, unpaid or free. When a due date needs attention (soon, today or past) it says that instead. Colour is never the only signal: the words and an icon say it too. */
@Component({
  selector: 'app-fee-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, TranslatePipe],
  template: `
    <span class="chip {{ look() }}">
      <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
      {{ label() | t }}
    </span>
  `,
  styles: `
    :host { display: inline-flex; }
    .chip { display: inline-flex; align-items: center; gap: 0.25rem; padding: 0.125rem 0.625rem 0.125rem 0.375rem; border-radius: 999px; font: var(--mat-sys-label-medium); white-space: nowrap; background: var(--mat-sys-surface-container-high); color: var(--mat-sys-on-surface-variant); }
    mat-icon { width: 1rem; height: 1rem; font-size: 1rem; }
    .paid { background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .partlypaid { background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
    .unpaid, .overdue { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .duetoday, .duesoon { background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
  `,
})
export class FeeStatus {
  readonly status = input.required<MonthStatus>();
  /** How the due date looks today; empty (the default) shows the plain status. */
  readonly timing = input<FeeTiming>('');
  protected readonly look = computed(() => (this.timing() || this.status()).toLowerCase());
  protected readonly icon = computed(() => (this.timing() ? TIMING_ICONS[this.timing() as Exclude<FeeTiming, ''>] : ICONS[this.status()]));
  protected readonly label = computed(() => (this.timing() ? `fees.timing.${this.timing()}` : `fees.status.${this.status()}`));
}
