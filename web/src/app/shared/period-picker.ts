import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { isMonthRange, monthOf, monthRange, todayColombo } from './format';
import { MonthNav } from './month-nav';

export interface DateRange {
  from: string;
  to: string;
}

/**
 * Picks the period a report covers. It starts as a month with previous and next arrows; "Custom dates" swaps in
 * two date boxes for any other range. The parent owns the dates and gets a new range whenever they change.
 */
@Component({
  selector: 'app-period-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatFormFieldModule, MatInputModule, MonthNav, TranslatePipe],
  template: `
    @if (custom()) {
      <div class="custom">
        <mat-form-field class="date">
          <mat-label>{{ 'attendance.from' | t }}</mat-label>
          <input matInput type="date" [value]="from()" [max]="to()" (change)="setFrom($any($event.target).value)" />
        </mat-form-field>
        <mat-form-field class="date">
          <mat-label>{{ 'attendance.to' | t }}</mat-label>
          <input matInput type="date" [value]="to()" [min]="from()" (change)="setTo($any($event.target).value)" />
        </mat-form-field>
        <button mat-button type="button" class="switch" (click)="useMonth()">{{ 'period.by_month' | t }}</button>
      </div>
    } @else {
      <app-month-nav [month]="month()" [current]="currentMonth" (monthChange)="pickMonth($event)" />
      <button mat-button type="button" class="switch" (click)="chosen.set(true)">{{ 'period.custom' | t }}</button>
    }
  `,
  styles: `
    :host { display: flex; flex-wrap: wrap; align-items: flex-start; gap: 0 0.5rem; }
    app-month-nav { flex: 1 1 16rem; }
    .custom { display: flex; flex: 1 1 100%; flex-wrap: wrap; gap: 0 1rem; align-items: flex-start; }
    .date { flex: 1 1 9rem; }
    .switch { margin-top: 0.25rem; }
  `,
})
export class PeriodPicker {
  /** The dates shown, "yyyy-MM-dd". */
  readonly from = input.required<string>();
  readonly to = input.required<string>();
  readonly rangeChange = output<DateRange>();

  protected readonly today = todayColombo();
  protected readonly currentMonth = monthOf(this.today);
  protected readonly month = computed(() => monthOf(this.from()));

  /** Set once the teacher chooses a view; until then the dates decide: a range that is not one whole month opens in the date boxes. */
  protected readonly chosen = signal<boolean | null>(null);
  protected readonly custom = computed(() => this.chosen() ?? !isMonthRange(this.from(), this.to(), this.today));

  protected pickMonth(month: string): void {
    this.rangeChange.emit(monthRange(month, this.today));
  }

  protected useMonth(): void {
    this.chosen.set(false);
    this.rangeChange.emit(monthRange(this.month(), this.today));
  }

  protected setFrom(value: string): void {
    this.chosen.set(true);
    if (value && value <= this.to()) this.rangeChange.emit({ from: value, to: this.to() });
  }

  protected setTo(value: string): void {
    this.chosen.set(true);
    if (value && value >= this.from()) this.rangeChange.emit({ from: this.from(), to: value });
  }
}
