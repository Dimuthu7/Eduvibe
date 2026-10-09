import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/** Sign-in screen. The form is shown now so the app can be installed and reviewed; sign-in itself arrives in Sprint 1. */
@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ 'login.title' | t }}</mat-card-title>
        <mat-card-subtitle>{{ 'app.tagline' | t }}</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        <form class="form">
          <mat-form-field>
            <mat-label>{{ 'login.phone' | t }}</mat-label>
            <input matInput type="tel" inputmode="tel" autocomplete="username" disabled />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'login.password' | t }}</mat-label>
            <input matInput type="password" autocomplete="current-password" disabled />
          </mat-form-field>
          <button mat-flat-button type="button" disabled>{{ 'login.submit' | t }}</button>
          <p class="hint">{{ 'login.coming' | t }}</p>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: block; max-width: 28rem; margin: 2rem auto; padding: 0 1rem; }
    .form { display: flex; flex-direction: column; gap: 0.5rem; padding-top: 1rem; }
    .hint { margin: 0.5rem 0 0; opacity: 0.7; font-size: 0.875rem; }
  `,
})
export class Login {}
