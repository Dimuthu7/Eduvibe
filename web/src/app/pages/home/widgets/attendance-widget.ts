import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { DashboardService, Watch, WatchRow } from '../../../core/api/dashboard.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslateService } from '../../../core/i18n/translate.service';
import { Loadable } from '../../../core/state/loadable';
import { formatDate } from '../../../shared/format';
import { WidgetCard } from './widget-card';
import { WidgetState } from './widget-state';

/** Students who have been missing classes, so the teacher can follow them up before it becomes a habit. */
@Component({
  selector: 'app-attendance-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatIconModule, TranslatePipe, WidgetCard, WidgetState],
  template: `
    <app-widget-card icon="event_busy" title="dashboard.attendance.title" [count]="state.data()?.total ?? 0" link="/attendance/summary" linkLabel="dashboard.attendance.summary">
      <app-widget-state [status]="state.status()" (retry)="load()">
        @if (state.data(); as watch) {
          @for (row of watch.rows; track row.enrolmentId) {
            <a class="panel-row link" [routerLink]="['/attendance/summary/student', row.enrolmentId]">
              <div class="row-main">
                <div class="row-title">{{ row.fullName }}</div>
                <div class="row-meta">{{ row.classTitle }} · {{ lastCame(row) }}</div>
                <div class="why">
                  @if (row.missedInARow >= 2) {
                    <span class="chip alert">{{ missed(row) }}</span>
                  }
                  @if (low(row)) {
                    <span class="chip">{{ rate(row) }}</span>
                  }
                </div>
              </div>
              <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
            </a>
          } @empty {
            <p class="line muted">{{ 'dashboard.attendance.empty' | t }}</p>
          }
          @if (watch.total > watch.rows.length) {
            <a class="line more" routerLink="/attendance/summary">{{ more(watch) }}</a>
          }
        }
      </app-widget-state>
    </app-widget-card>
  `,
  styles: `
    mat-icon { font-size: 1.5rem; }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .why { display: flex; flex-wrap: wrap; gap: 0.375rem; margin-top: 0.375rem; }
    .chip { padding: 0.0625rem 0.5rem; border-radius: 999px; background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); font: var(--mat-sys-label-small); white-space: nowrap; }
    .chip.alert { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .chevron { color: var(--mat-sys-on-surface-variant); }
    .line { display: block; margin: 0; padding: 0.875rem 1rem; font: var(--mat-sys-body-medium); }
    .more { border-top: 1px solid var(--mat-sys-outline-variant); color: var(--mat-sys-primary); text-decoration: none; }
  `,
})
export class AttendanceWidget {
  private readonly api = inject(DashboardService);
  private readonly i18n = inject(TranslateService);
  protected readonly state = new Loadable<Watch>();

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.load(this.api.attendanceWatch());
  }

  protected missed(row: WatchRow): string {
    return this.i18n.format('dashboard.attendance.missed', { count: row.missedInARow });
  }

  /** The rate is a second reason only when it is below what the server flags; a student who is simply missing lately shows just the streak. */
  protected low(row: WatchRow): boolean {
    return row.sessions >= 4 && row.percent < 75;
  }

  protected rate(row: WatchRow): string {
    return this.i18n.format('dashboard.attendance.rate', { percent: row.percent });
  }

  protected lastCame(row: WatchRow): string {
    return row.lastAttended
      ? this.i18n.format('dashboard.attendance.last_came', { date: formatDate(row.lastAttended) })
      : this.i18n.translate('dashboard.attendance.not_lately');
  }

  protected more(watch: Watch): string {
    return this.i18n.format('dashboard.more', { count: watch.total - watch.rows.length });
  }
}
