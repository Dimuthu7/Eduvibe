import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { FeesService, PaymentRecord } from '../../core/api/fees.service';
import { errorCode, inlineError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { applyServerError, fieldErrorKey, filled } from '../../shared/forms';
import { formatLkr } from '../../shared/format';
import { SubmitButton } from '../../shared/submit-button';

/** Cancels a payment that was entered wrongly. The payment stays on the record, marked void with the reason, and its months owe the money again. */
@Component({
  selector: 'app-void-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ 'fees.void_title' | t }}</h2>
      <mat-dialog-content>
        <p class="who">
          <strong>{{ data.receiptNo }}</strong>
          <span class="muted"> · {{ money(data.amount) }}</span>
        </p>
        <p class="muted">{{ 'fees.void_message' | t }}</p>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <mat-form-field>
          <mat-label>{{ 'fees.void_reason' | t }}</mat-label>
          <input matInput formControlName="reason" maxlength="200" autocomplete="off" required />
          <mat-hint>{{ 'fees.void_reason_hint' | t }}</mat-hint>
          <mat-error>{{ key() | t }}</mat-error>
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button label="fees.void_submit" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .who { margin: 0 0 0.5rem; }
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    mat-form-field { width: 100%; }
  `,
})
export class VoidDialog {
  protected readonly data = inject<PaymentRecord>(MAT_DIALOG_DATA);
  private readonly api = inject(FeesService);
  private readonly ref = inject<MatDialogRef<VoidDialog, PaymentRecord>>(MatDialogRef);

  protected readonly money = formatLkr;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    reason: ['', [filled, Validators.maxLength(200)]],
  });

  protected key(): string {
    const control = this.form.controls.reason;
    return control.touched ? fieldErrorKey(control) : '';
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.busy.set(true);
    this.error.set('');
    this.api.voidPayment(this.data.id, this.form.controls.reason.value.trim()).subscribe({
      next: (payment) => this.ref.close(payment),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}
