import { ChangeDetectionStrategy, Component, effect, inject, input, OnInit, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { ClassesService } from '../../core/api/classes.service';
import { ExamListItem, ExamsService } from '../../core/api/exams.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { formatDate } from '../../shared/format';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { openExamDialog } from './exam-dialog';

/** The class value that means "every class" in the dropdown. */
const ALL_CLASSES = 'all';

/** The teacher's exams, newest first, for every class or one. Making an exam goes straight on to entering its marks. */
@Component({
  selector: 'app-exams',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, SearchSelect, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'exams.title' | t }}</h1>
        <p class="page-subtitle">{{ 'exams.subtitle' | t }}</p>
      </div>
      <div class="row-wrap">
        <a mat-stroked-button routerLink="/exams/grading">
          <mat-icon>tune</mat-icon>
          {{ 'exams.grading' | t }}
        </a>
        <button mat-flat-button type="button" (click)="create()">
          <mat-icon>add</mat-icon>
          {{ 'exams.new' | t }}
        </button>
      </div>
    </header>

    <app-search-select [formControl]="classControl" [label]="'attendance.class' | t" [options]="classOptions()" />

    @switch (exams.status()) {
      @case ('ready') {
        @if (exams.data(); as list) {
          @if (list.length === 0) {
            <div class="empty">
              <mat-icon>quiz</mat-icon>
              <p>{{ 'exams.none' | t }}</p>
              <button mat-flat-button type="button" (click)="create()">{{ 'exams.new' | t }}</button>
            </div>
          } @else {
            <div class="panel">
              @for (e of list; track e.id) {
                <a class="panel-row link" [routerLink]="['/exams', e.id]">
                  <span class="avatar" aria-hidden="true"><mat-icon>quiz</mat-icon></span>
                  <div class="row-main">
                    <div class="row-title">{{ e.name }}</div>
                    <div class="row-meta">{{ e.classTitle }} · {{ date(e.heldOn) }}</div>
                    <div class="row-meta">{{ 'exams.marked_of' | t }} {{ e.marked }} / {{ e.students }} · {{ 'exams.out_of' | t }} {{ e.maxTotal }}</div>
                    <div class="row-meta state" [class.on]="!!e.publishedAt">
                      <mat-icon aria-hidden="true">{{ e.publishedAt ? 'visibility' : 'visibility_off' }}</mat-icon>
                      {{ (e.publishedAt ? 'exams.state_published' : 'exams.state_draft') | t }}
                    </div>
                  </div>
                  <div class="average">
                    <strong>{{ e.averagePercent === null ? '–' : e.averagePercent + '%' }}</strong>
                    <span class="muted">{{ 'exams.average' | t }}</span>
                  </div>
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
    .state { display: flex; gap: 0.25rem; align-items: center; }
    .state mat-icon { width: 1rem; height: 1rem; font-size: 1rem; }
    .state.on { color: var(--mat-sys-primary); }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .chevron { color: var(--mat-sys-on-surface-variant); }
    .average { display: grid; justify-items: end; }
    .average strong { font: var(--mat-sys-title-medium); }
    .average span { font: var(--mat-sys-label-small); }
  `,
})
export class Exams implements OnInit {
  /** Optional class to start on, from the ?classId= query parameter. */
  readonly classId = input<string>('');

  private readonly api = inject(ExamsService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(TranslateService);

  protected readonly exams = new Loadable<ExamListItem[]>();
  protected readonly classOptions = signal<SelectOption[]>([]);
  protected readonly classControl = new FormControl(ALL_CLASSES, { nonNullable: true });
  private readonly chosen = toSignal(this.classControl.valueChanges, { initialValue: ALL_CLASSES });
  protected readonly date = formatDate;

  constructor() {
    inject(ClassesService)
      .classes('all')
      .subscribe((list) =>
        this.classOptions.set([
          { value: ALL_CLASSES, label: this.i18n.translate('fees.all_classes') },
          ...list.map((c) => ({ value: c.id, label: c.title })),
        ]),
      );

    effect(() => {
      this.chosen();
      untracked(() => this.load());
    });
  }

  ngOnInit(): void {
    if (this.classId()) this.classControl.setValue(this.classId());
  }

  protected load(): void {
    const id = this.classControl.value;
    this.exams.load(this.api.list(id === ALL_CLASSES ? undefined : id));
  }

  protected create(): void {
    const id = this.classControl.value;
    openExamDialog(this.dialog, { classId: id === ALL_CLASSES ? undefined : id }).subscribe((exam) => {
      if (exam) void this.router.navigate(['/exams', exam.id]);
    });
  }
}
