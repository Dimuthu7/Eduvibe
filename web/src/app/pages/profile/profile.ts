import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { inlineError } from '../../core/api/problem';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ToastService } from '../../core/ui/toast.service';
import { SubmitButton } from '../../shared/submit-button';

@Component({
  selector: 'app-profile',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, SubmitButton, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <h1 class="page-title">{{ 'profile.title' | t }}</h1>
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ auth.user()?.fullName }}</mat-card-title>
        <mat-card-subtitle>{{ auth.user()?.phone }}</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        <form class="stack form" [formGroup]="form" (ngSubmit)="save()">
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
            <p class="field-error" role="alert">{{ error() | t }}</p>
          }
          <div class="row-wrap">
            <app-submit-button label="profile.save" [busy]="busy()" [disabled]="form.invalid" />
            <a mat-button routerLink="/change-password">{{ 'profile.change_password' | t }}</a>
          </div>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .form { padding-top: 1rem; }
  `,
})
export class Profile {
  protected readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  protected readonly busy = signal(false);
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
    this.error.set('');
    this.auth.updateProfile(this.form.getRawValue()).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success('profile.saved');
      },
      error: (e) => {
        this.error.set(inlineError(e));
        this.busy.set(false);
      },
    });
  }
}
