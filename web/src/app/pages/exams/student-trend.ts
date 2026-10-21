import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ExamsService, StudentHistory } from '../../core/api/exams.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { formatDate } from '../../shared/format';
import { SearchSelect } from '../../shared/search-select';
import { TrendChart, TrendPoint } from '../../shared/trend-chart';
import { short } from './exam-analysis';
import { StudentAvatar } from '../../shared/student-avatar';

/** One student's results across every exam of a class: a chart against the class average, then each exam with grade and rank. */
@Component({
  selector: 'app-student-trend',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StudentAvatar, ReactiveFormsModule, RouterLink, SearchSelect, TrendChart, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    @if (examId()) {
      <a mat-button [routerLink]="['/exams', examId()]" class="back">
        <mat-icon>arrow_back</mat-icon>
        {{ 'exams.back_to_exam' | t }}
      </a>
    } @else {
      <a mat-button routerLink="/exams" [queryParams]="{ classId: history()?.classId }" class="back">
        <mat-icon>arrow_back</mat-icon>
        {{ 'exams.title' | t }}
      </a>
    }

    @switch (record.status()) {
      @case ('ready') {
        @if (history(); as h) {
          <header class="page-header">
            <div class="who">
              <app-student-avatar [name]="h.fullName" [of]="h.studentId" size="lg" />
              <div>
                <h1 class="page-title">{{ h.fullName }}</h1>
                <p class="page-subtitle">#{{ h.indexNumber }} · {{ h.classTitle }}</p>
              </div>
            </div>
          </header>

          @if (h.classes.length > 1) {
            <app-search-select [formControl]="classControl" [label]="'exams.class' | t" [options]="classOptions()" />
          }

          @if (h.exams.length === 0) {
            <div class="empty">
              <mat-icon>quiz</mat-icon>
              <p>{{ 'exams.trend_none' | t }}</p>
            </div>
          } @else {
            <div class="totals" role="group" [attr.aria-label]="'exams.trend' | t">
              <div class="total"><strong>{{ overall() === null ? '–' : overall() + '%' }}</strong><span>{{ 'exams.average' | t }}</span></div>
              <div class="total"><strong>{{ best() === null ? '–' : best() + '%' }}</strong><span>{{ 'exams.best' | t }}</span></div>
            </div>

            <section class="chart" [attr.aria-label]="'exams.trend' | t">
              <app-trend-chart [points]="points()" [ownLabel]="h.fullName" [averageLabel]="'exams.class_average' | t" />
            </section>

            <div class="panel">
              @for (e of newestFirst(); track e.examId) {
                <a class="panel-row link" [routerLink]="['/exams', e.examId]">
                  <span class="avatar rank" aria-hidden="true">{{ e.rank ?? '–' }}</span>
                  <div class="row-main">
                    <div class="row-title">{{ e.name }}</div>
                    <div class="row-meta">{{ date(e.heldOn) }}</div>
                    <div class="row-meta">
                      @if (e.absent) {
                        {{ 'exams.absent' | t }}
                      } @else if (e.percent !== null) {
                        {{ num(e.total!) }} / {{ num(e.maxTotal) }} · {{ e.percent }}% · <span class="nowrap">{{ 'exams.rank' | t }} {{ e.rank }} {{ 'exams.of' | t }} {{ e.ranked }}</span>
                      } @else {
                        {{ 'exams.not_complete' | t }}
                      }
                    </div>
                    @if (e.classAverage !== null) {
                      <div class="row-meta">{{ 'exams.class_average' | t }} {{ e.classAverage }}%</div>
                    }
                  </div>
                  @if (e.grade) {
                    <span class="grade-chip">{{ e.grade }}</span>
                  }
                  <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
                </a>
              }
            </div>
          }
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
  `,
  styles: `
    .who { display: flex; align-items: center; gap: 1rem; min-width: 0; }
    .who > div { min-width: 0; }
    .back { margin-left: -0.75rem; margin-bottom: 0.5rem; }
    .totals { display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem; margin-bottom: 1rem; }
    .total { display: grid; gap: 0.125rem; padding: 0.75rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); }
    .total strong { font: var(--mat-sys-headline-small); }
    .total span { font: var(--mat-sys-label-medium); color: var(--mat-sys-on-surface-variant); }
    .chart { margin-bottom: 1rem; padding: 0.75rem 0.75rem 0.25rem; border: 1px solid var(--mat-sys-outline-variant); border-radius: var(--mat-sys-corner-medium); }
    .avatar.rank { font-weight: 600; }
    .nowrap { white-space: nowrap; }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .chevron { flex: none; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class StudentTrend {
  /** The enrolment from the route; the exam to go back to from the ?examId= query parameter. */
  readonly enrolmentId = input.required<string>();
  readonly examId = input<string>('');

  private readonly api = inject(ExamsService);
  private readonly router = inject(Router);
  private readonly i18n = inject(TranslateService);

  protected readonly record = new Loadable<StudentHistory>();
  protected readonly history = computed(() => this.record.data());
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly classOptions = computed(() => (this.history()?.classes ?? []).map((c) => ({ value: c.enrolmentId, label: c.title })));
  protected readonly date = formatDate;
  protected readonly num = short;

  protected readonly points = computed<TrendPoint[]>(() =>
    (this.history()?.exams ?? []).map((e) => ({
      label: e.name,
      detail: `${formatDate(e.heldOn)}${e.absent ? ' · ' + this.i18n.translate('exams.absent') : ''}`,
      value: e.percent,
      average: e.classAverage,
    })),
  );
  protected readonly newestFirst = computed(() => [...(this.history()?.exams ?? [])].reverse());
  private readonly percents = computed(() => (this.history()?.exams ?? []).map((e) => e.percent).filter((p): p is number => p !== null));
  protected readonly overall = computed(() => {
    const list = this.percents();
    return list.length === 0 ? null : short(list.reduce((a, b) => a + b, 0) / list.length);
  });
  protected readonly best = computed(() => (this.percents().length === 0 ? null : Math.max(...this.percents())));

  constructor() {
    effect(() => this.load());

    this.classControl.valueChanges.subscribe((enrolmentId) => {
      if (enrolmentId && enrolmentId !== this.enrolmentId()) {
        void this.router.navigate(['/exams/student', enrolmentId], { replaceUrl: true });
      }
    });
    effect(() => {
      const id = this.enrolmentId();
      if (this.classControl.value !== id) this.classControl.setValue(id, { emitEvent: false });
    });
  }

  protected load(): void {
    this.record.load(this.api.history(this.enrolmentId()));
  }
}
