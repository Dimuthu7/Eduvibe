import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { AdminService, Institute, TeacherSummary } from '../../../core/api/admin.service';
import { errorKey } from '../../../core/api/problem';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';

/** Super Admin: add institutes and choose which teachers work at each. */
@Component({
  selector: 'app-admin-institutes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButtonModule, MatCardModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <h1>{{ 'institutes.title' | t }}</h1>

    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ (editing() ? 'institutes.edit' : 'institutes.add') | t }}</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <form class="form" [formGroup]="form" (ngSubmit)="save()">
          <mat-form-field>
            <mat-label>{{ 'institutes.name' | t }}</mat-label>
            <input matInput formControlName="name" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'profile.town' | t }}</mat-label>
            <input matInput formControlName="town" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'institutes.address' | t }}</mat-label>
            <input matInput formControlName="address" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'login.phone' | t }}</mat-label>
            <input matInput type="tel" inputmode="tel" formControlName="phone" />
          </mat-form-field>
          @if (error()) {
            <p class="error" role="alert">{{ error() | t }}</p>
          }
          <div class="actions">
            <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">{{ 'institutes.save' | t }}</button>
            @if (editing()) {
              <button mat-button type="button" (click)="cancel()">{{ 'institutes.cancel' | t }}</button>
            }
          </div>
        </form>
      </mat-card-content>
    </mat-card>

    @for (institute of institutes(); track institute.id) {
      <mat-card appearance="outlined">
        <mat-card-content>
          <div class="head">
            <div>
              <strong>{{ institute.name }}</strong>
              <div class="muted">{{ institute.town }}</div>
            </div>
            <button mat-button type="button" (click)="edit(institute)">{{ 'institutes.edit' | t }}</button>
          </div>
          <h3>{{ 'institutes.teachers' | t }}</h3>
          @for (teacher of activeTeachers(); track teacher.id) {
            <mat-checkbox
              [checked]="institute.teacherIds.includes(teacher.id)"
              (change)="toggleTeacher(institute, teacher.id, $event.checked)"
            >
              {{ teacher.fullName }}
            </mat-checkbox>
          } @empty {
            <p class="muted">{{ 'teachers.none' | t }}</p>
          }
        </mat-card-content>
      </mat-card>
    } @empty {
      <p class="muted">{{ 'institutes.none' | t }}</p>
    }
  `,
  styles: `
    :host { display: block; max-width: 40rem; margin: 1.5rem auto; padding: 0 1rem; }
    mat-card { margin-bottom: 1rem; }
    mat-checkbox { display: block; }
    .form { display: flex; flex-direction: column; gap: 0.5rem; padding-top: 1rem; }
    .head { display: flex; justify-content: space-between; align-items: center; gap: 1rem; }
    .actions { display: flex; gap: 0.5rem; }
    .muted { opacity: 0.7; }
    .error { margin: 0; color: var(--mat-sys-error); }
  `,
})
export class AdminInstitutes {
  private readonly api = inject(AdminService);

  protected readonly institutes = signal<Institute[]>([]);
  private readonly teachers = signal<TeacherSummary[]>([]);
  protected readonly activeTeachers = computed(() => this.teachers().filter((t) => t.isActive));
  protected readonly editing = signal<Institute | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', Validators.required],
    town: [''],
    address: [''],
    phone: [''],
  });

  constructor() {
    forkJoin([this.api.institutes(), this.api.teachers()]).subscribe({
      next: ([institutes, teachers]) => {
        this.institutes.set(institutes);
        this.teachers.set(teachers);
      },
      error: (e) => this.error.set(errorKey(e)),
    });
  }

  protected edit(institute: Institute): void {
    this.editing.set(institute);
    this.form.setValue({
      name: institute.name,
      town: institute.town ?? '',
      address: institute.address ?? '',
      phone: institute.phone ?? '',
    });
  }

  protected cancel(): void {
    this.editing.set(null);
    this.form.reset();
  }

  protected save(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    const input = this.form.getRawValue();
    const current = this.editing();
    const request = current ? this.api.updateInstitute(current.id, input) : this.api.createInstitute(input);
    request.subscribe({
      next: (saved) => {
        this.institutes.update((rows) =>
          current ? rows.map((r) => (r.id === saved.id ? saved : r)) : [...rows, saved].sort((a, b) => a.name.localeCompare(b.name)),
        );
        this.cancel();
        this.busy.set(false);
      },
      error: (e) => {
        this.error.set(errorKey(e));
        this.busy.set(false);
      },
    });
  }

  protected toggleTeacher(institute: Institute, teacherId: string, on: boolean): void {
    const teacherIds = on
      ? [...institute.teacherIds, teacherId]
      : institute.teacherIds.filter((id) => id !== teacherId);
    this.api.setInstituteTeachers(institute.id, teacherIds).subscribe({
      next: () => this.institutes.update((rows) => rows.map((r) => (r.id === institute.id ? { ...r, teacherIds } : r))),
      error: (e) => this.error.set(errorKey(e)),
    });
  }
}
