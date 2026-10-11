import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { errorCode, inlineError } from '../../core/api/problem';
import { InviteLink, StudentsService } from '../../core/api/students.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { applyServerError, fieldErrorKey } from '../../shared/forms';
import { SubmitButton } from '../../shared/submit-button';

const DAYS = [1, 3, 7, 14, 30];

/** Make a sign-up link for a class: how long it works and, if wanted, how many students can use it. */
@Component({
  selector: 'app-invite-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ 'invites.create' | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <p class="intro">{{ 'invites.intro' | t }}</p>
        <div class="form-grid">
          <mat-form-field>
            <mat-label>{{ 'invites.expires' | t }}</mat-label>
            <mat-select formControlName="days">
              @for (days of options; track days) {
                <mat-option [value]="days">{{ days }} {{ (days === 1 ? 'invites.day' : 'invites.days') | t }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'invites.max_uses' | t }}</mat-label>
            <input matInput type="number" inputmode="numeric" min="1" formControlName="maxUses" />
            <mat-hint>{{ 'invites.max_uses_hint' | t }}</mat-hint>
            <mat-error>{{ key('maxUses') | t }}</mat-error>
          </mat-form-field>
        </div>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button label="invites.create_submit" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .intro { margin-top: 0; }
  `,
})
export class InviteDialog {
  private readonly api = inject(StudentsService);
  private readonly ref = inject<MatDialogRef<InviteDialog, InviteLink>>(MatDialogRef);
  private readonly data = inject<{ classId: string }>(MAT_DIALOG_DATA);

  protected readonly options = DAYS;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    days: [7],
    maxUses: this.fb.control<number | null>(null, [Validators.min(1), Validators.max(500)]),
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
    const { days, maxUses } = this.form.getRawValue();
    this.api.createInvite(this.data.classId, days, maxUses).subscribe({
      next: (invite) => this.ref.close(invite),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}
