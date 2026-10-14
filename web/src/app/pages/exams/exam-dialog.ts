import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ClassesService } from '../../core/api/classes.service';
import { ExamDetail, ExamsService } from '../../core/api/exams.service';
import { errorCode, inlineError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { applyServerError, fieldErrorKey, filled } from '../../shared/forms';
import { todayColombo } from '../../shared/format';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { SubmitButton } from '../../shared/submit-button';

const MAX_PARTS = 10;

export interface ExamDialogData {
  /** A class to start on when the dialog opens from one class's screen. */
  classId?: string;
  /** The exam being changed; missing when making a new one. */
  exam?: ExamDetail;
}

/** Make or change an exam: name, class, date and its parts (for example MCQ out of 40 and an essay out of 60). */
@Component({
  selector: 'app-exam-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SearchSelect, SubmitButton, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ (editing ? 'exams.edit_title' : 'exams.new_title') | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <div class="form-grid">
          @if (!editing) {
            <app-search-select class="wide" formControlName="classId" [label]="'attendance.class' | t" [options]="classOptions()" [required]="true" />
          }
          <mat-form-field class="wide">
            <mat-label>{{ 'exams.name' | t }}</mat-label>
            <input matInput formControlName="name" maxlength="100" autocomplete="off" required />
            <mat-hint>{{ 'exams.name_hint' | t }}</mat-hint>
            <mat-error>{{ key('name') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field class="wide">
            <mat-label>{{ 'exams.held_on' | t }}</mat-label>
            <input matInput type="date" formControlName="heldOn" required />
            <mat-error>{{ key('heldOn') | t }}</mat-error>
          </mat-form-field>
        </div>

        <h3 class="parts-title">{{ 'exams.parts' | t }}</h3>
        <p class="muted parts-hint">{{ (locked ? 'exams.parts_locked' : 'exams.parts_hint') | t }}</p>
        <div formArrayName="parts" class="parts">
          @for (part of parts.controls; track part; let i = $index) {
            <div class="part" [formGroupName]="i">
              <mat-form-field class="part-name">
                <mat-label>{{ 'exams.part_name' | t }}</mat-label>
                <input matInput formControlName="name" maxlength="50" autocomplete="off" required />
                <mat-error>{{ partKey(i, 'name') | t }}</mat-error>
              </mat-form-field>
              <mat-form-field class="part-max">
                <mat-label>{{ 'exams.part_max' | t }}</mat-label>
                <input matInput type="number" inputmode="decimal" formControlName="maxMark" min="0.01" max="1000" step="any" required />
                <mat-error>{{ partKey(i, 'maxMark') | t }}</mat-error>
              </mat-form-field>
              @if (!locked && parts.length > 1) {
                <button mat-icon-button type="button" class="remove" (click)="removePart(i)" [attr.aria-label]="'common.remove' | t">
                  <mat-icon>close</mat-icon>
                </button>
              }
            </div>
          }
        </div>
        @if (!locked && parts.length < maxParts) {
          <button mat-button type="button" (click)="addPart()">
            <mat-icon>add</mat-icon>
            {{ 'exams.add_part' | t }}
          </button>
        }
        <p class="total">{{ 'exams.total_marks' | t }}: <strong>{{ total() }}</strong></p>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button [label]="editing ? 'common.save' : 'exams.create'" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .wide { grid-column: 1 / -1; }
    .parts-title { margin: 0.5rem 0 0; font: var(--mat-sys-title-medium); }
    .parts-hint { margin: 0.25rem 0 1rem; }
    .part { display: flex; gap: 0.75rem; align-items: flex-start; }
    .part-name { flex: 2 1 0; min-width: 0; }
    .part-max { flex: 1 1 0; min-width: 0; }
    .remove { flex: none; margin-top: 0.25rem; }
    .total { margin: 0.5rem 0 0; color: var(--mat-sys-on-surface-variant); }
    .total strong { color: var(--mat-sys-on-surface); }
  `,
})
export class ExamDialog {
  private readonly api = inject(ExamsService);
  private readonly ref = inject<MatDialogRef<ExamDialog, ExamDetail>>(MatDialogRef);
  private readonly data = inject<ExamDialogData | null>(MAT_DIALOG_DATA);
  private readonly i18n = inject(TranslateService);

  protected readonly maxParts = MAX_PARTS;
  protected readonly editing = !!this.data?.exam;
  /** Once marks exist the parts stay as they are (names and maximums can still change), so no mark loses its part. */
  protected readonly locked = !!this.data?.exam?.students.some((s) => s.absent || Object.keys(s.marks).length > 0);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly classOptions = signal<SelectOption[]>([]);
  private readonly fb = inject(FormBuilder).nonNullable;

  protected readonly form = this.fb.group({
    classId: [this.data?.classId ?? '', this.editing ? [] : [Validators.required]],
    name: [this.data?.exam?.name ?? '', [filled, Validators.maxLength(100)]],
    heldOn: [this.data?.exam?.heldOn ?? todayColombo(), [Validators.required]],
    parts: this.fb.array(
      (this.data?.exam?.parts ?? [{ id: '', name: this.i18n.translate('exams.default_part'), maxMark: 100 }]).map((p) => this.partGroup(p.id, p.name, p.maxMark)),
    ),
  });

  protected get parts(): FormArray {
    return this.form.controls.parts;
  }

  constructor() {
    if (!this.editing) {
      inject(ClassesService)
        .classes('active')
        .subscribe((list) => this.classOptions.set(list.map((c) => ({ value: c.id, label: c.title }))));
    }
  }

  protected total(): number {
    const sum = this.parts.controls.reduce((acc, g) => acc + (Number(g.get('maxMark')?.value) || 0), 0);
    return Math.round(sum * 100) / 100;
  }

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected partKey(index: number, name: string): string {
    const control = this.parts.at(index).get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected addPart(): void {
    const number = this.parts.length + 1;
    this.parts.push(this.partGroup('', `${this.i18n.translate('exams.part')} ${number}`, 100));
  }

  protected removePart(index: number): void {
    this.parts.removeAt(index);
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.busy.set(true);
    this.error.set('');
    const { classId, name, heldOn, parts } = this.form.getRawValue();
    const input = {
      classId,
      name: name.trim(),
      heldOn,
      parts: parts.map((p: { id: string; name: string; maxMark: number | string }) => ({
        id: p.id || undefined,
        name: p.name.trim(),
        maxMark: Number(p.maxMark),
      })),
    };
    const request = this.data?.exam ? this.api.update(this.data.exam.id, input) : this.api.create(input);
    request.subscribe({
      next: (exam) => this.ref.close(exam),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }

  private partGroup(id: string, name: string, maxMark: number) {
    return this.fb.group({
      id: [id],
      name: [name, [filled, Validators.maxLength(50)]],
      maxMark: [maxMark as number | string, [Validators.required, Validators.min(0.01), Validators.max(1000)]],
    });
  }
}

/** Opens the exam dialog. Emits the saved exam, or nothing when cancelled. */
export function openExamDialog(dialog: MatDialog, data: ExamDialogData = {}) {
  return dialog
    .open<ExamDialog, ExamDialogData, ExamDetail>(ExamDialog, { data, width: '40rem', maxWidth: 'calc(100vw - 1rem)' })
    .afterClosed();
}
