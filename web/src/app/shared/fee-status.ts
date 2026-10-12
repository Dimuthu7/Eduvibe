import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MonthStatus } from '../core/api/fees.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';

const ICONS: Record<MonthStatus, string> = {
  Paid: 'check_circle',
  PartlyPaid: 'timelapse',
  Unpaid: 'error',
  FreeCard: 'card_giftcard',
};

/** A small pill saying whether a month is paid, partly paid, unpaid or free. Colour is never the only signal: the words and an icon say it too. */
@Component({
  selector: 'app-fee-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, TranslatePipe],
  template: `
    <span class="chip {{ status().toLowerCase() }}">
      <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
      {{ 'fees.status.' + status() | t }}
    </span>
  `,
  styles: `
    :host { display: inline-flex; }
    .chip { display: inline-flex; align-items: center; gap: 0.25rem; padding: 0.125rem 0.625rem 0.125rem 0.375rem; border-radius: 999px; font: var(--mat-sys-label-medium); white-space: nowrap; background: var(--mat-sys-surface-container-high); color: var(--mat-sys-on-surface-variant); }
    mat-icon { width: 1rem; height: 1rem; font-size: 1rem; }
    .paid { background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .partlypaid { background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
    .unpaid { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
  `,
})
export class FeeStatus {
  readonly status = input.required<MonthStatus>();
  protected readonly icon = computed(() => ICONS[this.status()]);
}
