import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { catchError, combineLatest, EMPTY, startWith, switchMap, tap } from 'rxjs';
import { AttendanceService, ClassSummary } from '../../core/api/attendance.service';
import { ClassesService } from '../../core/api/classes.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { monthStart, todayColombo } from '../../shared/format';
import { SearchSelect, SelectOption } from '../../shared/search-select';

/** How often each student of a class came over a period (this month to start with). Late counts as attended. */
@Component({
  selector: 'app-attendance-summary',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, SearchSelect, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslatePipe],
  template: `
    <a mat-button routerLink="/attendance" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'attendance.title' | t }}
    </a>
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'attendance.summary' | t }}</h1>
        <p class="page-subtitle">{{ 'attendance.summary_subtitle' | t }}</p>
      </div>
    </header>

    <div class="filters">
      <app-search-select class="class-filter" [formControl]="classControl" [label]="'attendance.class' | t" [options]="classOptions()" />
      <mat-form-field class="date">
        <mat-label>{{ 'attendance.from' | t }}</mat-label>
        <input matInput type="date" [formControl]="from" [max]="to.value" />
      </mat-form-field>
      <mat-form-field class="date">
        <mat-label>{{ 'attendance.to' | t }}</mat-label>
        <input matInput type="date" [formControl]="to" [min]="from.value" />
      </mat-form-field>
    </div>

    @if (!chosen()) {
      <div class="empty">
        <mat-icon>insights</mat-icon>
        <p>{{ 'attendance.pick_class' | t }}</p>
      </div>
    } @else {
      @switch (summary.status()) {
        @case ('ready') {
          @if (summary.data(); as data) {
            @if (data.sessions === 0) {
              <div class="empty">
                <mat-icon>event_busy</mat-icon>
                <p>{{ 'attendance.summary_none' | t }}</p>
              </div>
            } @else {
              <p class="overall">
                <strong>{{ data.percent }}%</strong>
                <span class="muted">{{ 'attendance.overall' | t }} · {{ 'attendance.sessions' | t }}: {{ data.sessions }}</span>
              </p>
              <div class="panel">
                @for (row of data.students; track row.enrolmentId) {
                  <div class="panel-row student">
                    <span class="avatar" aria-hidden="true">{{ row.indexNumber }}</span>
                    <div class="row-main">
                      <div class="row-title">{{ row.fullName }}</div>
                      <div class="row-meta">
                        {{ 'attendance.present' | t }} {{ row.present }} · {{ 'attendance.late' | t }} {{ row.late }} · {{ 'attendance.absent' | t }} {{ row.absent }}
                      </div>
                      <div class="bar" role="img" [attr.aria-label]="row.percent + '%'"><span [style.width.%]="row.percent"></span></div>
                    </div>
                    <span class="percent" [class.low]="row.sessions > 0 && row.percent < 75">{{ row.sessions > 0 ? row.percent + '%' : '–' }}</span>
                  </div>
                } @empty {
                  <div class="panel-row muted">{{ 'attendance.no_students' | t }}</div>
                }
              </div>
            }
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
    }
  `,
  styles: `
    .back { margin-left: -0.75rem; margin-bottom: 0.5rem; }
    .filters { display: flex; flex-wrap: wrap; gap: 0 1rem; align-items: flex-start; }
    .class-filter { flex: 2 1 16rem; }
    .date { flex: 1 1 9rem; }
    .overall { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: baseline; margin: 0 0 0.75rem; }
    .overall strong { font: var(--mat-sys-headline-small); }
    .bar { height: 0.375rem; margin-top: 0.375rem; border-radius: 999px; background: var(--mat-sys-surface-container-highest); overflow: hidden; }
    .bar span { display: block; height: 100%; background: var(--mat-sys-primary); }
    .percent { font: var(--mat-sys-title-medium); font-weight: 600; }
    .percent.low { color: var(--mat-sys-error); }
  `,
})
export class AttendanceSummary {
  /** Optional class to start on, from the ?classId= query parameter. */
  readonly classId = input<string>('');

  private readonly api = inject(AttendanceService);

  protected readonly summary = new Loadable<ClassSummary>();
  protected readonly classOptions = signal<SelectOption[]>([]);
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly from = new FormControl(monthStart(todayColombo()), { nonNullable: true });
  protected readonly to = new FormControl(todayColombo(), { nonNullable: true });
  private readonly classValue = toSignal(this.classControl.valueChanges, { initialValue: '' });
  private readonly refresh = signal(0);

  /** The class chosen, from the page address at first and from the dropdown after that. */
  protected readonly chosen = computed(() => this.classValue() || this.classId());

  private readonly criteria$ = combineLatest([
    toObservable(this.chosen),
    this.from.valueChanges.pipe(startWith(this.from.value)),
    this.to.valueChanges.pipe(startWith(this.to.value)),
    toObservable(this.refresh),
  ]);

  constructor() {
    inject(ClassesService)
      .classes('all')
      .subscribe((list) => {
        this.classOptions.set(list.map((c) => ({ value: c.id, label: c.title })));
        if (this.classId() && !this.classControl.value) this.classControl.setValue(this.classId());
      });

    this.criteria$
      .pipe(
        switchMap(([classId, from, to]) => {
          if (!classId || !from || !to || from > to) return EMPTY;
          this.summary.status.set('loading');
          return this.api.summary(classId, from, to).pipe(
            tap((data) => {
              this.summary.data.set(data);
              this.summary.status.set('ready');
            }),
            catchError((e) => {
              this.summary.error.set(e);
              this.summary.status.set('error');
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe();
  }

  protected reload(): void {
    this.refresh.update((n) => n + 1);
  }
}
