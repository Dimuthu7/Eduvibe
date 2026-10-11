import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ClassesService } from '../../core/api/classes.service';
import { errorCode, inlineError } from '../../core/api/problem';
import { FEE_PLANS, FeePlan, Student, StudentCreated, StudentDetails, StudentsService } from '../../core/api/students.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { applyServerError, fieldErrorKey, filled, phoneValidator } from '../../shared/forms';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { SubmitButton } from '../../shared/submit-button';

export interface StudentDialogData {
  /** Edit this student's details. Without it the dialog adds a new student. */
  student?: Student;
  /** Class to start with when adding. */
  classId?: string;
}

/** A phone number that already belongs to a student: the server tells us just enough to offer enrolling them. */
interface Existing {
  firstName: string;
  lastInitial: string;
  enrolledHere: boolean;
}

/** Add a student to a class, or edit a student's details. Closes with the new student's row and one-time password, or with the edited student. */
@Component({
  selector: 'app-student-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, SearchSelect, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ (editing ? 'students.edit' : 'students.add') | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        @if (existing(); as found) {
          <div class="banner info" role="alert">
            @if (found.enrolledHere) {
              <p>{{ 'students.exists_here' | t }}</p>
            } @else {
              <p>{{ 'students.exists' | t }} <strong>{{ found.firstName }} {{ found.lastInitial }}.</strong></p>
              <button mat-flat-button type="button" [disabled]="busy()" (click)="enrolExisting()">{{ 'students.enrol_existing' | t }}</button>
            }
          </div>
        }
        <div class="form-grid">
          @if (!editing) {
            <app-search-select class="full" formControlName="classId" [label]="'students.class' | t" [options]="classOptions()" required />
          }
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
          @if (!editing) {
            <mat-form-field class="full">
              <mat-label>{{ 'students.phone' | t }}</mat-label>
              <input matInput type="tel" inputmode="tel" formControlName="phone" autocomplete="off" required />
              <mat-hint>{{ 'students.phone_hint' | t }}</mat-hint>
              <mat-error>{{ key('phone') | t }}</mat-error>
            </mat-form-field>
          }
          <mat-form-field>
            <mat-label>{{ 'students.school' | t }}</mat-label>
            <input matInput formControlName="school" autocomplete="off" maxlength="120" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'students.grade' | t }}</mat-label>
            <input matInput formControlName="grade" autocomplete="off" maxlength="30" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'students.guardian' | t }}</mat-label>
            <input matInput formControlName="guardianName" autocomplete="off" maxlength="120" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'students.guardian_phone' | t }}</mat-label>
            <input matInput type="tel" inputmode="tel" formControlName="guardianPhone" autocomplete="off" />
            <mat-error>{{ key('guardianPhone') | t }}</mat-error>
          </mat-form-field>
          @if (!editing) {
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
            <mat-form-field>
              <mat-label>{{ 'students.index' | t }}</mat-label>
              <input matInput type="number" inputmode="numeric" min="1" formControlName="indexNumber" />
              <mat-hint>{{ 'students.index_hint' | t }}</mat-hint>
              <mat-error>{{ key('indexNumber') | t }}</mat-error>
            </mat-form-field>
          }
        </div>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button [label]="editing ? 'common.save' : 'students.add_submit'" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .banner.info { background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); }
    .banner p { margin: 0 0 0.5rem; }
    .banner p:last-child { margin: 0; }
  `,
})
export class StudentDialog {
  private readonly api = inject(StudentsService);
  private readonly classesApi = inject(ClassesService);
  private readonly ref = inject<MatDialogRef<StudentDialog, StudentCreated | Student>>(MatDialogRef);
  private readonly data = inject<StudentDialogData | null>(MAT_DIALOG_DATA, { optional: true });

  protected readonly plans: FeePlan[] = FEE_PLANS;
  protected readonly student = this.data?.student ?? null;
  protected readonly editing = this.student !== null;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly existing = signal<Existing | null>(null);
  private readonly classes = signal<SelectOption[]>([]);
  protected readonly classOptions = computed(() => this.classes());

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    classId: [this.data?.classId ?? '', this.editing ? [] : [Validators.required]],
    firstName: [this.student?.firstName ?? '', [filled, Validators.maxLength(60)]],
    lastName: [this.student?.lastName ?? '', [filled, Validators.maxLength(60)]],
    phone: ['', this.editing ? [] : [filled, phoneValidator]],
    school: [this.student?.school ?? '', Validators.maxLength(120)],
    grade: [this.student?.grade ?? '', Validators.maxLength(30)],
    guardianName: [this.student?.guardianName ?? '', Validators.maxLength(120)],
    guardianPhone: [this.student?.guardianPhone ?? '', phoneValidator],
    feePlan: ['Full' as FeePlan],
    discountAmount: this.fb.control<number | null>(null),
    indexNumber: this.fb.control<number | null>(null),
  });

  constructor() {
    if (!this.editing) {
      this.classesApi.classes('active').subscribe((list) => this.classes.set(list.map((c) => ({ value: c.id, label: c.title }))));
    }
    // A fresh phone number or class means the earlier "already exists" answer no longer applies.
    this.form.controls.phone.valueChanges.subscribe(() => this.existing.set(null));
    this.form.controls.classId.valueChanges.subscribe(() => this.existing.set(null));
  }

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  private details(): StudentDetails {
    const v = this.form.getRawValue();
    return { firstName: v.firstName.trim(), lastName: v.lastName.trim(), school: v.school, grade: v.grade, guardianName: v.guardianName, guardianPhone: v.guardianPhone };
  }

  protected submit(enrolExisting = false): void {
    this.form.markAllAsTouched();
    if (this.form.controls.feePlan.value === 'Discount') this.form.controls.discountAmount.addValidators(Validators.required);
    else this.form.controls.discountAmount.clearValidators();
    this.form.controls.discountAmount.updateValueAndValidity({ emitEvent: false });
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    const fail = (e: unknown) => {
      this.busy.set(false);
      if (e instanceof HttpErrorResponse && errorCode(e) === 'student_exists') {
        this.existing.set((e.error as { existing: Existing }).existing);
      } else if (!applyServerError(this.form, errorCode(e))) {
        this.error.set(inlineError(e));
      }
    };

    if (this.student) {
      this.api.update(this.student.id, this.details()).subscribe({
        next: () => this.ref.close({ ...this.student!, ...this.details(), fullName: `${this.details().firstName} ${this.details().lastName}` }),
        error: fail,
      });
      return;
    }

    const v = this.form.getRawValue();
    this.api
      .add({
        ...this.details(),
        phone: v.phone.trim(),
        classId: v.classId,
        feePlan: v.feePlan,
        discountAmount: v.feePlan === 'Discount' ? v.discountAmount : null,
        indexNumber: v.indexNumber,
        enrolExisting,
      })
      .subscribe({ next: (created) => this.ref.close(created), error: fail });
  }

  protected enrolExisting(): void {
    this.submit(true);
  }
}
