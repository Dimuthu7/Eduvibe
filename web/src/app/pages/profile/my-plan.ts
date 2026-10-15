import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MyPlan, PlansService } from '../../core/api/plans.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { formatDate } from '../../shared/format';

/** A teacher's own plan: its name, what it includes, and what is locked with the plan that unlocks it. Read only; the Super Admin decides it. */
@Component({
  selector: 'app-my-plan',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MatIconModule, TranslatePipe],
  template: `
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ 'my_plan.title' | t }}</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        @switch (plan.status()) {
          @case ('ready') {
            @if (plan.data(); as p) {
              <p class="current">
                <strong>{{ p.planName }}</strong>
                @if (p.endsOn) {
                  <span class="muted"> · {{ 'my_plan.until' | t }} {{ date(p.endsOn) }}</span>
                }
              </p>
              <ul class="list" [attr.aria-label]="'my_plan.features' | t">
                @for (f of p.features; track f.key) {
                  <li [class.locked]="!f.enabled">
                    <mat-icon aria-hidden="true">{{ f.enabled ? 'check_circle' : 'lock' }}</mat-icon>
                    <div class="text">
                      <span class="name">{{ 'plans.feature.' + f.key | t }}</span>
                      @if (!f.enabled && f.availableOn) {
                        <span class="chip">{{ 'my_plan.available_on' | t }} {{ f.availableOn }}</span>
                      }
                    </div>
                  </li>
                }
              </ul>
              @if (capped(p).length > 0) {
                <p class="limits muted">
                  @for (l of capped(p); track l.key) {
                    <span>{{ 'plans.limit.' + l.key | t }}: {{ l.limit }}</span>
                  }
                </p>
              }
            }
          }
          @case ('error') {
            <p class="muted">{{ 'error.load_failed' | t }}</p>
          }
          @default {
            <div class="skeleton"></div>
          }
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: block; margin-top: 1rem; }
    .current { margin: 0.5rem 0; font: var(--mat-sys-title-medium); }
    .list { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.5rem; }
    .list li { display: flex; align-items: flex-start; gap: 0.75rem; }
    .text { display: grid; justify-items: start; gap: 0.25rem; padding-top: 0.125rem; }
    .list mat-icon { flex: none; color: var(--mat-sys-primary); }
    .list .locked mat-icon, .list .locked .name { color: var(--mat-sys-on-surface-variant); }
    .chip { padding: 0.125rem 0.625rem; border-radius: 999px; background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); font: var(--mat-sys-label-small); white-space: nowrap; }
    .limits { display: flex; flex-wrap: wrap; gap: 0.25rem 1rem; margin: 1rem 0 0; font: var(--mat-sys-body-small); }
  `,
})
export class MyPlanCard {
  protected readonly plan = new Loadable<MyPlan>();
  protected readonly date = formatDate;

  constructor() {
    this.plan.load(inject(PlansService).mine());
  }

  /** Only the limits that cap something; a limit with no cap is not worth a line. */
  protected capped(plan: MyPlan): { key: string; limit: number }[] {
    return plan.limits.filter((l): l is { key: string; limit: number } => l.limit !== null);
  }
}
