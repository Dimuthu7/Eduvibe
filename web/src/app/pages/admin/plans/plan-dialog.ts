import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { errorCode, inlineError } from '../../../core/api/problem';
import { Plan, PLAN_FEATURES, PLAN_LIMITS, PlanInput, PlansService } from '../../../core/api/plans.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { applyServerError, fieldErrorKey, filled } from '../../../shared/forms';
import { SubmitButton } from '../../../shared/submit-button';

/** The request for a plan from the form: a blank limit is no cap, and a feature the form leaves off is off. */
export function toPlanInput(value: {
  name: string;
  description: string;
  monthlyPrice: number | null;
  isActive: boolean;
  features: Record<string, boolean>;
  limits: Record<string, number | null>;
}): PlanInput {
  return {
    name: value.name.trim(),
    description: value.description.trim() || null,
    monthlyPrice: Number(value.monthlyPrice ?? 0),
    isActive: value.isActive,
    features: { ...value.features },
    limits: Object.fromEntries(Object.entries(value.limits).map(([key, cap]) => [key, cap === null || (cap as unknown) === '' ? null : Number(cap)])),
  };
}

/** The "New plan" and "Edit plan" pop-up: a name and price, which features the plan includes and its caps. Closes with the saved plan. */
@Component({
  selector: 'app-plan-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ (plan ? 'plans.edit' : 'plans.new') | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <div class="form-grid">
          <mat-form-field class="full">
            <mat-label>{{ 'plans.name' | t }}</mat-label>
            <input matInput formControlName="name" autocomplete="off" maxlength="60" required />
            <mat-error>{{ key('name') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field class="full">
            <mat-label>{{ 'plans.description' | t }}</mat-label>
            <textarea matInput formControlName="description" rows="2" maxlength="300"></textarea>
            <mat-error>{{ key('description') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field class="full">
            <mat-label>{{ 'plans.price' | t }}</mat-label>
            <span matTextPrefix>LKR&nbsp;</span>
            <input matInput type="number" inputmode="decimal" formControlName="monthlyPrice" min="0" step="any" />
            <mat-hint>{{ 'plans.price_hint' | t }}</mat-hint>
            <mat-error>{{ key('monthlyPrice') | t }}</mat-error>
          </mat-form-field>
        </div>

        <h3 class="group">{{ 'plans.features' | t }}</h3>
        <div class="toggles" formGroupName="features">
          @for (feature of features; track feature) {
            <mat-slide-toggle [formControlName]="feature">{{ 'plans.feature.' + feature | t }}</mat-slide-toggle>
          }
        </div>

        <h3 class="group">{{ 'plans.limits' | t }}</h3>
        <div class="form-grid" formGroupName="limits">
          @for (limit of limits; track limit) {
            <mat-form-field class="full">
              <mat-label>{{ 'plans.limit.' + limit | t }}</mat-label>
              <input matInput type="number" inputmode="numeric" [formControlName]="limit" min="0" max="1000000" step="1" [placeholder]="'plans.no_limit' | t" />
              <mat-error>{{ limitKey(limit) | t }}</mat-error>
            </mat-form-field>
          }
        </div>
        <p class="muted hint">{{ 'plans.limit_hint' | t }}</p>

        @if (!plan?.isDefault) {
          <mat-slide-toggle formControlName="isActive">{{ 'plans.active' | t }}</mat-slide-toggle>
          <p class="muted hint">{{ 'plans.active_hint' | t }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button label="common.save" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .group { margin: 1rem 0 0.5rem; font: var(--mat-sys-title-small); }
    .toggles { display: grid; gap: 0.5rem; }
    .hint { margin: 0.25rem 0 0.75rem; font: var(--mat-sys-body-small); }
  `,
})
export class PlanDialog {
  protected readonly plan = inject<Plan | null>(MAT_DIALOG_DATA, { optional: true });
  private readonly api = inject(PlansService);
  private readonly ref = inject<MatDialogRef<PlanDialog, Plan>>(MatDialogRef);

  protected readonly features = PLAN_FEATURES;
  protected readonly limits = PLAN_LIMITS;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    name: [this.plan?.name ?? '', [filled, Validators.maxLength(60)]],
    description: [this.plan?.description ?? '', [Validators.maxLength(300)]],
    monthlyPrice: [this.plan?.monthlyPrice ?? (0 as number | null), [Validators.min(0), Validators.max(10_000_000)]],
    isActive: [this.plan?.isActive ?? true],
    features: this.fb.group(Object.fromEntries(PLAN_FEATURES.map((f) => [f, [this.plan?.features[f] ?? false]]))),
    limits: this.fb.group(Object.fromEntries(PLAN_LIMITS.map((l) => [l, [(this.plan?.limits[l] ?? null) as number | null, [Validators.min(0), Validators.max(1_000_000)]]]))),
  });

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected limitKey(name: string): string {
    const control = this.form.get(['limits', name]);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.busy.set(true);
    this.error.set('');
    const input = toPlanInput(this.form.getRawValue());
    const request = this.plan ? this.api.updatePlan(this.plan.id, input) : this.api.createPlan(input);
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}
