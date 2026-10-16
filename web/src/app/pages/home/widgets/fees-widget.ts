import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { DashboardService } from '../../../core/api/dashboard.service';
import { ClassOverview, FeeOverview } from '../../../core/api/fees.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslateService } from '../../../core/i18n/translate.service';
import { Loadable } from '../../../core/state/loadable';
import { formatLkr, formatMonth, monthOf, todayColombo } from '../../../shared/format';
import { WidgetCard } from './widget-card';
import { WidgetEmpty } from './widget-empty';
import { WidgetState } from './widget-state';

const SHOWN = 4;

/** The classes that still owe fees this month, worst first: how many students and how much. Never lists a class that is fully paid. */
export function owingClasses(classes: readonly ClassOverview[]): ClassOverview[] {
  return classes
    .filter((c) => !c.isArchived && c.totals.outstanding > 0)
    .sort((a, b) => b.totals.outstanding - a.totals.outstanding || a.title.localeCompare(b.title));
}

/** The share of the month's fees already collected, 0 to 100. */
export const collectedPercent = (o: FeeOverview): number => (o.totals.due > 0 ? Math.min(100, Math.round((100 * o.totals.collected) / o.totals.due)) : 0);

/** This month's fees at a glance: what is still to collect, and which classes owe it. */
@Component({
  selector: 'app-fees-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatIconModule, TranslatePipe, WidgetCard, WidgetEmpty, WidgetState],
  template: `
    <app-widget-card icon="payments" title="dashboard.fees.title" tone="tertiary" [subtitle]="month()" link="/fees" linkLabel="dashboard.fees.all">
      <app-widget-state [status]="state.status()" (retry)="load()">
        @if (state.data(); as o) {
          @if (o.totals.due === 0) {
            <app-widget-empty icon="payments">{{ 'dashboard.fees.none' | t }}</app-widget-empty>
          } @else {
            <div class="summary">
              <div class="hero">
                <div class="owed">
                  @if (o.totals.outstanding > 0) {
                    <strong>{{ lkr(o.totals.outstanding) }}</strong>
                    <span class="muted">{{ 'dashboard.fees.to_collect' | t }}</span>
                  } @else {
                    <strong class="settled"><mat-icon aria-hidden="true">check_circle</mat-icon>{{ 'dashboard.fees.all_paid' | t }}</strong>
                  }
                </div>
                <span class="pct" [class.settled]="o.totals.outstanding === 0">{{ percent(o) }}%</span>
              </div>
              <div class="bar" role="progressbar" [attr.aria-valuenow]="percent(o)" aria-valuemin="0" aria-valuemax="100" [attr.aria-label]="collectedText(o)">
                <div class="fill" [style.width.%]="percent(o)"></div>
              </div>
              <span class="row-meta">{{ collectedText(o) }}</span>
            </div>
            @for (c of owing(o).slice(0, shown); track c.classId) {
              <a class="panel-row link" routerLink="/fees" [queryParams]="{ classId: c.classId }">
                <div class="row-main">
                  <div class="row-title">{{ c.title }}</div>
                  <div class="row-meta">{{ unpaidText(c) }}</div>
                </div>
                <strong class="amount">{{ lkr(c.totals.outstanding) }}</strong>
                <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
              </a>
            }
            @if (owing(o).length > shown) {
              <a class="line more" routerLink="/fees">{{ more(owing(o).length - shown) }}</a>
            }
            @if (o.totals.outstanding > 0) {
              <a class="line more" routerLink="/fees/unpaid">
                <mat-icon aria-hidden="true">how_to_reg</mat-icon>
                {{ 'dashboard.fees.attended_unpaid' | t }}
              </a>
            }
          }
        }
      </app-widget-state>
    </app-widget-card>
  `,
  styles: `
    mat-icon { font-size: 1.5rem; }
    .summary { display: grid; gap: 0.625rem; padding: 1rem; }
    .hero { display: flex; align-items: center; justify-content: space-between; gap: 0.75rem; }
    .owed { display: flex; flex-direction: column; min-width: 0; }
    .owed strong { font: var(--mat-sys-headline-small); font-weight: 600; }
    .owed .settled { display: flex; align-items: center; gap: 0.5rem; color: var(--mat-sys-primary); }
    .pct { flex: none; padding: 0.25rem 0.75rem; border-radius: 999px; background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); font: var(--mat-sys-label-large); }
    .pct.settled { background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .bar { height: 0.625rem; border-radius: 999px; background: var(--mat-sys-surface-container-highest); overflow: hidden; }
    .fill { height: 100%; min-width: 0.625rem; border-radius: 999px; background: var(--mat-sys-tertiary); }
    a.link { color: inherit; text-decoration: none; border-top: 1px solid var(--mat-sys-outline-variant); }
    a.link:hover { background: var(--mat-sys-surface-container); }
    a.link:active { background: var(--mat-sys-surface-container-high); }
    .amount { font: var(--mat-sys-label-large); color: var(--mat-sys-error); white-space: nowrap; }
    .chevron { color: var(--mat-sys-on-surface-variant); }
    .line { display: flex; align-items: center; gap: 0.5rem; margin: 0; padding: 0.875rem 1rem; font: var(--mat-sys-body-medium); }
    .more { border-top: 1px solid var(--mat-sys-outline-variant); color: var(--mat-sys-primary); text-decoration: none; }
    .more mat-icon { font-size: 1.25rem; width: 1.25rem; height: 1.25rem; }
  `,
})
export class FeesWidget {
  private readonly api = inject(DashboardService);
  private readonly i18n = inject(TranslateService);
  protected readonly state = new Loadable<FeeOverview>();
  protected readonly shown = SHOWN;
  protected readonly lkr = formatLkr;
  protected readonly owing = (o: FeeOverview) => owingClasses(o.classes);
  protected readonly percent = collectedPercent;
  private readonly monthKey = monthOf(todayColombo());

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.load(this.api.feeOverview(this.monthKey));
  }

  protected month(): string {
    return formatMonth(this.monthKey);
  }

  protected collectedText(o: FeeOverview): string {
    return this.i18n.format('dashboard.fees.collected', { collected: formatLkr(o.totals.collected), due: formatLkr(o.totals.due), percent: collectedPercent(o) });
  }

  protected unpaidText(c: ClassOverview): string {
    return this.i18n.format('dashboard.fees.unpaid_students', { count: c.totals.unpaid + c.totals.partlyPaid });
  }

  protected more(count: number): string {
    return this.i18n.format('dashboard.more', { count });
  }
}
