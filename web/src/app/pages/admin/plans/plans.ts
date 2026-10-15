import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { Plan, PLAN_FEATURES, PLAN_LIMITS, PlansService } from '../../../core/api/plans.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslateService } from '../../../core/i18n/translate.service';
import { Loadable } from '../../../core/state/loadable';
import { ToastService } from '../../../core/ui/toast.service';
import { formatLkr } from '../../../shared/format';
import { PlanDialog } from './plan-dialog';

/** Super Admin: the plans, what each includes and what it costs. Teachers are put on a plan from the Teachers list. */
@Component({
  selector: 'app-admin-plans',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'plans.title' | t }}</h1>
        <p class="page-subtitle">{{ 'plans.subtitle' | t }}</p>
      </div>
      <button mat-flat-button type="button" (click)="open(null)">
        <mat-icon>add</mat-icon>
        {{ 'plans.new' | t }}
      </button>
    </header>

    @switch (plans.status()) {
      @case ('ready') {
        <div class="panel">
          @for (plan of plans.data(); track plan.id) {
            <button type="button" class="panel-row link" (click)="open(plan)">
              <div class="row-main">
                <div class="row-title">
                  {{ plan.name }}
                  @if (plan.isDefault) {
                    <span class="status wait">{{ 'plans.default' | t }}</span>
                  }
                  @if (!plan.isActive) {
                    <span class="status off">{{ 'plans.inactive' | t }}</span>
                  }
                </div>
                <div class="row-meta">{{ price(plan) }}</div>
                <div class="row-meta">{{ featureText(plan) }}</div>
                <div class="row-meta">{{ limitText(plan) }}</div>
              </div>
              <mat-icon class="chevron" aria-hidden="true">edit</mat-icon>
            </button>
          }
        </div>
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
  `,
  styles: `
    button.link { width: 100%; border: 0; background: none; color: inherit; font: inherit; text-align: start; cursor: pointer; }
    button.link:hover { background: var(--mat-sys-surface-container); }
    .chevron { flex: none; color: var(--mat-sys-on-surface-variant); }
    .status { margin-left: 0.375rem; vertical-align: middle; padding: 0.125rem 0.625rem; border-radius: 999px; font: var(--mat-sys-label-small); white-space: nowrap; }
    .status.off { background: var(--mat-sys-surface-container-highest); color: var(--mat-sys-on-surface-variant); }
    .status.wait { background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
  `,
})
export class AdminPlans {
  private readonly api = inject(PlansService);
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(TranslateService);
  private readonly toast = inject(ToastService);

  protected readonly plans = new Loadable<Plan[]>();

  constructor() {
    this.load();
  }

  protected load(): void {
    this.plans.load(this.api.plans());
  }

  protected open(plan: Plan | null): void {
    this.dialog
      .open<PlanDialog, Plan | null, Plan>(PlanDialog, { data: plan, width: '36rem', maxWidth: 'calc(100vw - 1rem)', autoFocus: 'first-tabbable' })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.toast.success('plans.saved');
        this.load();
      });
  }

  protected price(plan: Plan): string {
    return plan.monthlyPrice > 0 ? `${formatLkr(plan.monthlyPrice)} ${this.i18n.translate('plans.per_month')}` : this.i18n.translate('plans.free_of_charge');
  }

  protected featureText(plan: Plan): string {
    const on = PLAN_FEATURES.filter((f) => plan.features[f]).map((f) => this.i18n.translate(`plans.feature.${f}`));
    return on.length > 0 ? on.join(', ') : this.i18n.translate('plans.no_paid_features');
  }

  protected limitText(plan: Plan): string {
    const capped = PLAN_LIMITS.filter((l) => plan.limits[l] !== null && plan.limits[l] !== undefined).map(
      (l) => `${this.i18n.translate(`plans.limit.${l}`)}: ${plan.limits[l]}`,
    );
    return capped.length > 0 ? capped.join(' · ') : this.i18n.translate('plans.no_limits');
  }
}
