import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { AttendanceService, ClassSession } from '../../core/api/attendance.service';
import { ClassesService } from '../../core/api/classes.service';
import { errorCode, inlineError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { applyServerError, fieldErrorKey } from '../../shared/forms';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { SubmitButton } from '../../shared/submit-button';

/** Add a class that is not on the weekly timetable, such as a revision session, so its attendance can be taken. */
@Component({
  selector: 'app-extra-session-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SearchSelect, SubmitButton, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ 'attendance.extra_title' | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <div class="form-grid">
          <app-search-select formControlName="classId" [label]="'attendance.class' | t" [options]="classOptions()" [required]="true" />
          <mat-form-field>
            <mat-label>{{ 'attendance.date' | t }}</mat-label>
            <input matInput type="date" formControlName="date" required />
            <mat-error>{{ key('date') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'attendance.start' | t }}</mat-label>
            <input matInput type="time" formControlName="start" required />
            <mat-error>{{ key('start') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'attendance.end' | t }}</mat-label>
            <input matInput type="time" formControlName="end" required />
            <mat-error>{{ key('end') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field class="wide">
            <mat-label>{{ 'attendance.note' | t }}</mat-label>
            <input matInput formControlName="note" maxlength="200" autocomplete="off" />
            <mat-hint>{{ 'attendance.note_hint' | t }}</mat-hint>
            <mat-error>{{ key('note') | t }}</mat-error>
          </mat-form-field>
        </div>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button label="attendance.extra_submit" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .wide { grid-column: 1 / -1; }
  `,
})
export class ExtraSessionDialog {
  private readonly api = inject(AttendanceService);
  private readonly ref = inject<MatDialogRef<ExtraSessionDialog, ClassSession>>(MatDialogRef);
  private readonly data = inject<{ date: string; classId?: string }>(MAT_DIALOG_DATA);

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly classOptions = signal<SelectOption[]>([]);
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    classId: [this.data.classId ?? '', [Validators.required]],
    date: [this.data.date, [Validators.required]],
    start: ['', [Validators.required]],
    end: ['', [Validators.required]],
    note: ['', [Validators.maxLength(200)]],
  });

  constructor() {
    inject(ClassesService)
      .classes('active')
      .subscribe((list) => this.classOptions.set(list.map((c) => ({ value: c.id, label: c.title }))));
  }

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    if (this.form.controls.end.value <= this.form.controls.start.value) {
      applyServerError(this.form, 'time_range_invalid');
      return;
    }

    this.busy.set(true);
    this.error.set('');
    const { classId, date, start, end, note } = this.form.getRawValue();
    this.api.addExtra({ classId, date, start, end, note: note.trim() || null }).subscribe({
      next: (session) => this.ref.close(session),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}
