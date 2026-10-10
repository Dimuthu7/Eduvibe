import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { errorCode, inlineError } from '../../core/api/problem';
import { applyServerError, fieldErrorKey, filled, usernameValidator } from '../../shared/forms';
import { SubmitButton } from '../../shared/submit-button';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

const MIN_LENGTH = 8;

const sameAsNew = (group: AbstractControl): ValidationErrors | null =>
  group.get('newPassword')?.value === group.get('confirm')?.value ? null : { mismatch: true };

/**
 * First sign-in: replace the one-time password and choose a username. Accounts from before usernames
 * only choose a username and may keep their password. Also reachable from the profile to change the password.
 */
@Component({
  selector: 'app-change-password',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, MatCardModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ (chooseUsername() ? 'setup.title' : 'password.title') | t }}</mat-card-title>
        @if (chooseUsername()) {
          <mat-card-subtitle>{{ (mustChange() ? 'setup.intro' : 'setup.intro_username_only') | t }}</mat-card-subtitle>
        } @else if (mustChange()) {
          <mat-card-subtitle>{{ 'password.required' | t }}</mat-card-subtitle>
        }
      </mat-card-header>
      <mat-card-content>
        <form class="stack form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <mat-form-field>
            <mat-label>{{ (mustChange() ? 'password.one_time' : 'password.current') | t }}</mat-label>
            <input matInput type="password" autocomplete="current-password" formControlName="currentPassword" required />
            <mat-error>{{ key('currentPassword') | t }}</mat-error>
          </mat-form-field>
          @if (chooseUsername()) {
            <mat-form-field>
              <mat-label>{{ 'setup.username' | t }}</mat-label>
              <input matInput autocomplete="username" autocapitalize="none" spellcheck="false" maxlength="30" formControlName="username" required />
              <mat-hint>{{ 'setup.username_hint' | t }}</mat-hint>
              <mat-error>{{ key('username') | t }}</mat-error>
            </mat-form-field>
          }
          <mat-form-field>
            <mat-label>{{ (passwordRequired() ? 'password.new' : 'password.new_optional') | t }}</mat-label>
            <input matInput type="password" autocomplete="new-password" formControlName="newPassword" [required]="passwordRequired()" />
            <mat-hint>{{ 'password.hint' | t }}</mat-hint>
            <mat-error>{{ key('newPassword') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'password.confirm' | t }}</mat-label>
            <input matInput type="password" autocomplete="new-password" formControlName="confirm" />
            <mat-error>{{ confirmKey() | t }}</mat-error>
          </mat-form-field>
          @if (error()) {
            <p class="field-error" role="alert">{{ error() | t }}</p>
          }
          <app-submit-button [label]="chooseUsername() ? 'common.save' : 'password.submit'" [busy]="busy()" />
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
  protected readonly chooseUsername = () => this.auth.user()?.mustChooseUsername ?? false;
  /** Only an older account choosing just a username may leave the password alone. */
  protected readonly passwordRequired = () => this.mustChange() || !this.chooseUsername();
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      currentPassword: ['', filled],
      username: [''],
      newPassword: [''],
      confirm: [''],
    },
    { validators: sameAsNew },
  );

  constructor() {
    const { username, newPassword, confirm } = this.form.controls;
    if (this.chooseUsername()) username.addValidators([filled, usernameValidator]);
    newPassword.addValidators(Validators.minLength(MIN_LENGTH));
    if (this.passwordRequired()) {
      newPassword.addValidators(Validators.required);
      confirm.addValidators(Validators.required);
    }
  }

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected confirmKey(): string {
    const control = this.form.controls.confirm;
    if (!control.touched) return '';
    return control.errors ? fieldErrorKey(control) : this.form.hasError('mismatch') ? 'password.mismatch' : '';
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    const { currentPassword, newPassword, username } = this.form.getRawValue();
    this.auth.changePassword(currentPassword, newPassword, this.chooseUsername() ? username.trim() : undefined).subscribe({
      next: () => void this.router.navigateByUrl(this.auth.homeRoute()),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}
