import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { AdminService, Institute, TeacherSummary } from '../../../core/api/admin.service';
import { inlineError } from '../../../core/api/problem';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Loadable } from '../../../core/state/loadable';
import { ToastService } from '../../../core/ui/toast.service';
import { SubmitButton } from '../../../shared/submit-button';

interface Page {
  institutes: Institute[];
  teachers: TeacherSummary[];
}

/** Super Admin: add institutes and choose which teachers work at each. */
@Component({
  selector: 'app-admin-institutes',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    SubmitButton,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    TranslatePipe,
  ],
  template: `
    <h1 class="page-title">{{ 'institutes.title' | t }}</h1>

    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ (editing() ? 'institutes.edit' : 'institutes.add') | t }}</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <form class="stack form" [formGroup]="form" (ngSubmit)="save()">
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
            <p class="field-error" role="alert">{{ error() | t }}</p>
          }
          <div class="row-wrap">
            <app-submit-button label="institutes.save" [busy]="busy()" [disabled]="form.invalid" />
            @if (editing()) {
              <button mat-button type="button" (click)="cancel()">{{ 'institutes.cancel' | t }}</button>
            }
          </div>
        </form>
      </mat-card-content>
    </mat-card>

    @switch (page.status()) {
      @case ('ready') {
        @for (institute of institutes(); track institute.id) {
          <mat-card appearance="outlined">
            <mat-card-content>
              <div class="head">
                <div>
                  <strong>{{ institute.name }}</strong>
                  <div class="muted">{{ institute.town }}</div>
                </div>
                <button mat-button type="button" (click)="edit(institute)">
                  <mat-icon>edit</mat-icon>
                  {{ 'institutes.edit' | t }}
                </button>
              </div>
              <h3 class="section-title">{{ 'institutes.teachers' | t }}</h3>
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
      }
      @case ('error') {
        <p class="muted">{{ 'error.load_failed' | t }}</p>
        <button mat-stroked-button type="button" (click)="load()">{{ 'status.retry' | t }}</button>
      }
      @default {
        <div class="skeleton"></div>
        <div class="skeleton"></div>
      }
    }
  `,
  styles: `
    mat-checkbox { display: block; }
    .form { padding-top: 1rem; }
    .head { display: flex; justify-content: space-between; align-items: center; gap: 1rem; }
  `,
})
export class AdminInstitutes {
  private readonly api = inject(AdminService);
  private readonly toast = inject(ToastService);

  protected readonly page = new Loadable<Page>();
  protected readonly institutes = computed(() => this.page.data()?.institutes ?? []);
  protected readonly activeTeachers = computed(() => (this.page.data()?.teachers ?? []).filter((t) => t.isActive));
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
    this.load();
  }

  protected load(): void {
    this.page.load(
      forkJoin({ institutes: this.api.institutes(), teachers: this.api.teachers() }),
    );
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
        this.page.update((p) => ({
          ...p,
          institutes: current
            ? p.institutes.map((r) => (r.id === saved.id ? saved : r))
            : [...p.institutes, saved].sort((a, b) => a.name.localeCompare(b.name)),
        }));
        this.cancel();
        this.busy.set(false);
        this.toast.success('institutes.saved');
      },
      error: (e) => {
        this.error.set(inlineError(e));
        this.busy.set(false);
      },
    });
  }

  protected toggleTeacher(institute: Institute, teacherId: string, on: boolean): void {
    const teacherIds = on
      ? [...institute.teacherIds, teacherId]
      : institute.teacherIds.filter((id) => id !== teacherId);
    this.api.setInstituteTeachers(institute.id, teacherIds).subscribe({
      next: () =>
        this.page.update((p) => ({
          ...p,
          institutes: p.institutes.map((r) => (r.id === institute.id ? { ...r, teacherIds } : r)),
        })),
      error: () => this.page.update((p) => ({ ...p })),
    });
  }
}
