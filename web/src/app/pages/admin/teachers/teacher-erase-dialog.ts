import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TeacherSummary } from '../../../core/api/admin.service';
import { errorCode, inlineError } from '../../../core/api/problem';
import { PrivacyService } from '../../../core/api/privacy.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { applyServerError, fieldErrorKey } from '../../../shared/forms';
import { SubmitButton } from '../../../shared/submit-button';

/** Erases a switched-off teacher's data for good. The person must type the teacher's full name, so the wrong teacher is hard to pick. */
@Component({
  selector: 'app-teacher-erase-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ 'privacy.erase_title' | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <p>{{ 'privacy.erase_what' | t }}</p>
        <p class="muted">{{ 'privacy.erase_kept' | t }}</p>
        <p>
          {{ 'privacy.erase_type' | t }} <strong class="name">{{ data.fullName }}</strong>
        </p>
        <mat-form-field>
          <mat-label>{{ 'privacy.erase_name' | t }}</mat-label>
          <input matInput formControlName="confirm" autocomplete="off" autocapitalize="off" spellcheck="false" required />
          <mat-error>{{ key() | t }}</mat-error>
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button label="privacy.erase_submit" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .name { overflow-wrap: anywhere; }
    mat-form-field { width: 100%; }
  `,
})
export class TeacherEraseDialog {
  protected readonly data = inject<TeacherSummary>(MAT_DIALOG_DATA);
  private readonly api = inject(PrivacyService);
  private readonly ref = inject<MatDialogRef<TeacherEraseDialog, boolean>>(MatDialogRef);
  private readonly fb = inject(FormBuilder).nonNullable;

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = this.fb.group({ confirm: ['', [Validators.required]] });

  protected key(): string {
    const control = this.form.controls.confirm;
    return control.touched ? fieldErrorKey(control) : '';
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.busy.set(true);
    this.error.set('');
    this.api.eraseTeacher(this.data.id, this.form.getRawValue().confirm).subscribe({
      next: () => this.ref.close(true),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}
