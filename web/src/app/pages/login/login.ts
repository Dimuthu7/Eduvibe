import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { inlineError } from '../../core/api/problem';
import { SubmitButton } from '../../shared/submit-button';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

@Component({
  selector: 'app-login',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ 'login.title' | t }}</mat-card-title>
        <mat-card-subtitle>{{ 'app.tagline' | t }}</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        <form class="stack form" [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field>
            <mat-label>{{ 'login.username' | t }}</mat-label>
            <input matInput autocomplete="username" autocapitalize="none" autocorrect="off" spellcheck="false" enterkeyhint="next" formControlName="username" />
            <mat-hint>{{ 'login.first_time_hint' | t }}</mat-hint>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'login.password' | t }}</mat-label>
            <input matInput [type]="showPassword() ? 'text' : 'password'" autocomplete="current-password" autocapitalize="none" autocorrect="off" spellcheck="false" enterkeyhint="go" formControlName="password" />
            <button
              mat-icon-button
              matSuffix
              type="button"
              [attr.aria-label]="(showPassword() ? 'login.hide_password' : 'login.show_password') | t"
              [attr.aria-pressed]="showPassword()"
              (click)="showPassword.set(!showPassword())"
            >
              <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
            </button>
          </mat-form-field>
          @if (error()) {
            <p class="field-error" role="alert">{{ error() | t }}</p>
          }
          <app-submit-button label="login.submit" [busy]="busy()" [disabled]="form.invalid" />
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .form { padding-top: 1rem; }
  `,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly busy = signal(false);
  protected readonly showPassword = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    username: ['', Validators.required],
    password: ['', Validators.required],
  });

  protected submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    const { username, password } = this.form.getRawValue();
    this.auth.login(username.trim(), password).subscribe({
      next: () => void this.router.navigateByUrl(this.auth.homeRoute()),
      error: (e) => {
        this.error.set(inlineError(e));
        this.busy.set(false);
      },
    });
  }
}
