import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AbstractControl, FormArray, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { AdminService } from '../../core/api/admin.service';
import { CatalogStore } from '../../core/api/catalog.service';
import { ClassInput, ClassesService, MEDIUMS, TuitionClass } from '../../core/api/classes.service';
import { errorCode, inlineError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { applyServerError, fieldErrorKey, filled } from '../../shared/forms';
import { minutes, toInputTime } from '../../shared/format';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { SubmitButton } from '../../shared/submit-button';

const DAYS = [1, 2, 3, 4, 5, 6, 7];

/** A slot ends after it starts. */
const slotOrder = (slot: AbstractControl): ValidationErrors | null => {
  const { start, end } = slot.value as { start: string; end: string };
  return start && end && minutes(end) <= minutes(start) ? { slotOrder: true } : null;
};

/** No two slots on the same day overlap. */
const slotsOverlap = (array: AbstractControl): ValidationErrors | null => {
  const slots = (array.value as { day: number; start: string; end: string }[]).filter((s) => s.start && s.end);
  const clash = slots.some((a, i) =>
    slots.some((b, j) => i !== j && a.day === b.day && minutes(a.start) < minutes(b.end) && minutes(b.start) < minutes(a.end)),
  );
  return clash ? { slotsOverlap: true } : null;
};

/** Add or edit a class: details, where it is held and its weekly times. Closes with the saved class. */
@Component({
  selector: 'app-class-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    SubmitButton,
    SearchSelect,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    TranslatePipe,
  ],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ (existing ? 'classes.edit' : 'classes.add') | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        @if (placesLoaded() && placeOptions().length === 0) {
          <p class="banner info" role="status">{{ 'classes.no_places' | t }}</p>
        }
        <div class="form-grid">
          <mat-form-field class="full">
            <mat-label>{{ 'classes.name' | t }}</mat-label>
            <input matInput formControlName="title" autocomplete="off" maxlength="120" required />
            <mat-hint>{{ 'classes.title_hint' | t }}</mat-hint>
            <mat-error>{{ key('title') | t }}</mat-error>
          </mat-form-field>
          <app-search-select formControlName="subjectId" [label]="'classes.subject' | t" [options]="catalog.subjectOptions()" required />
          <app-search-select formControlName="streamId" [label]="'classes.stream' | t" [options]="catalog.streamOptions()" required />
          <mat-form-field>
            <mat-label>{{ 'classes.exam_year' | t }}</mat-label>
            <input matInput type="number" inputmode="numeric" formControlName="examYear" required />
            <mat-error>{{ key('examYear') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'classes.medium' | t }}</mat-label>
            <mat-select formControlName="medium" required>
              @for (medium of mediums; track medium) {
                <mat-option [value]="medium">{{ 'medium.' + medium | t }}</mat-option>
              }
            </mat-select>
            <mat-error>{{ key('medium') | t }}</mat-error>
          </mat-form-field>
          <app-search-select class="full" formControlName="place" [label]="'classes.place' | t" [options]="placeOptions()" required />
          <mat-form-field class="full">
            <mat-label>{{ 'classes.fee' | t }}</mat-label>
            <input matInput type="number" inputmode="decimal" min="0" formControlName="monthlyFee" required />
            <mat-error>{{ key('monthlyFee') | t }}</mat-error>
          </mat-form-field>
        </div>

        <h3 class="section-title slots-title">{{ 'classes.slots' | t }}</h3>
        <div formArrayName="slots" class="slots">
          @for (slot of slots.controls; track slot; let i = $index) {
            <div class="slot" [formGroupName]="i">
              <mat-form-field class="day">
                <mat-label>{{ 'classes.day' | t }}</mat-label>
                <mat-select formControlName="day">
                  @for (day of days; track day) {
                    <mat-option [value]="day">{{ 'day.' + day | t }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>
              <mat-form-field>
                <mat-label>{{ 'classes.start' | t }}</mat-label>
                <input matInput type="time" formControlName="start" required />
                <mat-error>{{ slotKey(i, 'start') | t }}</mat-error>
              </mat-form-field>
              <mat-form-field>
                <mat-label>{{ 'classes.end' | t }}</mat-label>
                <input matInput type="time" formControlName="end" required />
                <mat-error>{{ slotKey(i, 'end') | t }}</mat-error>
              </mat-form-field>
              <button mat-icon-button type="button" class="remove" [disabled]="slots.length === 1" (click)="removeSlot(i)" [attr.aria-label]="'classes.remove_slot' | t">
                <mat-icon>delete</mat-icon>
              </button>
            </div>
          }
        </div>
        @if (slotsMessage()) {
          <p class="field-error" role="alert">{{ slotsMessage() | t }}</p>
        }
        <button mat-stroked-button type="button" (click)="addSlot()">
          <mat-icon>add</mat-icon>
          {{ 'classes.add_slot' | t }}
        </button>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button label="common.save" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .banner.info { background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); }
    .slots-title { margin-top: 0.5rem; }
    .slot { display: grid; grid-template-columns: 1fr 1fr; column-gap: 0.75rem; align-items: start; }
    .slot .day { grid-column: 1 / -1; }
    .slot .remove { grid-column: 1 / -1; justify-self: end; margin-top: -1rem; }
    @media (min-width: 560px) {
      .slot { grid-template-columns: 1.4fr 1fr 1fr auto; }
      .slot .day { grid-column: auto; }
      .slot .remove { grid-column: auto; margin-top: 0.5rem; }
    }
  `,
})
export class ClassDialog {
  private readonly api = inject(ClassesService);
  private readonly admin = inject(AdminService);
  private readonly i18n = inject(TranslateService);
  private readonly ref = inject<MatDialogRef<ClassDialog, TuitionClass>>(MatDialogRef);
  protected readonly existing = inject<TuitionClass | null>(MAT_DIALOG_DATA, { optional: true });
  protected readonly catalog = inject(CatalogStore);

  protected readonly mediums = MEDIUMS;
  protected readonly days = DAYS;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly placesLoaded = signal(false);
  private readonly places = signal<SelectOption[]>([]);

  /** The teacher's institutes and venues, plus the class's current place even if it has since been hidden. */
  protected readonly placeOptions = computed(() => {
    const options = this.places();
    const current = this.existing;
    if (!current) return options;
    const value = this.placeValue(current.placeType === 'institute' ? 'i' : 'v', current.placeId);
    return options.some((o) => o.value === value) ? options : [...options, { value, label: current.placeName }];
  });

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly slots = this.fb.array([] as ReturnType<ClassDialog['newSlot']>[], [slotsOverlap]);
  protected readonly form = this.fb.group({
    title: [this.existing?.title ?? '', [filled, Validators.maxLength(120)]],
    subjectId: [this.existing?.subjectId ?? '', Validators.required],
    streamId: [this.existing?.streamId ?? '', Validators.required],
    examYear: this.fb.control<number | null>(this.existing?.examYear ?? new Date().getFullYear() + 1, [Validators.required, Validators.min(2000), Validators.max(2100)]),
    medium: [this.existing?.medium ?? '', Validators.required],
    place: [this.existing ? this.placeValue(this.existing.placeType === 'institute' ? 'i' : 'v', this.existing.placeId) : '', Validators.required],
    monthlyFee: this.fb.control<number | null>(this.existing?.monthlyFee ?? null, [Validators.required, Validators.min(0)]),
    slots: this.slots,
  });

  constructor() {
    this.catalog.ensureLoaded();
    const initial = this.existing?.slots.length ? this.existing.slots : [{ day: 1, start: '', end: '' }];
    for (const slot of initial) this.slots.push(this.newSlot(slot.day, toInputTime(slot.start), toInputTime(slot.end)));

    forkJoin({ institutes: this.admin.myInstitutes(), venues: this.api.venues() }).subscribe({
      next: ({ institutes, venues }) => {
        const institute = this.i18n.translate('classes.place_institute');
        const venue = this.i18n.translate('classes.place_venue');
        this.places.set([
          ...institutes.map((i) => ({ value: this.placeValue('i', i.id), label: `${i.name} (${institute})` })),
          ...venues.filter((v) => v.isActive).map((v) => ({ value: this.placeValue('v', v.id), label: `${v.name} (${venue})` })),
        ]);
        this.placesLoaded.set(true);
      },
    });
  }

  protected newSlot(day: number, start: string, end: string) {
    return this.fb.group({ day: [day], start: [start, Validators.required], end: [end, Validators.required] }, { validators: slotOrder });
  }

  protected addSlot(): void {
    const last = this.slots.at(this.slots.length - 1)?.getRawValue();
    this.slots.push(this.newSlot(last?.day ?? 1, '', ''));
  }

  protected removeSlot(index: number): void {
    this.slots.removeAt(index);
  }

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected slotKey(index: number, name: 'start' | 'end'): string {
    const slot = this.slots.at(index);
    const control = slot.controls[name];
    if (!control.touched) return '';
    if (control.errors) return fieldErrorKey(control);
    return name === 'end' && slot.hasError('slotOrder') ? 'validation.slot_order' : '';
  }

  protected slotsMessage(): string {
    return this.slots.touched && this.slots.hasError('slotsOverlap') ? 'validation.slots_overlap' : '';
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    const v = this.form.getRawValue();
    const input: ClassInput = {
      title: v.title.trim(),
      subjectId: v.subjectId,
      streamId: v.streamId,
      examYear: v.examYear!,
      medium: v.medium,
      instituteId: v.place.startsWith('i:') ? v.place.slice(2) : null,
      venueId: v.place.startsWith('v:') ? v.place.slice(2) : null,
      monthlyFee: v.monthlyFee!,
      slots: v.slots.map((s) => ({ day: s.day, start: s.start, end: s.end })),
    };
    const request = this.existing ? this.api.updateClass(this.existing.id, input) : this.api.createClass(input);
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }

  private placeValue(kind: 'i' | 'v', id: string): string {
    return `${kind}:${id}`;
  }
}
