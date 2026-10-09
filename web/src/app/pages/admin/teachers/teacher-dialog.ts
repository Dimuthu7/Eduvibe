import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { SearchSelect } from '../../../shared/search-select';
import { AdminService, TeacherSummary } from '../../../core/api/admin.service';
import { CatalogStore } from '../../../core/api/catalog.service';
import { errorCode, inlineError } from '../../../core/api/problem';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { applyServerError, fieldErrorKey, filled, nonEmptyArray, phoneValidator } from '../../../shared/forms';
import { SubmitButton } from '../../../shared/submit-button';

export interface CreatedTeacher {
  teacher: TeacherSummary;
  oneTimePassword: string;
}

/** The "Add teacher" pop-up. Closes with the new teacher and the one-time password to hand out. */
@Component({
  selector: 'app-teacher-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    SubmitButton,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    SearchSelect,
    TranslatePipe,
  ],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ 'teachers.add' | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <div class="form-grid">
          <mat-form-field>
            <mat-label>{{ 'person.first_name' | t }}</mat-label>
            <input matInput formControlName="firstName" autocomplete="off" maxlength="60" required />
            <mat-error>{{ key('firstName') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'person.last_name' | t }}</mat-label>
            <input matInput formControlName="lastName" autocomplete="off" maxlength="60" required />
            <mat-error>{{ key('lastName') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field class="full">
            <mat-label>{{ 'login.phone' | t }}</mat-label>
            <input matInput type="tel" inputmode="tel" formControlName="phone" placeholder="077 123 4567" required />
            <mat-hint>{{ 'person.phone_hint' | t }}</mat-hint>
            <mat-error>{{ key('phone') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field class="full">
            <mat-label>{{ 'profile.email' | t }}</mat-label>
            <input matInput type="email" inputmode="email" formControlName="email" />
            <mat-error>{{ key('email') | t }}</mat-error>
          </mat-form-field>
          <app-search-select formControlName="district" [label]="'person.district' | t" [options]="catalog.districtOptions()" required />
          <app-search-select formControlName="streamId" [label]="'person.stream' | t" [options]="catalog.streamOptions()" required />
          <app-search-select class="full" formControlName="subjectIds" [label]="'person.subjects' | t" [options]="catalog.subjectOptions()" multiple required />
        </div>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button label="teachers.create" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
  `,
})
export class TeacherDialog {
  private readonly api = inject(AdminService);
  private readonly ref = inject<MatDialogRef<TeacherDialog, CreatedTeacher>>(MatDialogRef);
  protected readonly catalog = inject(CatalogStore);

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    firstName: ['', [filled, Validators.maxLength(60)]],
    lastName: ['', [filled, Validators.maxLength(60)]],
    phone: ['', [filled, phoneValidator]],
    email: ['', [Validators.email, Validators.maxLength(120)]],
    district: ['', Validators.required],
    streamId: ['', Validators.required],
    subjectIds: [[] as string[], nonEmptyArray],
  });

  constructor() {
    this.catalog.ensureLoaded();
  }

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    const value = this.form.getRawValue();
    this.api.createTeacher({ ...value, email: value.email.trim() || undefined }).subscribe({
      next: (created) => this.ref.close(created),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}
