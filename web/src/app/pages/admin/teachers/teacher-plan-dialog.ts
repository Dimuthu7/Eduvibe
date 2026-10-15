import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { TeacherSummary } from '../../../core/api/admin.service';
import { errorCode, inlineError } from '../../../core/api/problem';
import { PAYMENT_METHODS, Plan, PlansService, TeacherPlan } from '../../../core/api/plans.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Loadable } from '../../../core/state/loadable';
import { ToastService } from '../../../core/ui/toast.service';
import { applyServerError, fieldErrorKey } from '../../../shared/forms';
import { formatDate, formatLkr, todayColombo } from '../../../shared/format';
import { SubmitButton } from '../../../shared/submit-button';

interface Loaded {
  plans: Plan[];
  teacher: TeacherPlan;
}

/** A teacher's plan: what they are on, putting them on another, and the payments recorded by hand. */
@Component({
  selector: 'app-teacher-plan-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatTabsModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'teacher_plan.title' | t }}</h2>
    <mat-dialog-content>
      <p class="who">
        <strong>{{ data.fullName }}</strong>
        <span class="muted"> · {{ data.phone }}</span>
      </p>
      @switch (loaded.status()) {
        @case ('ready') {
          @if (current(); as t) {
            <div class="now" role="status">
              <span class="muted">{{ 'teacher_plan.on' | t }}</span>
              <strong>{{ t.effective.planName }}</strong>
              <span class="muted small">
                @if (t.effective.isDefault) {
                  {{ 'teacher_plan.default_note' | t }}
                } @else {
                  {{ date(t.effective.startsOn!) }}
                  @if (t.effective.endsOn) {
                    → {{ date(t.effective.endsOn) }}
                  }
                }
              </span>
            </div>

            <mat-tab-group animationDuration="0ms" [(selectedIndex)]="tab" class="tabs">
              <mat-tab [label]="'teacher_plan.tab_plan' | t">
                <form class="pane" [formGroup]="planForm" (ngSubmit)="assign()" novalidate>
                  @if (planError()) {
                    <p class="banner" role="alert">{{ planError() | t }}</p>
                  }
                  <div class="form-grid">
                    <mat-form-field class="full">
                      <mat-label>{{ 'teacher_plan.plan' | t }}</mat-label>
                      <mat-select formControlName="planId" required>
                        @for (p of choices(); track p.id) {
                          <mat-option [value]="p.id">{{ p.name }}</mat-option>
                        }
                      </mat-select>
                      <mat-error>{{ planKey('planId') | t }}</mat-error>
                    </mat-form-field>
                    <mat-form-field>
                      <mat-label>{{ 'teacher_plan.starts' | t }}</mat-label>
                      <input matInput type="date" formControlName="startsOn" required />
                      <mat-error>{{ planKey('startsOn') | t }}</mat-error>
                    </mat-form-field>
                    <mat-form-field>
                      <mat-label>{{ 'teacher_plan.ends' | t }}</mat-label>
                      <input matInput type="date" formControlName="endsOn" [min]="planForm.controls.startsOn.value" />
                      <mat-hint>{{ 'teacher_plan.ends_hint' | t }}</mat-hint>
                      <mat-error>{{ planKey('endsOn') | t }}</mat-error>
                    </mat-form-field>
                    <mat-form-field class="full">
                      <mat-label>{{ 'teacher_plan.note' | t }}</mat-label>
                      <input matInput formControlName="note" maxlength="200" autocomplete="off" />
                      <mat-error>{{ planKey('note') | t }}</mat-error>
                    </mat-form-field>
                  </div>
                  <app-submit-button label="teacher_plan.assign" [busy]="busy()" />
                </form>

                <h3 class="group">{{ 'teacher_plan.history' | t }}</h3>
                <div class="panel">
                  @for (s of t.subscriptions; track s.id) {
                    <div class="panel-row">
                      <div class="row-main">
                        <div class="row-title">
                          {{ s.planName }}
                          @if (s.inEffect) {
                            <span class="status on">{{ 'teacher_plan.in_effect' | t }}</span>
                          } @else if (s.status === 'cancelled') {
                            <span class="status off">{{ 'teacher_plan.cancelled' | t }}</span>
                          }
                        </div>
                        <div class="row-meta">{{ date(s.startsOn) }}{{ s.endsOn ? ' → ' + date(s.endsOn) : '' }}</div>
                        @if (s.note) {
                          <div class="row-meta">{{ s.note }}</div>
                        }
                      </div>
                    </div>
                  } @empty {
                    <div class="panel-row muted">{{ 'teacher_plan.no_history' | t }}</div>
                  }
                </div>
              </mat-tab>

              <mat-tab [label]="'teacher_plan.tab_payments' | t">
                <form class="pane" [formGroup]="payForm" (ngSubmit)="pay()" novalidate>
                  @if (payError()) {
                    <p class="banner" role="alert">{{ payError() | t }}</p>
                  }
                  <div class="form-grid">
                    <mat-form-field>
                      <mat-label>{{ 'fees.amount' | t }}</mat-label>
                      <span matTextPrefix>LKR&nbsp;</span>
                      <input matInput type="number" inputmode="decimal" formControlName="amount" min="1" step="any" required />
                      <mat-error>{{ payKey('amount') | t }}</mat-error>
                    </mat-form-field>
                    <mat-form-field>
                      <mat-label>{{ 'fees.method' | t }}</mat-label>
                      <mat-select formControlName="method" required>
                        @for (m of methods; track m) {
                          <mat-option [value]="m">{{ 'teacher_plan.method.' + m | t }}</mat-option>
                        }
                      </mat-select>
                      <mat-error>{{ payKey('method') | t }}</mat-error>
                    </mat-form-field>
                    <mat-form-field>
                      <mat-label>{{ 'fees.paid_on' | t }}</mat-label>
                      <input matInput type="date" formControlName="paidOn" [max]="today" required />
                      <mat-error>{{ payKey('paidOn') | t }}</mat-error>
                    </mat-form-field>
                    <mat-form-field>
                      <mat-label>{{ 'teacher_plan.reference' | t }}</mat-label>
                      <input matInput formControlName="reference" maxlength="100" autocomplete="off" />
                      <mat-error>{{ payKey('reference') | t }}</mat-error>
                    </mat-form-field>
                  </div>
                  <app-submit-button label="teacher_plan.record" [busy]="busy()" />
                </form>

                <h3 class="group">{{ 'teacher_plan.payments' | t }}</h3>
                <div class="panel">
                  @for (p of t.payments; track p.id) {
                    <div class="panel-row">
                      <div class="row-main">
                        <div class="row-title amount">{{ money(p.amount) }}</div>
                        <div class="row-meta">{{ date(p.paidOn) }} · {{ 'teacher_plan.method.' + p.method | t }} · {{ p.planName }}</div>
                        @if (p.reference) {
                          <div class="row-meta">{{ p.reference }}</div>
                        }
                      </div>
                    </div>
                  } @empty {
                    <div class="panel-row muted">{{ 'teacher_plan.no_payments' | t }}</div>
                  }
                </div>
              </mat-tab>
            </mat-tab-group>
          }
        }
        @case ('error') {
          <div class="empty">
            <p>{{ 'error.load_failed' | t }}</p>
            <button mat-stroked-button type="button" (click)="load()">{{ 'status.retry' | t }}</button>
          </div>
        }
        @default {
          <div class="skeleton"></div>
          <div class="skeleton"></div>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>{{ 'common.done' | t }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    .who { margin: 0 0 0.75rem; }
    .now { display: grid; gap: 0.125rem; margin-bottom: 0.75rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); }
    .now strong { font: var(--mat-sys-title-medium); }
    .small { font: var(--mat-sys-body-small); }
    .pane { display: grid; gap: 0.75rem; padding-top: 1rem; justify-items: start; }
    .pane .form-grid { width: 100%; }
    .group { margin: 1.25rem 0 0.5rem; font: var(--mat-sys-title-small); }
    .banner { margin: 0; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .status { margin-left: 0.375rem; vertical-align: middle; padding: 0.125rem 0.625rem; border-radius: 999px; font: var(--mat-sys-label-small); white-space: nowrap; }
    .status.on { background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .status.off { background: var(--mat-sys-surface-container-highest); color: var(--mat-sys-on-surface-variant); }
  `,
})
export class TeacherPlanDialog {
  protected readonly data = inject<TeacherSummary>(MAT_DIALOG_DATA);
  private readonly api = inject(PlansService);
  private readonly toast = inject(ToastService);

  protected readonly today = todayColombo();
  protected readonly methods = PAYMENT_METHODS;
  protected readonly date = formatDate;
  protected readonly money = formatLkr;
  protected readonly tab = signal(0);
  protected readonly busy = signal(false);
  protected readonly planError = signal('');
  protected readonly payError = signal('');
  protected readonly loaded = new Loadable<Loaded>();
  /** Set after a change, so the screen shows the latest without loading again. */
  private readonly latest = signal<TeacherPlan | null>(null);
  protected readonly current = computed(() => this.latest() ?? this.loaded.data()?.teacher ?? null);
  /** Plans that can be assigned: the active ones. */
  protected readonly choices = computed(() => (this.loaded.data()?.plans ?? []).filter((p) => p.isActive));

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly planForm = this.fb.group({
    planId: ['', Validators.required],
    startsOn: [this.today, Validators.required],
    endsOn: [''],
    note: ['', Validators.maxLength(200)],
  });
  protected readonly payForm = this.fb.group({
    amount: [null as number | null, [Validators.required, Validators.min(1)]],
    method: ['cash' as string, Validators.required],
    paidOn: [this.today, Validators.required],
    reference: ['', Validators.maxLength(100)],
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loaded.load(forkJoin({ plans: this.api.plans(), teacher: this.api.teacherPlan(this.data.id) }));
  }

  protected planKey(name: string): string {
    const control = this.planForm.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected payKey(name: string): string {
    const control = this.payForm.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected assign(): void {
    this.planForm.markAllAsTouched();
    if (this.planForm.invalid) return;

    this.busy.set(true);
    this.planError.set('');
    const { planId, startsOn, endsOn, note } = this.planForm.getRawValue();
    this.api.assign(this.data.id, { planId, startsOn, endsOn: endsOn || null, note: note.trim() || null }).subscribe({
      next: (teacher) => {
        this.busy.set(false);
        this.latest.set(teacher);
        this.toast.success('teacher_plan.assigned');
      },
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.planForm, errorCode(e))) this.planError.set(inlineError(e));
      },
    });
  }

  protected pay(): void {
    this.payForm.markAllAsTouched();
    if (this.payForm.invalid) return;

    this.busy.set(true);
    this.payError.set('');
    const { amount, method, paidOn, reference } = this.payForm.getRawValue();
    this.api.recordPayment(this.data.id, { amount: Number(amount), method, paidOn, reference: reference.trim() || null }).subscribe({
      next: (teacher) => {
        this.busy.set(false);
        this.latest.set(teacher);
        this.payForm.reset({ amount: null, method, paidOn: this.today, reference: '' });
        this.toast.success('teacher_plan.recorded');
      },
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.payForm, errorCode(e))) this.payError.set(inlineError(e));
      },
    });
  }
}
