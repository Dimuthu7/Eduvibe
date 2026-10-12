import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { catchError, combineLatest, EMPTY, switchMap, tap } from 'rxjs';
import { AttendanceService, ClassSession } from '../../core/api/attendance.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { addDays, formatDate, formatTime, todayColombo } from '../../shared/format';

/** The day's classes. Tap one to mark who came. The date moves with the arrows and is kept in the address. */
@Component({
  selector: 'app-attendance',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'attendance.title' | t }}</h1>
        <p class="page-subtitle">{{ 'attendance.subtitle' | t }}</p>
      </div>
      <div class="row-wrap">
        <a mat-stroked-button routerLink="/attendance/summary">
          <mat-icon>insights</mat-icon>
          {{ 'attendance.summary' | t }}
        </a>
      </div>
    </header>

    <nav class="day-nav" [attr.aria-label]="'attendance.day' | t">
      <button mat-icon-button type="button" (click)="go(-1)" [attr.aria-label]="'attendance.previous_day' | t">
        <mat-icon>chevron_left</mat-icon>
      </button>
      <div class="day">
        <strong>{{ label() }}</strong>
        @if (isToday()) {
          <span class="tag">{{ 'timetable.today' | t }}</span>
        }
      </div>
      <button mat-icon-button type="button" (click)="go(1)" [attr.aria-label]="'attendance.next_day' | t">
        <mat-icon>chevron_right</mat-icon>
      </button>
      @if (!isToday()) {
        <button mat-button type="button" (click)="goTo(today)">{{ 'timetable.today' | t }}</button>
      }
    </nav>

    @switch (sessions.status()) {
      @case ('ready') {
        @if ((sessions.data() ?? []).length > 0) {
          <div class="panel">
            @for (s of sessions.data(); track s.id) {
              <a class="panel-row link" [routerLink]="['/attendance', s.id]">
                <div class="time">
                  <strong>{{ time(s.start) }}</strong>
                  <span class="muted">{{ time(s.end) }}</span>
                </div>
                <div class="row-main">
                  <div class="row-title">{{ s.classTitle }}</div>
                  <div class="row-meta">
                    @if (s.taken) {
                      {{ 'attendance.present' | t }} {{ s.present + s.late }} · {{ 'attendance.absent' | t }} {{ s.absent }}
                    } @else {
                      {{ 'attendance.not_marked' | t }}
                    }
                    @if (s.isExtra) {
                      · {{ 'attendance.extra_tag' | t }}{{ s.note ? ': ' + s.note : '' }}
                    }
                  </div>
                </div>
                @if (s.taken) {
                  <mat-icon class="done" aria-hidden="true">check_circle</mat-icon>
                } @else if (s.editable) {
                  <span class="tag action">{{ 'attendance.mark' | t }}</span>
                }
                <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
              </a>
            }
          </div>
        } @else {
          <div class="empty">
            <mat-icon>event_available</mat-icon>
            <p>{{ emptyKey() | t }}</p>
          </div>
        }
      }
      @case ('error') {
        <div class="empty">
          <p>{{ 'error.load_failed' | t }}</p>
          <button mat-stroked-button type="button" (click)="reload()">{{ 'status.retry' | t }}</button>
        </div>
      }
      @default {
        <div class="skeleton"></div>
        <div class="skeleton"></div>
      }
    }
  `,
  styles: `
    .day-nav { display: flex; align-items: center; gap: 0.25rem; margin-bottom: 1rem; }
    .day { display: flex; flex: 1; flex-wrap: wrap; gap: 0.5rem; align-items: center; justify-content: center; min-width: 0; text-align: center; }
    .tag { padding: 0.125rem 0.625rem; border-radius: 999px; background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); font: var(--mat-sys-label-small); white-space: nowrap; }
    .tag.action { background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); }
    .time { display: flex; flex-direction: column; min-width: 4.5rem; font: var(--mat-sys-body-small); }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .chevron { color: var(--mat-sys-on-surface-variant); }
    .done { color: var(--mat-sys-primary); }
  `,
})
export class Attendance {
  /** Optional date from the ?date= query parameter, as yyyy-MM-dd. */
  readonly date = input<string>('');

  private readonly api = inject(AttendanceService);
  private readonly router = inject(Router);

  protected readonly today = todayColombo();
  protected readonly time = formatTime;
  protected readonly sessions = new Loadable<ClassSession[]>();
  protected readonly day = computed(() => (/^\d{4}-\d{2}-\d{2}$/.test(this.date()) ? this.date() : this.today));
  protected readonly isToday = computed(() => this.day() === this.today);
  protected readonly label = computed(() => formatDate(this.day()));
  protected readonly canAdd = computed(() => this.day() <= this.today && this.day() >= addDays(this.today, -7));
  protected readonly emptyKey = computed(() =>
    this.day() > this.today ? 'attendance.none_future' : this.canAdd() ? 'attendance.none' : 'attendance.none_old',
  );
  private readonly refresh = signal(0);

  constructor() {
    combineLatest([toObservable(this.day), toObservable(this.refresh)])
      .pipe(
        switchMap(([day]) => {
          this.sessions.status.set('loading');
          return this.api.sessions(day).pipe(
            tap((list) => {
              this.sessions.data.set(list);
              this.sessions.status.set('ready');
            }),
            catchError((e) => {
              this.sessions.error.set(e);
              this.sessions.status.set('error');
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe();
  }

  protected go(days: number): void {
    this.goTo(addDays(this.day(), days));
  }

  protected goTo(date: string): void {
    void this.router.navigate([], { queryParams: { date: date === this.today ? null : date }, replaceUrl: true });
  }

  protected reload(): void {
    this.refresh.update((n) => n + 1);
  }
}
