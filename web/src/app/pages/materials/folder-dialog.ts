import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { errorCode, inlineError } from '../../core/api/problem';
import { FOLDER_MAX_NAME, MaterialFolder, MaterialKind, MaterialsService } from '../../core/api/materials.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { applyServerError, fieldErrorKey, filled } from '../../shared/forms';
import { SubmitButton } from '../../shared/submit-button';

export interface FolderDialogData {
  /** The folder being renamed; missing when making a new one. */
  folder?: MaterialFolder;
  classId: string;
  kind: MaterialKind;
  /** Where a new folder goes: a folder, or null for the shelf itself. */
  parentId: string | null;
}

/** Make a folder inside a shelf or another folder, or rename one. Closes with the saved folder. */
@Component({
  selector: 'app-folder-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ (data.folder ? 'materials.folder_rename' : 'materials.folder_new') | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <mat-form-field class="field">
          <mat-label>{{ 'materials.folder_name' | t }}</mat-label>
          <input matInput formControlName="name" [maxlength]="maxName" autocomplete="off" required />
          <mat-hint>{{ 'materials.folder_name_hint' | t }}</mat-hint>
          <mat-error>{{ key() | t }}</mat-error>
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button [label]="data.folder ? 'common.save' : 'materials.folder_create'" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .field { width: 100%; }
  `,
})
export class FolderDialog {
  private readonly api = inject(MaterialsService);
  private readonly ref = inject<MatDialogRef<FolderDialog, MaterialFolder>>(MatDialogRef);
  protected readonly data = inject<FolderDialogData>(MAT_DIALOG_DATA);

  protected readonly maxName = FOLDER_MAX_NAME;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: [this.data.folder?.name ?? '', [filled, Validators.maxLength(FOLDER_MAX_NAME)]],
  });

  protected key(): string {
    const control = this.form.controls.name;
    return control.touched || control.dirty ? fieldErrorKey(control) : '';
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    const name = this.form.getRawValue().name.trim();
    const request = this.data.folder
      ? this.api.renameFolder(this.data.folder.id, name)
      : this.api.createFolder({ classId: this.data.classId, kind: this.data.kind, parentId: this.data.parentId, name });
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}
