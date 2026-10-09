import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { errorKey } from '../../core/api/problem';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

@Component({
  selector: 'app-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ 'profile.title' | t }}</mat-card-title>
        <mat-card-subtitle>{{ auth.user()?.phone }}</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        <form class="form" [formGroup]="form" (ngSubmit)="save()">
          <mat-form-field>
            <mat-label>{{ 'profile.name' | t }}</mat-label>
            <input matInput autocomplete="name" formControlName="fullName" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'profile.email' | t }}</mat-label>
            <input matInput type="email" autocomplete="email" formControlName="email" />
          </mat-form-field>
          @if (auth.user()?.teacherId) {
            <mat-form-field>
              <mat-label>{{ 'profile.town' | t }}</mat-label>
              <input matInput formControlName="town" />
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'profile.subjects' | t }}</mat-label>
              <input matInput formControlName="subjects" />
            </mat-form-field>
          }
          @if (error()) {
            <p class="error" role="alert">{{ error() | t }}</p>
          }
          @if (saved()) {
            <p class="ok" role="status">{{ 'profile.saved' | t }}</p>
          }
          <div class="actions">
            <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">{{ 'profile.save' | t }}</button>
            <a mat-button routerLink="/change-password">{{ 'profile.change_password' | t }}</a>
          </div>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: block; max-width: 32rem; margin: 1.5rem auto; padding: 0 1rem; }
    .form { display: flex; flex-direction: column; gap: 0.5rem; padding-top: 1rem; }
    .actions { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .error { margin: 0; color: var(--mat-sys-error); }
    .ok { margin: 0; }
  `,
})
export class Profile {
  protected readonly auth = inject(AuthService);

  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    fullName: [this.auth.user()?.fullName ?? '', [Validators.required, Validators.maxLength(120)]],
    email: [this.auth.user()?.email ?? '', Validators.email],
    town: [this.auth.user()?.town ?? ''],
    subjects: [this.auth.user()?.subjects ?? ''],
  });

  protected save(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.saved.set(false);
    this.error.set('');
    this.auth.updateProfile(this.form.getRawValue()).subscribe({
      next: () => {
        this.busy.set(false);
        this.saved.set(true);
      },
      error: (e) => {
        this.error.set(errorKey(e));
        this.busy.set(false);
      },
    });
  }
}
