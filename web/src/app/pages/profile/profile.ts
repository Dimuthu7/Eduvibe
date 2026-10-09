import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { CatalogStore } from '../../core/api/catalog.service';
import { errorCode, inlineError } from '../../core/api/problem';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ToastService } from '../../core/ui/toast.service';
import { applyServerError, fieldErrorKey, filled, nonEmptyArray } from '../../shared/forms';
import { SubmitButton } from '../../shared/submit-button';

@Component({
  selector: 'app-profile',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    SubmitButton,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    TranslatePipe,
  ],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'profile.title' | t }}</h1>
        <p class="page-subtitle">{{ auth.user()?.phone }}</p>
      </div>
    </header>
    <mat-card appearance="outlined">
      <mat-card-content>
        <form class="stack form" [formGroup]="form" (ngSubmit)="save()" novalidate>
          <div class="form-grid">
            <mat-form-field>
              <mat-label>{{ 'person.first_name' | t }}</mat-label>
              <input matInput autocomplete="given-name" formControlName="firstName" maxlength="60" required />
              <mat-error>{{ key('firstName') | t }}</mat-error>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'person.last_name' | t }}</mat-label>
              <input matInput autocomplete="family-name" formControlName="lastName" maxlength="60" required />
              <mat-error>{{ key('lastName') | t }}</mat-error>
            </mat-form-field>
            <mat-form-field class="full">
              <mat-label>{{ 'profile.email' | t }}</mat-label>
              <input matInput type="email" autocomplete="email" formControlName="email" />
              <mat-error>{{ key('email') | t }}</mat-error>
            </mat-form-field>
            @if (auth.user()?.teacherId) {
              <mat-form-field>
                <mat-label>{{ 'person.district' | t }}</mat-label>
                <mat-select formControlName="district" required>
                  @for (district of catalog.districts(); track district) {
                    <mat-option [value]="district">{{ district }}</mat-option>
                  }
                </mat-select>
                <mat-error>{{ key('district') | t }}</mat-error>
              </mat-form-field>
              <mat-form-field>
                <mat-label>{{ 'person.stream' | t }}</mat-label>
                <mat-select formControlName="streamId" required>
                  @for (stream of catalog.streams(); track stream.id) {
                    <mat-option [value]="stream.id">{{ stream.name }}</mat-option>
                  }
                </mat-select>
                <mat-error>{{ key('streamId') | t }}</mat-error>
              </mat-form-field>
              <mat-form-field class="full">
                <mat-label>{{ 'person.subjects' | t }}</mat-label>
                <mat-select formControlName="subjectIds" multiple required>
                  @for (subject of catalog.subjects(); track subject.id) {
                    <mat-option [value]="subject.id">{{ subject.name }}</mat-option>
                  }
                </mat-select>
                <mat-error>{{ key('subjectIds') | t }}</mat-error>
              </mat-form-field>
            }
          </div>
          @if (error()) {
            <p class="field-error" role="alert">{{ error() | t }}</p>
          }
          <div class="row-wrap actions">
            <app-submit-button label="common.save" [busy]="busy()" />
            <a mat-button routerLink="/change-password">{{ 'profile.change_password' | t }}</a>
          </div>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .form { padding-top: 0.5rem; }
    .actions { margin-top: 0.5rem; }
  `,
})
export class Profile {
  protected readonly auth = inject(AuthService);
  protected readonly catalog = inject(CatalogStore);
  private readonly toast = inject(ToastService);

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  private readonly user = this.auth.user();
  protected readonly form = inject(FormBuilder).nonNullable.group({
    firstName: [this.user?.firstName ?? '', [filled, Validators.maxLength(60)]],
    lastName: [this.user?.lastName ?? '', [filled, Validators.maxLength(60)]],
    email: [this.user?.email ?? '', [Validators.email, Validators.maxLength(120)]],
    district: [this.user?.district ?? ''],
    streamId: [this.user?.streamId ?? ''],
    subjectIds: [this.user?.subjectIds ?? ([] as string[])],
  });

  constructor() {
    this.catalog.ensureLoaded();
    if (this.user?.teacherId) {
      const { district, streamId, subjectIds } = this.form.controls;
      district.addValidators(Validators.required);
      streamId.addValidators(Validators.required);
      subjectIds.addValidators(nonEmptyArray);
    }
  }

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected save(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    const value = this.form.getRawValue();
    this.auth
      .updateProfile({ ...value, district: value.district || null, streamId: value.streamId || null })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.toast.success('profile.saved');
        },
        error: (e) => {
          this.busy.set(false);
          if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
        },
      });
  }
}
