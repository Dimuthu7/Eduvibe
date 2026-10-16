import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { DashboardService, ExamProgress, ExamProgressClass } from '../../../core/api/dashboard.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslateService } from '../../../core/i18n/translate.service';
import { Loadable } from '../../../core/state/loadable';
import { formatDate, formatPercent } from '../../../shared/format';
import { WidgetCard } from './widget-card';
import { WidgetEmpty } from './widget-empty';
import { WidgetState } from './widget-state';

/** How the class average moved from the exam before, in points, or null when there is nothing to compare. */
export const averageChange = (c: ExamProgressClass): number | null =>
  c.averagePercent === null || c.previousAverage === null ? null : Math.round((c.averagePercent - c.previousAverage) * 10) / 10;

/** Where each class stands in its latest exam, and the students whose marks fell since the exam before. */
@Component({
  selector: 'app-exams-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatIconModule, TranslatePipe, WidgetCard, WidgetEmpty, WidgetState],
  template: `
    <app-widget-card icon="trending_up" title="dashboard.exams.title" tone="secondary" link="/exams" linkLabel="dashboard.exams.all">
      <app-widget-state [status]="state.status()" (retry)="load()">
        @if (state.data(); as p) {
          @for (c of p.classes; track c.classId) {
            <a class="panel-row link" [routerLink]="['/exams', c.examId]">
              <div class="row-main">
                <div class="row-title">{{ c.classTitle }}</div>
                <div class="row-meta">{{ c.examName }} · {{ date(c.heldOn) }}</div>
                @if (c.marked < c.students) {
                  <div class="row-meta warn">{{ marking(c) }}</div>
                } @else if (!c.published && c.averagePercent !== null) {
                  <div class="row-meta">{{ 'dashboard.exams.not_published' | t }}</div>
                }
              </div>
              @if (c.averagePercent !== null) {
                <div class="avg">
                  <strong>{{ percent(c.averagePercent) }}</strong>
                  @if (change(c); as delta) {
                    <span class="delta" [class.up]="delta > 0" [class.down]="delta < 0">
                      <mat-icon aria-hidden="true">{{ delta > 0 ? 'arrow_upward' : 'arrow_downward' }}</mat-icon>{{ abs(delta) }}
                    </span>
                  }
                </div>
              }
              <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
            </a>
          } @empty {
            <app-widget-empty icon="quiz">{{ 'dashboard.exams.empty' | t }}</app-widget-empty>
          }
          @if (p.dropped.length > 0) {
            <h3 class="sub">{{ 'dashboard.exams.dropped' | t }}</h3>
            @for (d of p.dropped; track d.enrolmentId) {
              <a class="panel-row link" [routerLink]="['/exams/student', d.enrolmentId]">
                <div class="row-main">
                  <div class="row-title">{{ d.fullName }}</div>
                  <div class="row-meta">{{ d.classTitle }} · {{ d.examName }}</div>
                </div>
                <span class="fall">{{ percent(d.previous) }}<mat-icon aria-hidden="true">arrow_forward</mat-icon>{{ percent(d.latest) }}</span>
              </a>
            }
            @if (p.droppedTotal > p.dropped.length) {
              <p class="line muted">{{ more(p) }}</p>
            }
          }
        }
      </app-widget-state>
    </app-widget-card>
  `,
  styles: `
    mat-icon { font-size: 1.5rem; }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    a.link:active { background: var(--mat-sys-surface-container-high); }
    .panel-row + .panel-row, .sub + .panel-row { border-top: 1px solid var(--mat-sys-outline-variant); }
    .warn { color: var(--mat-sys-error); }
    .avg { display: flex; flex-direction: column; align-items: flex-end; font: var(--mat-sys-title-medium); }
    .avg strong { font-weight: 600; }
    .delta { display: inline-flex; align-items: center; padding: 0 0.375rem 0 0.125rem; border-radius: 999px; background: var(--mat-sys-surface-container-highest); font: var(--mat-sys-label-small); color: var(--mat-sys-on-surface-variant); }
    .delta mat-icon, .fall mat-icon { font-size: 1rem; width: 1rem; height: 1rem; }
    .delta.up { background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .delta.down { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .fall { display: inline-flex; align-items: center; gap: 0.25rem; padding: 0.25rem 0.625rem; border-radius: 999px; background: var(--mat-sys-error-container); font: var(--mat-sys-label-large); color: var(--mat-sys-on-error-container); white-space: nowrap; }
    .chevron { color: var(--mat-sys-on-surface-variant); }
    .sub { margin: 0; padding: 0.625rem 1rem; border-top: 1px solid var(--mat-sys-outline-variant); background: var(--mat-sys-surface-container); font: var(--mat-sys-label-large); color: var(--mat-sys-on-surface-variant); }
    .line { display: block; margin: 0; padding: 0.875rem 1rem; font: var(--mat-sys-body-medium); }
  `,
})
export class ExamsWidget {
  private readonly api = inject(DashboardService);
  private readonly i18n = inject(TranslateService);
  protected readonly state = new Loadable<ExamProgress>();
  protected readonly date = formatDate;
  protected readonly percent = formatPercent;
  protected readonly change = averageChange;
  protected readonly abs = Math.abs;

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.load(this.api.examProgress());
  }

  protected marking(c: ExamProgressClass): string {
    return this.i18n.format('dashboard.exams.marking', { marked: c.marked, students: c.students });
  }

  protected more(p: ExamProgress): string {
    return this.i18n.format('dashboard.more', { count: p.droppedTotal - p.dropped.length });
  }
}
