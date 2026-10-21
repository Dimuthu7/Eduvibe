import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { SearchSelect, SelectOption } from '../../shared/search-select';

export interface MoveDialogData {
  /** The file or folder being moved, shown in the heading. */
  name: string;
  /** Every place it can go: a shelf or a folder, named by where it sits. */
  options: SelectOption[];
}

/** Choose where to move a file or folder. Closes with the option value picked; the page does the move. */
@Component({
  selector: 'app-move-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SearchSelect, MatButtonModule, MatDialogModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'materials.move_title' | t }}</h2>
    <mat-dialog-content>
      <p class="name">{{ data.name }}</p>
      <app-search-select [formControl]="to" [label]="'materials.move_to' | t" [options]="data.options" [required]="true" />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
      <button mat-flat-button type="button" [disabled]="to.invalid" (click)="ref.close(to.value)">{{ 'materials.move' | t }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    .name { margin: 0 0 1rem; overflow-wrap: anywhere; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class MoveDialog {
  protected readonly data = inject<MoveDialogData>(MAT_DIALOG_DATA);
  protected readonly ref = inject<MatDialogRef<MoveDialog, string>>(MatDialogRef);
  protected readonly to = new FormControl('', { nonNullable: true, validators: [Validators.required] });
}
