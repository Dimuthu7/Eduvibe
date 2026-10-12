import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { addMonths, formatMonth } from './format';

/** Previous and next month arrows with the month name between them. Months run up to the current one and back 3 years. */
@Component({
  selector: 'app-month-nav',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <nav class="month-nav" [attr.aria-label]="'fees.choose_month' | t">
      <button mat-icon-button type="button" (click)="move(-1)" [disabled]="!canGoBack()" [attr.aria-label]="'fees.previous_month' | t">
        <mat-icon>chevron_left</mat-icon>
      </button>
      <div class="month">
        <strong>{{ label() }}</strong>
        @if (isCurrent()) {
          <span class="tag">{{ 'fees.this_month' | t }}</span>
        }
      </div>
      <button mat-icon-button type="button" (click)="move(1)" [disabled]="isCurrent()" [attr.aria-label]="'fees.next_month' | t">
        <mat-icon>chevron_right</mat-icon>
      </button>
      @if (!isCurrent()) {
        <button mat-button type="button" (click)="monthChange.emit(current())">{{ 'fees.this_month' | t }}</button>
      }
    </nav>
  `,
  styles: `
    .month-nav { display: flex; align-items: center; gap: 0.25rem; margin-bottom: 1rem; }
    .month { display: flex; flex: 1; flex-wrap: wrap; gap: 0.5rem; align-items: center; justify-content: center; min-width: 0; text-align: center; }
    .tag { padding: 0.125rem 0.625rem; border-radius: 999px; background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); font: var(--mat-sys-label-small); white-space: nowrap; }
  `,
})
export class MonthNav {
  /** The month shown, "yyyy-MM". */
  readonly month = input.required<string>();
  /** This month in Sri Lanka: the newest month allowed. */
  readonly current = input.required<string>();
  readonly monthChange = output<string>();

  protected readonly label = computed(() => formatMonth(this.month()));
  protected readonly isCurrent = computed(() => this.month() >= this.current());
  protected readonly canGoBack = computed(() => this.month() > addMonths(this.current(), -36));

  protected move(months: number): void {
    this.monthChange.emit(addMonths(this.month(), months));
  }
}
