import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { SearchSelect } from '../../../shared/search-select';
import { AdminService, Institute } from '../../../core/api/admin.service';
import { CatalogStore } from '../../../core/api/catalog.service';
import { errorCode, inlineError } from '../../../core/api/problem';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { applyServerError, fieldErrorKey, filled, phoneValidator } from '../../../shared/forms';
import { SubmitButton } from '../../../shared/submit-button';

/** The pop-up to add an institute, or edit one when it is given the institute. Closes with the saved institute. */
@Component({
  selector: 'app-institute-dialog',
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
      <h2 mat-dialog-title>{{ (existing ? 'institutes.edit' : 'institutes.add') | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <div class="form-grid">
          <mat-form-field class="full">
            <mat-label>{{ 'institutes.name' | t }}</mat-label>
            <input matInput formControlName="name" autocomplete="off" maxlength="120" required />
            <mat-error>{{ key('name') | t }}</mat-error>
          </mat-form-field>
          <app-search-select formControlName="district" [label]="'person.district' | t" [options]="catalog.districtOptions()" required />
          <mat-form-field>
            <mat-label>{{ 'profile.town' | t }}</mat-label>
            <input matInput formControlName="town" maxlength="80" />
            <mat-error>{{ key('town') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field class="full">
            <mat-label>{{ 'institutes.address' | t }}</mat-label>
            <input matInput formControlName="address" maxlength="250" />
            <mat-error>{{ key('address') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field class="full">
            <mat-label>{{ 'login.phone' | t }}</mat-label>
            <input matInput type="tel" inputmode="tel" formControlName="phone" placeholder="011 234 5678" />
            <mat-error>{{ key('phone') | t }}</mat-error>
          </mat-form-field>
        </div>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button label="common.save" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
  `,
})
export class InstituteDialog {
  private readonly api = inject(AdminService);
  private readonly ref = inject<MatDialogRef<InstituteDialog, Institute>>(MatDialogRef);
  protected readonly existing = inject<Institute | null>(MAT_DIALOG_DATA, { optional: true });
  protected readonly catalog = inject(CatalogStore);

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: [this.existing?.name ?? '', [filled, Validators.maxLength(120)]],
    district: [this.existing?.district ?? '', Validators.required],
    town: [this.existing?.town ?? '', Validators.maxLength(80)],
    address: [this.existing?.address ?? '', Validators.maxLength(250)],
    phone: [this.existing?.phone ?? '', phoneValidator],
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
    const input = this.form.getRawValue();
    const request = this.existing ? this.api.updateInstitute(this.existing.id, input) : this.api.createInstitute(input);
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}
