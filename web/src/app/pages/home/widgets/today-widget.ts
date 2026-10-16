import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { catchError, forkJoin, map, of } from 'rxjs';
import { ClassSession } from '../../../core/api/attendance.service';
import { DashboardService } from '../../../core/api/dashboard.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslateService } from '../../../core/i18n/translate.service';
import { Loadable } from '../../../core/state/loadable';
import { colomboNow, formatTime, todayColombo } from '../../../shared/format';
import { nextClass, NextClass, SessionState, sessionStates } from './next-class';
import { WidgetCard } from './widget-card';
import { WidgetState } from './widget-state';

interface TodayData {
  sessions: ClassSession[];
  states: SessionState[];
  /** The next weekly class, only when nothing is left to start today. */
  later: NextClass | null;
}

/** Today's classes with a one-tap way to mark attendance, and the next class once today is done. */
@Component({
  selector: 'app-today-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatIconModule, TranslatePipe, WidgetCard, WidgetState],
  template: `
    <app-widget-card icon="event" title="dashboard.today.title" link="/timetable" linkLabel="dashboard.today.timetable">
      <app-widget-state [status]="state.status()" (retry)="load()">
        @if (state.data(); as data) {
          @for (s of data.sessions; track s.id; let i = $index) {
            <a class="panel-row link" [routerLink]="['/attendance', s.id]" [class.over]="data.states[i] === 'over' && s.taken">
              <div class="time">
                <strong>{{ time(s.start) }}</strong>
                <span class="muted">{{ time(s.end) }}</span>
              </div>
              <div class="row-main">
                <div class="row-title">{{ s.classTitle }}</div>
                <div class="row-meta">
                  @if (data.states[i] === 'now') {
                    <span class="chip now">{{ 'dashboard.today.now' | t }}</span>
                  } @else if (data.states[i] === 'next') {
                    <span class="chip">{{ 'dashboard.today.next' | t }}</span>
                  }
                  @if (s.taken) {
                    {{ 'attendance.present' | t }} {{ s.present + s.late }} · {{ 'attendance.absent' | t }} {{ s.absent }}
                  } @else if (s.isExtra && s.note) {
                    {{ s.note }}
                  } @else {
                    {{ 'attendance.not_marked' | t }}
                  }
                </div>
              </div>
              @if (s.taken) {
                <mat-icon class="done" aria-hidden="true">check_circle</mat-icon>
              } @else if (data.states[i] === 'later' || data.states[i] === 'next') {
                <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
              } @else {
                <span class="mark">{{ 'attendance.mark' | t }}</span>
              }
            </a>
          } @empty {
            <p class="line muted">{{ 'home.no_classes_today' | t }}</p>
          }
          @if (data.later; as n) {
            <p class="line">
              <mat-icon aria-hidden="true">schedule</mat-icon>
              <span>{{ nextText(n) }}</span>
            </p>
          }
        }
      </app-widget-state>
    </app-widget-card>
  `,
  styles: `
    mat-icon { font-size: 1.5rem; }
    .time { display: flex; flex-direction: column; min-width: 4.5rem; font: var(--mat-sys-body-small); }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .over { opacity: 0.8; }
    .done { color: var(--mat-sys-primary); }
    .chevron { color: var(--mat-sys-on-surface-variant); }
    .mark { color: var(--mat-sys-primary); font: var(--mat-sys-label-large); }
    .chip { margin-right: 0.375rem; padding: 0.0625rem 0.5rem; border-radius: 999px; background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); font: var(--mat-sys-label-small); vertical-align: middle; }
    .chip.now { background: var(--mat-sys-primary); color: var(--mat-sys-on-primary); }
    .line { display: flex; align-items: center; gap: 0.5rem; margin: 0; padding: 0.875rem 1rem; font: var(--mat-sys-body-medium); }
    .line + .panel-row, .panel-row + .line { border-top: 1px solid var(--mat-sys-outline-variant); }
    .line mat-icon { flex: none; font-size: 1.25rem; width: 1.25rem; height: 1.25rem; color: var(--mat-sys-primary); }
  `,
})
export class TodayWidget {
  private readonly api = inject(DashboardService);
  private readonly i18n = inject(TranslateService);
  protected readonly state = new Loadable<TodayData>();
  protected readonly time = formatTime;

  constructor() {
    this.load();
  }

  protected load(): void {
    const now = new Date();
    const clock = colomboNow(now);
    this.state.load(
      forkJoin({
        sessions: this.api.sessions(todayColombo(now)),
        // The timetable only adds the "next class" line, so a failure there must not hide today's classes.
        timetable: this.api.timetable().pipe(catchError(() => of([]))),
      }).pipe(
        map(({ sessions, timetable }) => {
          const states = sessionStates(sessions, clock.minutes);
          const anyLeft = states.some((s) => s !== 'over');
          return { sessions, states, later: anyLeft ? null : nextClass(timetable, clock) };
        }),
      ),
    );
  }

  protected nextText(n: NextClass): string {
    const when =
      n.daysAhead === 0
        ? this.i18n.translate('dashboard.today.later_today')
        : n.daysAhead === 1
          ? this.i18n.translate('dashboard.today.tomorrow')
          : this.i18n.translate(`day.${n.entry.day}`);
    return this.i18n.format('dashboard.today.next_class', { when, time: formatTime(n.entry.start), class: n.entry.title });
  }
}
