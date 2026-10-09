import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { inlineError } from '../../core/api/problem';
import { SubmitButton } from '../../shared/submit-button';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

const MIN_LENGTH = 8;

const sameAsNew = (group: AbstractControl): ValidationErrors | null =>
  group.get('newPassword')?.value === group.get('confirm')?.value ? null : { mismatch: true };

/** Shown after signing in with a one-time password, and reachable from the profile to change it later. */
@Component({
  selector: 'app-change-password',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, MatCardModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ 'password.title' | t }}</mat-card-title>
        @if (mustChange()) {
          <mat-card-subtitle>{{ 'password.required' | t }}</mat-card-subtitle>
        }
      </mat-card-header>
      <mat-card-content>
        <form class="stack form" [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field>
            <mat-label>{{ (mustChange() ? 'password.one_time' : 'password.current') | t }}</mat-label>
            <input matInput type="password" autocomplete="current-password" formControlName="currentPassword" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'password.new' | t }}</mat-label>
            <input matInput type="password" autocomplete="new-password" formControlName="newPassword" />
            <mat-hint>{{ 'password.hint' | t }}</mat-hint>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'password.confirm' | t }}</mat-label>
            <input matInput type="password" autocomplete="new-password" formControlName="confirm" />
          </mat-form-field>
          @if (form.hasError('mismatch') && form.controls.confirm.dirty) {
            <p class="field-error" role="alert">{{ 'password.mismatch' | t }}</p>
          }
          @if (error()) {
            <p class="field-error" role="alert">{{ error() | t }}</p>
          }
          <app-submit-button label="password.submit" [busy]="busy()" [disabled]="form.invalid" />
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .form { padding-top: 1rem; }
  `,
})
export class ChangePassword {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly mustChange = () => this.auth.user()?.mustChangePassword ?? false;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', [Validators.required, Validators.minLength(MIN_LENGTH)]],
      confirm: ['', Validators.required],
    },
    { validators: sameAsNew },
  );

  protected submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    const { currentPassword, newPassword } = this.form.getRawValue();
    this.auth.changePassword(currentPassword, newPassword).subscribe({
      next: () => void this.router.navigateByUrl(this.auth.homeRoute()),
      error: (e) => {
        this.error.set(inlineError(e));
        this.busy.set(false);
      },
    });
  }
}
