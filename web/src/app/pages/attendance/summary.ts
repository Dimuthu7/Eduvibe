import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, linkedSignal, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { catchError, EMPTY, switchMap, tap } from 'rxjs';
import { AttendanceService, ClassSummary } from '../../core/api/attendance.service';
import { ClassesService } from '../../core/api/classes.service';
import { ReportsService } from '../../core/api/reports.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { fileNamePart, saveBlob } from '../../shared/download';
import { monthRange, todayColombo } from '../../shared/format';
import { DateRange, PeriodPicker } from '../../shared/period-picker';
import { SearchSelect, SelectOption } from '../../shared/search-select';

/** How often each student of a class came over a period (this month to start with). Late counts as attended. */
@Component({
  selector: 'app-attendance-summary',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, PeriodPicker, SearchSelect, MatButtonModule, MatIconModule, TranslatePipe],
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
      @if (chosen()) {
        <button mat-stroked-button type="button" (click)="exportExcel()" [disabled]="exporting()">
          <mat-icon>download</mat-icon>
          {{ 'attendance.export' | t }}
        </button>
      }
    </header>

    <app-search-select [formControl]="classControl" [label]="'attendance.class' | t" [options]="classOptions()" />
    <app-period-picker [from]="range().from" [to]="range().to" (rangeChange)="range.set($event)" />

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
                  <a
                    class="panel-row student link"
                    [routerLink]="['/attendance/summary/student', row.enrolmentId]"
                    [queryParams]="{ from: range().from, to: range().to }"
                  >
                    <span class="avatar" aria-hidden="true">{{ row.indexNumber }}</span>
                    <div class="row-main">
                      <div class="row-title">{{ row.fullName }}</div>
                      <div class="row-meta">
                        {{ 'attendance.present' | t }} {{ row.present }} · {{ 'attendance.late' | t }} {{ row.late }} · {{ 'attendance.absent' | t }} {{ row.absent }}
                      </div>
                      <div class="bar" role="img" [attr.aria-label]="row.percent + '%'"><span [style.width.%]="row.percent"></span></div>
                    </div>
                    <span class="percent" [class.low]="row.sessions > 0 && row.percent < 75">{{ row.sessions > 0 ? row.percent + '%' : '–' }}</span>
                    <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
                  </a>
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
    .overall { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: baseline; margin: 0 0 0.75rem; }
    .overall strong { font: var(--mat-sys-headline-small); }
    .bar { height: 0.375rem; margin-top: 0.375rem; border-radius: 999px; background: var(--mat-sys-surface-container-highest); overflow: hidden; }
    .bar span { display: block; height: 100%; background: var(--mat-sys-primary); }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .chevron { color: var(--mat-sys-on-surface-variant); }
    .percent { font: var(--mat-sys-title-medium); font-weight: 600; }
    .percent.low { color: var(--mat-sys-error); }
  `,
})
export class AttendanceSummary {
  /** Optional class and dates to start on, from the query parameters (this month when there are no dates). */
  readonly classId = input<string>('');
  readonly fromParam = input<string>('', { alias: 'from' });
  readonly toParam = input<string>('', { alias: 'to' });

  private readonly api = inject(AttendanceService);

  private readonly reports = inject(ReportsService);
  private readonly i18n = inject(TranslateService);
  protected readonly summary = new Loadable<ClassSummary>();
  protected readonly exporting = signal(false);
  protected readonly classOptions = signal<SelectOption[]>([]);
  protected readonly classControl = new FormControl('', { nonNullable: true });
  private readonly today = todayColombo();
  /** The period shown: the dates from the address at first, then whatever the teacher picks. Defaults to this month so far. */
  protected readonly range = linkedSignal<DateRange>(() => ({
    from: this.fromParam() || monthRange(this.today.slice(0, 7), this.today).from,
    to: this.toParam() || this.today,
  }));
  private readonly classValue = toSignal(this.classControl.valueChanges, { initialValue: '' });
  private readonly refresh = signal(0);

  /** The class chosen, from the page address at first and from the dropdown after that. */
  protected readonly chosen = computed(() => this.classValue() || this.classId());

  private readonly criteria$ = toObservable(computed(() => ({ classId: this.chosen(), ...this.range(), refresh: this.refresh() })));

  constructor() {
    inject(ClassesService)
      .classes('all')
      .subscribe((list) => {
        this.classOptions.set(list.map((c) => ({ value: c.id, label: c.title })));
        if (this.classId() && !this.classControl.value) this.classControl.setValue(this.classId());
      });

    this.criteria$
      .pipe(
        switchMap(({ classId, from, to }) => {
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

  /** Saves the register for the class and dates on screen as an Excel file: a column per class held, P, L or A for each student. */
  protected exportExcel(): void {
    const { from, to } = this.range();
    const title = this.classOptions().find((o) => o.value === this.chosen())?.label ?? '';
    this.exporting.set(true);
    this.reports.download('attendance', { classId: this.chosen(), from, to }).subscribe({
      next: (file) => {
        this.exporting.set(false);
        saveBlob(file, `${fileNamePart(`${this.i18n.translate('attendance.title')} ${title} ${from} ${to}`)}.xlsx`);
      },
      error: () => this.exporting.set(false),
    });
  }

  protected reload(): void {
    this.refresh.update((n) => n + 1);
  }
}
