import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { fieldErrorKey, filled } from './forms';
import { SubmitButton } from './submit-button';

/** What the dialog is called and what the one field is asking for, both translation keys. */
export interface ReasonAsk {
  title: string;
  label: string;
  hint?: string;
  submit?: string;
}

/** Asks for a short reason and hands it back. The caller does the work, so this knows nothing about what it is for. */
@Component({
  selector: 'app-reason-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ data.title | t }}</h2>
      <mat-dialog-content>
        <mat-form-field>
          <mat-label>{{ data.label | t }}</mat-label>
          <input matInput formControlName="reason" maxlength="200" autocomplete="off" required />
          @if (data.hint) {
            <mat-hint>{{ data.hint | t }}</mat-hint>
          }
          <mat-error>{{ key() | t }}</mat-error>
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button [label]="data.submit ?? 'common.save'" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    mat-form-field { width: 100%; }
  `,
})
export class ReasonDialog {
  protected readonly data = inject<ReasonAsk>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<ReasonDialog, string>>(MatDialogRef);

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
    this.ref.close(this.form.controls.reason.value.trim());
  }
}
