import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { inlineError } from '../../core/api/problem';
import { SubmitButton } from '../../shared/submit-button';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

@Component({
  selector: 'app-login',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, MatCardModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ 'login.title' | t }}</mat-card-title>
        <mat-card-subtitle>{{ 'app.tagline' | t }}</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        <form class="stack form" [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field>
            <mat-label>{{ 'login.phone' | t }}</mat-label>
            <input matInput type="tel" inputmode="tel" autocomplete="username" formControlName="phone" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'login.password' | t }}</mat-label>
            <input matInput type="password" autocomplete="current-password" formControlName="password" />
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
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    phone: ['', Validators.required],
    password: ['', Validators.required],
  });

  protected submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    const { phone, password } = this.form.getRawValue();
    this.auth.login(phone, password).subscribe({
      next: () => void this.router.navigateByUrl(this.auth.homeRoute()),
      error: (e) => {
        this.error.set(inlineError(e));
        this.busy.set(false);
      },
    });
  }
}
