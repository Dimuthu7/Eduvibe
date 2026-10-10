import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { CatalogStore } from '../../core/api/catalog.service';
import { ClassesService, Venue } from '../../core/api/classes.service';
import { errorCode, inlineError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { applyServerError, fieldErrorKey, filled } from '../../shared/forms';
import { SearchSelect } from '../../shared/search-select';
import { SubmitButton } from '../../shared/submit-button';

/** Add or edit one of the teacher's private venues. Closes with the saved venue. */
@Component({
  selector: 'app-venue-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, SearchSelect, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ (existing ? 'venues.edit' : 'venues.add') | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <div class="form-grid">
          <mat-form-field class="full">
            <mat-label>{{ 'venues.name' | t }}</mat-label>
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
export class VenueDialog {
  private readonly api = inject(ClassesService);
  private readonly ref = inject<MatDialogRef<VenueDialog, Venue>>(MatDialogRef);
  protected readonly existing = inject<Venue | null>(MAT_DIALOG_DATA, { optional: true });
  protected readonly catalog = inject(CatalogStore);

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: [this.existing?.name ?? '', [filled, Validators.maxLength(120)]],
    district: [this.existing?.district ?? '', Validators.required],
    town: [this.existing?.town ?? '', Validators.maxLength(80)],
    address: [this.existing?.address ?? '', Validators.maxLength(250)],
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
    const request = this.existing ? this.api.updateVenue(this.existing.id, input) : this.api.createVenue(input);
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}
