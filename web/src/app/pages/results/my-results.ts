import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { forkJoin } from 'rxjs';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ExamsService, MyExam } from '../../core/api/exams.service';
import { MyClass, StudentsService } from '../../core/api/students.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { formatDate } from '../../shared/format';
import { short } from '../exams/exam-analysis';

interface ClassResults {
  classId: string;
  title: string;
  teacher: string | null;
  exams: MyExam[];
}

/** A student's own published results, class by class, newest first: marks for each part, total, grade and rank. */
@Component({
  selector: 'app-my-results',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <a mat-button routerLink="/welcome" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'welcome.my_classes' | t }}
    </a>
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'results.title' | t }}</h1>
        <p class="page-subtitle">{{ 'results.subtitle' | t }}</p>
      </div>
    </header>

    @switch (data.status()) {
      @case ('ready') {
        @for (group of groups(); track group.classId) {
          <h2 class="section-title" [id]="'class-' + group.classId">{{ group.title }}</h2>
          @if (group.teacher) {
            <p class="muted teacher">{{ group.teacher }}</p>
          }
          <div class="panel">
            @for (e of group.exams; track e.examId) {
              <div class="panel-row exam">
                <div class="row-main">
                  <div class="row-title">{{ e.name }}</div>
                  <div class="row-meta">{{ date(e.heldOn) }}</div>
                  <div class="row-meta">
                    @if (e.absent) {
                      {{ 'exams.absent' | t }}
                    } @else if (e.percent !== null) {
                      <strong class="score">{{ num(e.total!) }} / {{ num(e.maxTotal) }}</strong> · {{ e.percent }}%
                      @if (e.rank !== null) {
                        · <span class="nowrap">{{ 'exams.rank' | t }} {{ e.rank }} {{ 'exams.of' | t }} {{ e.ranked }}</span>
                      }
                    } @else {
                      {{ 'results.not_complete' | t }}
                    }
                  </div>
                  @if (!e.absent && e.parts.length > 1) {
                    <div class="parts">
                      @for (p of e.parts; track $index) {
                        <span class="part">{{ p.name }} <strong>{{ p.mark === null ? '–' : num(p.mark) }}</strong>/{{ num(p.maxMark) }}</span>
                      }
                    </div>
                  }
                </div>
                @if (e.grade) {
                  <span class="grade-chip" [attr.aria-label]="('exams.grade' | t) + ' ' + e.grade">{{ e.grade }}</span>
                }
              </div>
            }
          </div>
        } @empty {
          <div class="empty">
            <mat-icon>quiz</mat-icon>
            <p>{{ 'results.none' | t }}</p>
          </div>
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
    .back { margin-left: -0.75rem; margin-bottom: 0.5rem; }
    .teacher { margin: -0.5rem 0 0.5rem; font: var(--mat-sys-body-small); }
    .panel { margin-bottom: 1rem; }
    .exam { align-items: flex-start; }
    .score { color: var(--mat-sys-on-surface); }
    .nowrap { white-space: nowrap; }
    .parts { display: flex; flex-wrap: wrap; gap: 0.375rem; margin-top: 0.375rem; }
    .part { padding: 0.125rem 0.5rem; border-radius: var(--mat-sys-corner-small); background: var(--mat-sys-surface-container-high); font: var(--mat-sys-label-medium); color: var(--mat-sys-on-surface-variant); }
    .part strong { color: var(--mat-sys-on-surface); }
    .grade-chip { min-width: 2.25rem; padding: 0.375rem 0.75rem; border-radius: 999px; background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); font: var(--mat-sys-title-medium); text-align: center; }
  `,
})
export class MyResults {
  /** Optional class to scroll to, from the ?classId= query parameter. */
  readonly classId = input<string>('');

  private readonly exams = inject(ExamsService);
  private readonly students = inject(StudentsService);

  protected readonly data = new Loadable<{ classes: MyClass[]; exams: MyExam[] }>();
  protected readonly date = formatDate;
  protected readonly num = short;

  /** Classes that have published results; the class asked for comes first. */
  protected readonly groups = computed<ClassResults[]>(() => {
    const value = this.data.data();
    if (!value) return [];
    const groups = value.classes
      .map((c) => ({ classId: c.classId, title: c.classTitle, teacher: c.teacherName, exams: value.exams.filter((e) => e.classId === c.classId) }))
      .filter((g) => g.exams.length > 0);
    const first = this.classId();
    return first ? [...groups.filter((g) => g.classId === first), ...groups.filter((g) => g.classId !== first)] : groups;
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.data.load(forkJoin({ classes: this.students.myClasses(), exams: this.exams.mine() }));
  }
}
