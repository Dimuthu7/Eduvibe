import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ExamsService, GradeBand } from '../../core/api/exams.service';
import { inlineError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { OL_DEFAULT_BANDS } from '../../shared/exam-calc';
import { fieldErrorKey, filled } from '../../shared/forms';
import { SubmitButton } from '../../shared/submit-button';

const MAX_BANDS = 10;

/** The teacher's grade scheme: each grade and the lowest percentage that earns it. Starts as the O/L default. */
@Component({
  selector: 'app-grading',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, SubmitButton, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslatePipe],
  template: `
    <a mat-button routerLink="/exams" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'exams.title' | t }}
    </a>
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'exams.grading' | t }}</h1>
        <p class="page-subtitle">{{ 'exams.grading_subtitle' | t }}</p>
      </div>
    </header>

    @switch (scheme.status()) {
      @case ('ready') {
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          @if (error()) {
            <p class="banner" role="alert">{{ error() | t }}</p>
          }
          <div formArrayName="bands" class="bands">
            @for (band of bands.controls; track band; let i = $index) {
              <div class="band" [formGroupName]="i">
                <mat-form-field class="grade">
                  <mat-label>{{ 'exams.grade' | t }}</mat-label>
                  <input matInput formControlName="grade" maxlength="3" autocomplete="off" required />
                  <mat-error>{{ bandKey(i, 'grade') | t }}</mat-error>
                </mat-form-field>
                <mat-form-field class="min">
                  <mat-label>{{ 'exams.from_percent' | t }}</mat-label>
                  <input matInput type="number" inputmode="decimal" formControlName="minPercent" min="0" max="100" step="any" required />
                  <span matTextSuffix>%</span>
                  <mat-error>{{ bandKey(i, 'minPercent') | t }}</mat-error>
                </mat-form-field>
                @if (bands.length > 1) {
                  <button mat-icon-button type="button" class="remove" (click)="remove(i)" [attr.aria-label]="'common.remove' | t">
                    <mat-icon>close</mat-icon>
                  </button>
                }
              </div>
            }
          </div>
          <p class="muted hint">{{ 'exams.grading_hint' | t }}</p>
          <div class="row-wrap actions">
            @if (bands.length < maxBands) {
              <button mat-stroked-button type="button" (click)="add()">
                <mat-icon>add</mat-icon>
                {{ 'exams.add_grade' | t }}
              </button>
            }
            <button mat-button type="button" (click)="useDefault()">{{ 'exams.reset_default' | t }}</button>
            <span class="spacer"></span>
            <app-submit-button label="common.save" [busy]="busy()" />
          </div>
        </form>
      }
      @case ('error') {
        <div class="empty">
          <p>{{ 'error.load_failed' | t }}</p>
          <button mat-stroked-button type="button" (click)="load()">{{ 'status.retry' | t }}</button>
        </div>
      }
      @default {
        <div class="skeleton"></div>
      }
    }
  `,
  styles: `
    .back { margin-left: -0.75rem; margin-bottom: 0.5rem; }
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .band { display: flex; gap: 0.75rem; align-items: flex-start; }
    .grade { flex: 1 1 0; min-width: 0; }
    .min { flex: 2 1 0; min-width: 0; }
    .remove { flex: none; margin-top: 0.25rem; }
    .hint { margin: 0.25rem 0 1rem; }
    .actions { align-items: center; }
    .spacer { flex: 1; }
  `,
})
export class Grading {
  private readonly api = inject(ExamsService);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(FormBuilder).nonNullable;

  protected readonly maxBands = MAX_BANDS;
  protected readonly scheme = new Loadable<{ bands: GradeBand[] }>();
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = this.fb.group({ bands: this.fb.array<ReturnType<Grading['bandGroup']>>([]) });

  protected get bands(): FormArray {
    return this.form.controls.bands;
  }

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.scheme.status.set('loading');
    this.api.gradeScheme().subscribe({
      next: (scheme) => {
        this.fill(scheme.bands);
        this.scheme.data.set(scheme);
        this.scheme.status.set('ready');
      },
      error: () => this.scheme.status.set('error'),
    });
  }

  protected bandKey(index: number, name: string): string {
    const control = this.bands.at(index).get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected add(): void {
    this.bands.push(this.bandGroup('', 0));
  }

  protected remove(index: number): void {
    this.bands.removeAt(index);
  }

  protected useDefault(): void {
    this.fill(OL_DEFAULT_BANDS);
    this.form.markAsDirty();
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    this.error.set('');
    if (this.form.invalid) return;

    const bands = this.form.getRawValue().bands.map((b: { grade: string; minPercent: number | string }) => ({
      grade: b.grade.trim(),
      minPercent: Number(b.minPercent),
    }));
    this.busy.set(true);
    this.api.saveGradeScheme(bands).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.fill(saved.bands);
        this.toast.success('exams.grading_saved');
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(inlineError(e));
      },
    });
  }

  private fill(bands: GradeBand[]): void {
    this.bands.clear();
    for (const band of [...bands].sort((a, b) => b.minPercent - a.minPercent)) this.bands.push(this.bandGroup(band.grade, band.minPercent));
  }

  private bandGroup(grade: string, minPercent: number) {
    return this.fb.group({
      grade: [grade, [filled, Validators.maxLength(3)]],
      minPercent: [minPercent as number | string, [Validators.required, Validators.min(0), Validators.max(100)]],
    });
  }
}
