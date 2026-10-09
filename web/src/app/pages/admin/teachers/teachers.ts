import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { AdminService, TeacherSummary } from '../../../core/api/admin.service';
import { errorKey } from '../../../core/api/problem';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslateService } from '../../../core/i18n/translate.service';

interface Handout {
  name: string;
  phone: string;
  password: string;
}

/** Super Admin: add teachers and hand out one-time passwords. */
@Component({
  selector: 'app-admin-teachers',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, TranslatePipe],
  template: `
    <h1>{{ 'teachers.title' | t }}</h1>

    @if (handout(); as h) {
      <mat-card appearance="outlined" class="handout">
        <mat-card-content>
          <p>{{ 'teachers.password_for' | t }} <strong>{{ h.name }}</strong> ({{ h.phone }})</p>
          <p class="otp">{{ h.password }}</p>
          <p class="muted">{{ 'teachers.password_once' | t }}</p>
          <div class="actions">
            <button mat-stroked-button type="button" (click)="copy(h.password)">
              {{ (copied() ? 'teachers.copied' : 'teachers.copy') | t }}
            </button>
            <a mat-stroked-button [href]="whatsapp(h)" target="_blank" rel="noopener">{{ 'teachers.share' | t }}</a>
            <button mat-button type="button" (click)="handout.set(null)">{{ 'teachers.done' | t }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }

    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ 'teachers.add' | t }}</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <form class="form" [formGroup]="form" (ngSubmit)="create()">
          <mat-form-field>
            <mat-label>{{ 'profile.name' | t }}</mat-label>
            <input matInput formControlName="fullName" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'login.phone' | t }}</mat-label>
            <input matInput type="tel" inputmode="tel" formControlName="phone" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'profile.town' | t }}</mat-label>
            <input matInput formControlName="town" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'profile.subjects' | t }}</mat-label>
            <input matInput formControlName="subjects" />
          </mat-form-field>
          @if (error()) {
            <p class="error" role="alert">{{ error() | t }}</p>
          }
          <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">{{ 'teachers.create' | t }}</button>
        </form>
      </mat-card-content>
    </mat-card>

    <h2>{{ 'teachers.all' | t }}</h2>
    @for (teacher of teachers(); track teacher.id) {
      <mat-card appearance="outlined" class="teacher">
        <mat-card-content>
          <div class="head">
            <div>
              <strong>{{ teacher.fullName }}</strong>
              <div class="muted">{{ teacher.phone }}@if (teacher.town) { · {{ teacher.town }} }</div>
              @if (teacher.mustChangePassword) {
                <div class="muted">{{ 'teachers.not_signed_in' | t }}</div>
              }
            </div>
            <mat-slide-toggle [checked]="teacher.isActive" (change)="setActive(teacher, $event.checked)">
              {{ 'teachers.active' | t }}
            </mat-slide-toggle>
          </div>
          <button mat-button type="button" (click)="reset(teacher)">{{ 'teachers.reset' | t }}</button>
        </mat-card-content>
      </mat-card>
    } @empty {
      <p class="muted">{{ 'teachers.none' | t }}</p>
    }
  `,
  styles: `
    :host { display: block; max-width: 40rem; margin: 1.5rem auto; padding: 0 1rem; }
    mat-card { margin-bottom: 1rem; }
    .form { display: flex; flex-direction: column; gap: 0.5rem; padding-top: 1rem; }
    .head { display: flex; justify-content: space-between; gap: 1rem; align-items: center; flex-wrap: wrap; }
    .actions { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .otp { font: 600 1.75rem/1.2 ui-monospace, monospace; letter-spacing: 0.08em; margin: 0.5rem 0; user-select: all; }
    .muted { opacity: 0.7; }
    .error { margin: 0; color: var(--mat-sys-error); }
  `,
})
export class AdminTeachers {
  private readonly api = inject(AdminService);
  private readonly i18n = inject(TranslateService);

  protected readonly teachers = signal<TeacherSummary[]>([]);
  protected readonly handout = signal<Handout | null>(null);
  protected readonly copied = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    fullName: ['', Validators.required],
    phone: ['', Validators.required],
    town: [''],
    subjects: [''],
  });

  constructor() {
    this.load();
  }

  protected create(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    this.api.createTeacher(this.form.getRawValue()).subscribe({
      next: ({ teacher, oneTimePassword }) => {
        this.show(teacher, oneTimePassword);
        this.form.reset();
        this.busy.set(false);
        this.load();
      },
      error: (e) => {
        this.error.set(errorKey(e));
        this.busy.set(false);
      },
    });
  }

  protected reset(teacher: TeacherSummary): void {
    this.api.resetPassword(teacher.id).subscribe({
      next: ({ oneTimePassword }) => {
        this.show(teacher, oneTimePassword);
        this.load();
      },
      error: (e) => this.error.set(errorKey(e)),
    });
  }

  protected setActive(teacher: TeacherSummary, isActive: boolean): void {
    this.api.setTeacherActive(teacher.id, isActive).subscribe({
      next: () => this.load(),
      error: (e) => {
        this.error.set(errorKey(e));
        this.load();
      },
    });
  }

  protected async copy(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.copied.set(true);
    } catch {
      // Clipboard access can be refused; the password is selectable on screen.
    }
  }

  protected whatsapp(h: Handout): string {
    const text = this.i18n.translate('teachers.share_message').replace('{password}', h.password);
    return `https://wa.me/${h.phone.replace('+', '')}?text=${encodeURIComponent(text)}`;
  }

  private show(teacher: TeacherSummary, password: string): void {
    this.copied.set(false);
    this.handout.set({ name: teacher.fullName, phone: teacher.phone, password });
  }

  private load(): void {
    this.api.teachers().subscribe({
      next: (rows) => this.teachers.set(rows),
      error: (e) => this.error.set(errorKey(e)),
    });
  }
}
