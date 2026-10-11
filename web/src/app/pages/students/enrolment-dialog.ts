import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ClassesService } from '../../core/api/classes.service';
import { errorCode, inlineError } from '../../core/api/problem';
import { EnrolmentStatus, FEE_PLANS, FeePlan, StudentEnrolment, StudentRow, StudentsService } from '../../core/api/students.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { applyServerError, fieldErrorKey } from '../../shared/forms';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { SubmitButton } from '../../shared/submit-button';

export interface EnrolmentDialogData {
  studentId: string;
  /** Change this enrolment. Without it the dialog enrols the student in another class. */
  enrolment?: StudentEnrolment;
  /** Classes the student is already in, so they are not offered again. */
  takenClassIds?: string[];
}

const STATUSES: EnrolmentStatus[] = ['Active', 'Left'];

/** Enrol an existing student in another class, or change their index number, fee plan and status in one class. */
@Component({
  selector: 'app-enrolment-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, SearchSelect, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ (enrolment ? 'students.edit_enrolment' : 'students.enrol_another') | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <div class="form-grid">
          @if (enrolment) {
            <p class="full class-name">{{ enrolment.classTitle }}</p>
          } @else {
            <app-search-select class="full" formControlName="classId" [label]="'students.class' | t" [options]="classOptions()" required />
          }
          <mat-form-field>
            <mat-label>{{ 'students.index' | t }}</mat-label>
            <input matInput type="number" inputmode="numeric" min="1" formControlName="indexNumber" />
            @if (!enrolment) {
              <mat-hint>{{ 'students.index_hint' | t }}</mat-hint>
            }
            <mat-error>{{ key('indexNumber') | t }}</mat-error>
          </mat-form-field>
          @if (enrolment && enrolment.status !== 'Pending') {
            <mat-form-field>
              <mat-label>{{ 'students.status' | t }}</mat-label>
              <mat-select formControlName="status">
                @for (status of statuses; track status) {
                  <mat-option [value]="status">{{ 'students.status.' + status | t }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          }
          <mat-form-field>
            <mat-label>{{ 'students.fee_plan' | t }}</mat-label>
            <mat-select formControlName="feePlan">
              @for (plan of plans; track plan) {
                <mat-option [value]="plan">{{ 'students.plan.' + plan | t }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          @if (form.controls.feePlan.value === 'Discount') {
            <mat-form-field>
              <mat-label>{{ 'students.discount' | t }}</mat-label>
              <input matInput type="number" inputmode="decimal" min="1" formControlName="discountAmount" required />
              <mat-hint>{{ 'students.discount_hint' | t }}</mat-hint>
              <mat-error>{{ key('discountAmount') | t }}</mat-error>
            </mat-form-field>
          }
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
    .class-name { margin: 0 0 1rem; font: var(--mat-sys-title-medium); }
  `,
})
export class EnrolmentDialog {
  private readonly api = inject(StudentsService);
  private readonly classesApi = inject(ClassesService);
  private readonly ref = inject<MatDialogRef<EnrolmentDialog, StudentRow>>(MatDialogRef);
  private readonly data = inject<EnrolmentDialogData>(MAT_DIALOG_DATA);

  protected readonly enrolment = this.data.enrolment ?? null;
  protected readonly plans = FEE_PLANS;
  protected readonly statuses = STATUSES;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly classOptions = signal<SelectOption[]>([]);

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    classId: [this.enrolment?.classId ?? '', this.enrolment ? [] : [Validators.required]],
    indexNumber: this.fb.control<number | null>(this.enrolment?.indexNumber ?? null),
    status: [(this.enrolment?.status ?? 'Active') as EnrolmentStatus],
    feePlan: [(this.enrolment?.feePlan ?? 'Full') as FeePlan],
    discountAmount: this.fb.control<number | null>(this.enrolment?.discountAmount || null),
  });

  constructor() {
    if (!this.enrolment) {
      const taken = new Set(this.data.takenClassIds ?? []);
      this.classesApi
        .classes('active')
        .subscribe((list) => this.classOptions.set(list.filter((c) => !taken.has(c.id)).map((c) => ({ value: c.id, label: c.title }))));
    }
  }

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected submit(): void {
    const discount = this.form.controls.discountAmount;
    if (this.form.controls.feePlan.value === 'Discount') discount.addValidators(Validators.required);
    else discount.clearValidators();
    discount.updateValueAndValidity({ emitEvent: false });
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    const v = this.form.getRawValue();
    const discountAmount = v.feePlan === 'Discount' ? v.discountAmount : null;
    const request = this.enrolment
      ? this.api.updateEnrolment(this.enrolment.id, { indexNumber: v.indexNumber, status: v.status, feePlan: v.feePlan, discountAmount })
      : this.api.enrol(this.data.studentId, { classId: v.classId, indexNumber: v.indexNumber, feePlan: v.feePlan, discountAmount });
    request.subscribe({
      next: (row) => this.ref.close(row),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}
