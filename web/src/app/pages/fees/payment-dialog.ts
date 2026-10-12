import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { EnrolmentFees, FeesService, PaymentMethod, PaymentRecord } from '../../core/api/fees.service';
import { errorCode, inlineError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { applyServerError, fieldErrorKey } from '../../shared/forms';
import { addMonths, formatLkr, formatMonth, monthOf, todayColombo } from '../../shared/format';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { SubmitButton } from '../../shared/submit-button';

const METHODS: PaymentMethod[] = ['Cash', 'BankTransfer', 'Online'];

/** How many months ahead of the current one a payment can be put toward. */
const MONTHS_AHEAD = 3;

/** The months a payment can go toward: every month with something owing, then the next few. */
export function monthChoices(fees: Pick<EnrolmentFees, 'dues'>, current: string): string[] {
  const owing = fees.dues.filter((d) => d.due - d.paid > 0).map((d) => d.month);
  const known = new Set(fees.dues.map((d) => d.month));
  const ahead = Array.from({ length: MONTHS_AHEAD }, (_, i) => addMonths(current, i + 1)).filter((m) => !known.has(m));
  return [...owing.sort(), ...ahead];
}

/** Record money a student has paid. Left on its own it clears the oldest unpaid months first; the teacher can name the months instead. */
@Component({
  selector: 'app-payment-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SearchSelect, SubmitButton, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ 'fees.pay_title' | t }}</h2>
      <mat-dialog-content>
        <p class="who">
          <strong>{{ data.fullName }}</strong>
          <span class="muted"> · {{ data.classTitle }}</span>
        </p>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <div class="form-grid">
          <mat-form-field>
            <mat-label>{{ 'fees.amount' | t }}</mat-label>
            <span matTextPrefix>LKR&nbsp;</span>
            <input matInput type="number" inputmode="decimal" formControlName="amount" min="1" step="any" required />
            @if (data.outstanding > 0) {
              <mat-hint>{{ 'fees.owing_hint' | t }} {{ money(data.outstanding) }}</mat-hint>
            }
            <mat-error>{{ key('amount') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'fees.method' | t }}</mat-label>
            <mat-select formControlName="method" required>
              @for (m of methods; track m) {
                <mat-option [value]="m">{{ 'fees.method.' + m | t }}</mat-option>
              }
            </mat-select>
            <mat-error>{{ key('method') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'fees.paid_on' | t }}</mat-label>
            <input matInput type="date" formControlName="paidOn" [max]="today" required />
            <mat-error>{{ key('paidOn') | t }}</mat-error>
          </mat-form-field>
          <app-search-select class="wide" formControlName="months" [multiple]="true" [label]="'fees.months' | t" [options]="monthOptions()" />
          <p class="wide hint muted">{{ (form.controls.months.value.length === 0 ? 'fees.months_oldest' : 'fees.months_chosen') | t }}</p>
        </div>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button label="fees.pay_submit" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .who { margin: 0 0 1rem; }
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .wide { grid-column: 1 / -1; }
    .hint { margin: 0; font: var(--mat-sys-body-small); }
  `,
})
export class PaymentDialog {
  protected readonly data = inject<EnrolmentFees>(MAT_DIALOG_DATA);
  private readonly api = inject(FeesService);
  private readonly ref = inject<MatDialogRef<PaymentDialog, PaymentRecord>>(MatDialogRef);

  protected readonly today = todayColombo();
  protected readonly money = formatLkr;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly methods = METHODS;
  protected readonly monthOptions = computed<SelectOption[]>(() =>
    monthChoices(this.data, monthOf(this.today)).map((m) => ({ value: m, label: formatMonth(m) })),
  );
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    amount: [this.data.outstanding > 0 ? this.data.outstanding : (null as number | null), [Validators.required, Validators.min(1)]],
    method: ['Cash' as string, [Validators.required]],
    paidOn: [this.today, [Validators.required]],
    months: [[] as string[]],
  });

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.busy.set(true);
    this.error.set('');
    const { amount, method, paidOn, months } = this.form.getRawValue();
    this.api.pay({ enrolmentId: this.data.enrolmentId, amount: Number(amount), method: method as PaymentMethod, paidOn, months }).subscribe({
      next: (payment) => this.ref.close(payment),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}
