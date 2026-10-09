import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { AdminService, TeacherSummary } from '../../../core/api/admin.service';
import { inlineError } from '../../../core/api/problem';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslateService } from '../../../core/i18n/translate.service';
import { Loadable } from '../../../core/state/loadable';
import { ToastService } from '../../../core/ui/toast.service';
import { SubmitButton } from '../../../shared/submit-button';

interface Handout {
  name: string;
  phone: string;
  password: string;
}

/** Super Admin: add teachers and hand out one-time passwords. */
@Component({
  selector: 'app-admin-teachers',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    SubmitButton,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSlideToggleModule,
    TranslatePipe,
  ],
  template: `
    <h1 class="page-title">{{ 'teachers.title' | t }}</h1>

    @if (handout(); as h) {
      <mat-card appearance="outlined" class="handout">
        <mat-card-content>
          <p>{{ 'teachers.password_for' | t }} <strong>{{ h.name }}</strong> ({{ h.phone }})</p>
          <p class="otp">{{ h.password }}</p>
          <p class="muted">{{ 'teachers.password_once' | t }}</p>
          <div class="row-wrap">
            <button mat-stroked-button type="button" (click)="copy(h.password)">
              <mat-icon>content_copy</mat-icon>
              {{ 'teachers.copy' | t }}
            </button>
            <a mat-stroked-button [href]="whatsapp(h)" target="_blank" rel="noopener">
              <mat-icon>send</mat-icon>
              {{ 'teachers.share' | t }}
            </a>
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
        <form class="stack form" [formGroup]="form" (ngSubmit)="create()">
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
            <p class="field-error" role="alert">{{ error() | t }}</p>
          }
          <div class="row-wrap">
            <app-submit-button label="teachers.create" [busy]="busy()" [disabled]="form.invalid" />
          </div>
        </form>
      </mat-card-content>
    </mat-card>

    <h2 class="section-title">{{ 'teachers.all' | t }}</h2>
    @switch (teachers.status()) {
      @case ('ready') {
        @for (teacher of teachers.data(); track teacher.id) {
          <mat-card appearance="outlined">
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
              <button mat-button type="button" (click)="reset(teacher)">
                <mat-icon>key</mat-icon>
                {{ 'teachers.reset' | t }}
              </button>
            </mat-card-content>
          </mat-card>
        } @empty {
          <p class="muted">{{ 'teachers.none' | t }}</p>
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
    .form { padding-top: 1rem; }
    .head { display: flex; justify-content: space-between; gap: 1rem; align-items: center; flex-wrap: wrap; }
    .otp { margin: 0.5rem 0; font: 600 1.75rem/1.2 ui-monospace, monospace; letter-spacing: 0.08em; user-select: all; }
  `,
})
export class AdminTeachers {
  private readonly api = inject(AdminService);
  private readonly i18n = inject(TranslateService);
  private readonly toast = inject(ToastService);

  protected readonly teachers = new Loadable<TeacherSummary[]>();
  protected readonly handout = signal<Handout | null>(null);
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

  protected load(): void {
    this.teachers.load(this.api.teachers());
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
        this.toast.success('teachers.created');
        this.load();
      },
      error: (e) => {
        this.error.set(inlineError(e));
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
    });
  }

  protected setActive(teacher: TeacherSummary, isActive: boolean): void {
    this.api.setTeacherActive(teacher.id, isActive).subscribe({
      next: () => {
        this.toast.success(isActive ? 'teachers.activated' : 'teachers.deactivated');
        this.load();
      },
      error: () => this.load(),
    });
  }

  protected async copy(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.toast.success('teachers.copied');
    } catch {
      // Clipboard access can be refused; the password is selectable on screen.
      this.toast.error('teachers.copy_failed');
    }
  }

  protected whatsapp(h: Handout): string {
    const text = this.i18n.translate('teachers.share_message').replace('{password}', h.password);
    return `https://wa.me/${h.phone.replace('+', '')}?text=${encodeURIComponent(text)}`;
  }

  private show(teacher: TeacherSummary, password: string): void {
    this.handout.set({ name: teacher.fullName, phone: teacher.phone, password });
  }
}
