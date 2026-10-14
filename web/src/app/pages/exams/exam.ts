import { ChangeDetectionStrategy, Component, computed, effect, ElementRef, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ExamDetail, ExamPart, ExamsService, MarkRow, StudentResult } from '../../core/api/exams.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { parseMark, previewRow, RowPreview } from '../../shared/exam-calc';
import { formatDate } from '../../shared/format';
import { openExamDialog } from './exam-dialog';

interface Entry {
  student: StudentResult;
  absent: boolean;
  preview: RowPreview;
  /** Differs from what is saved. */
  dirty: boolean;
}

/**
 * One exam: a grid to type each student's marks (number keypad, Enter moves to the next box, Absent is separate from 0)
 * and, after saving, the results with grade and rank. Totals and grades show as the teacher types; ranks come from the server.
 */
@Component({
  selector: 'app-exam',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule, MatMenuModule, MatProgressSpinnerModule, TranslatePipe,
  ],
  template: `
    <a mat-button routerLink="/exams" [queryParams]="{ classId: detail()?.classId }" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'exams.title' | t }}
    </a>

    @switch (exam.status()) {
      @case ('ready') {
        @if (detail(); as d) {
          <header class="page-header">
            <div class="head-text">
              <h1 class="page-title">{{ d.name }}</h1>
              <p class="page-subtitle">{{ d.classTitle }} · {{ date(d.heldOn) }} · {{ 'exams.out_of' | t }} {{ d.maxTotal }}</p>
            </div>
            <div class="row-wrap">
              <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="'common.actions' | t">
                <mat-icon>more_vert</mat-icon>
              </button>
              <mat-menu #menu="matMenu">
                <button mat-menu-item type="button" (click)="edit(d)">
                  <mat-icon>edit</mat-icon>
                  <span>{{ 'exams.edit' | t }}</span>
                </button>
                <button mat-menu-item type="button" (click)="confirmDelete(d)">
                  <mat-icon>delete</mat-icon>
                  <span>{{ 'exams.delete' | t }}</span>
                </button>
              </mat-menu>
            </div>
          </header>

          <mat-button-toggle-group class="views" [value]="view()" (change)="view.set($event.value)" [hideSingleSelectionIndicator]="true" [attr.aria-label]="'exams.view' | t">
            <mat-button-toggle value="marks">{{ 'exams.marks' | t }}</mat-button-toggle>
            <mat-button-toggle value="results">{{ 'exams.results' | t }}</mat-button-toggle>
          </mat-button-toggle-group>

          @if (entries().length === 0) {
            <div class="empty">
              <mat-icon>groups</mat-icon>
              <p>{{ 'attendance.no_students' | t }}</p>
            </div>
          } @else if (view() === 'marks') {
            <p class="hint muted">{{ 'exams.marks_hint' | t }}</p>
            <div class="panel">
              @for (entry of entries(); track entry.student.enrolmentId) {
                <div class="panel-row entry" [class.absent]="entry.absent" [class.dirty]="entry.dirty">
                  <div class="who">
                    <span class="avatar" aria-hidden="true">{{ entry.student.indexNumber }}</span>
                    <div class="row-main">
                      <div class="row-title">{{ entry.student.fullName }}</div>
                      <div class="row-meta summary">
                        @if (entry.absent) {
                          {{ 'exams.absent' | t }}
                        } @else if (entry.preview.complete) {
                          <strong>{{ entry.preview.total }}</strong> / {{ d.maxTotal }} · {{ entry.preview.percent }}% ·
                          <strong class="grade">{{ entry.preview.grade }}</strong>
                          @if (!entry.dirty && entry.student.rank !== null) {
                            · <span class="nowrap">{{ 'exams.rank' | t }} {{ entry.student.rank }}</span>
                          }
                        } @else {
                          {{ 'exams.not_complete' | t }}
                        }
                      </div>
                    </div>
                    <button
                      mat-stroked-button
                      type="button"
                      class="absent-toggle"
                      [class.on]="entry.absent"
                      [attr.aria-pressed]="entry.absent"
                      (click)="toggleAbsent(entry.student)"
                    >
                      <mat-icon>{{ entry.absent ? 'check_box' : 'check_box_outline_blank' }}</mat-icon>
                      {{ 'exams.absent' | t }}
                    </button>
                  </div>
                  <div class="marks">
                    @for (part of d.parts; track part.id) {
                      <mat-form-field class="mark" appearance="outline" subscriptSizing="dynamic" [class.bad]="isBad(entry.student, part)">
                        <mat-label>{{ part.name }} / {{ part.maxMark }}</mat-label>
                        <input
                          matInput
                          class="mark-input"
                          inputmode="decimal"
                          enterkeyhint="next"
                          autocomplete="off"
                          [disabled]="entry.absent"
                          [value]="text(entry.student, part)"
                          [attr.aria-invalid]="isBad(entry.student, part)"
                          (input)="setText(entry.student, part, $any($event.target).value)"
                          (keydown.enter)="next($event)"
                          (focus)="$any($event.target).select()"
                        />
                      </mat-form-field>
                    }
                  </div>
                  @if (entry.preview.invalid && !entry.absent) {
                    <p class="problem" role="alert">{{ 'exams.mark_problem' | t }}</p>
                  }
                </div>
              }
            </div>

            <div class="savebar" role="region" [attr.aria-label]="'exams.save' | t">
              <span class="muted">
                @if (dirtyCount() > 0) {
                  {{ dirtyCount() }} {{ 'exams.changed' | t }}
                } @else {
                  {{ 'exams.all_saved' | t }}
                }
              </span>
              <button mat-flat-button type="button" (click)="save(d)" [disabled]="dirtyCount() === 0 || hasInvalid() || saving()" [attr.aria-busy]="saving()">
                @if (saving()) {
                  <mat-spinner diameter="18" />
                }
                {{ 'exams.save' | t }}
              </button>
            </div>
          } @else {
            <div class="totals" role="group" [attr.aria-label]="'exams.results' | t">
              <div class="total"><strong>{{ d.summary.marked }} / {{ d.summary.students }}</strong><span>{{ 'exams.marked_of' | t }}</span></div>
              <div class="total"><strong>{{ d.summary.averagePercent === null ? '–' : d.summary.averagePercent + '%' }}</strong><span>{{ 'exams.average' | t }}</span></div>
            </div>
            @if (dirtyCount() > 0) {
              <p class="hint" role="status"><mat-icon>info</mat-icon> {{ 'exams.unsaved_results' | t }}</p>
            }
            <div class="panel">
              @for (s of ranked(); track s.enrolmentId) {
                <div class="panel-row">
                  <span class="avatar rank" aria-hidden="true">{{ s.rank ?? '–' }}</span>
                  <div class="row-main">
                    <div class="row-title">{{ s.fullName }}</div>
                    <div class="row-meta">
                      @if (s.absent) {
                        {{ 'exams.absent' | t }}
                      } @else if (s.percent !== null) {
                        {{ s.total }} / {{ d.maxTotal }} · {{ s.percent }}%
                      } @else if (s.total !== null) {
                        {{ 'exams.not_complete' | t }} · {{ s.total }}
                      } @else {
                        {{ 'exams.not_entered' | t }}
                      }
                    </div>
                  </div>
                  @if (s.grade) {
                    <span class="grade-chip">{{ s.grade }}</span>
                  }
                </div>
              }
            </div>
          }
        }
      }
      @case ('error') {
        <div class="empty">
          <p>{{ 'exams.not_found' | t }}</p>
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
    .views { margin-bottom: 1rem; }
    .hint { display: flex; gap: 0.5rem; align-items: center; margin: 0 0 0.75rem; font: var(--mat-sys-body-medium); }
    .hint mat-icon { flex: none; }
    .entry { display: grid; gap: 0.5rem; align-items: stretch; }
    .entry.dirty { background: var(--mat-sys-surface-container-low); box-shadow: inset 0.25rem 0 0 var(--mat-sys-primary); }
    .who { display: flex; gap: 0.75rem; align-items: center; }
    .summary strong { color: var(--mat-sys-on-surface); }
    .summary .grade { color: var(--mat-sys-primary); }
    .absent-toggle { flex: none; --mat-button-outlined-container-height: 2.25rem; }
    .absent-toggle.on { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .marks { display: flex; flex-wrap: wrap; gap: 0.5rem; }
    .mark { flex: 1 1 6.5rem; min-width: 0; --mat-form-field-container-height: 2.75rem; --mat-form-field-container-vertical-padding: 0.5rem; }
    .mark.bad { --mat-form-field-outlined-outline-color: var(--mat-sys-error); --mat-form-field-outlined-hover-outline-color: var(--mat-sys-error); --mat-form-field-outlined-focus-outline-color: var(--mat-sys-error); --mat-form-field-outlined-label-text-color: var(--mat-sys-error); --mat-form-field-outlined-focus-label-text-color: var(--mat-sys-error); }
    .entry.absent .marks { opacity: 0.5; }
    .problem { margin: 0; color: var(--mat-sys-error); font: var(--mat-sys-body-small); }
    .savebar { position: sticky; bottom: 0; display: flex; gap: 1rem; align-items: center; justify-content: space-between; margin-top: 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-large); background: var(--mat-sys-surface-container-high); box-shadow: var(--mat-sys-level2); }
    .savebar mat-spinner { display: inline-block; margin-right: 0.5rem; vertical-align: middle; }
    .totals { display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem; margin-bottom: 1rem; }
    .total { display: grid; gap: 0.125rem; padding: 0.75rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); }
    .total strong { font: var(--mat-sys-headline-small); }
    .total span { font: var(--mat-sys-label-medium); color: var(--mat-sys-on-surface-variant); }
    .avatar.rank { font-weight: 600; }
    .head-text { flex: 1 1 12rem; min-width: 0; }
    .nowrap { white-space: nowrap; }
    .grade-chip { min-width: 2rem; padding: 0.25rem 0.625rem; border-radius: 999px; background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); font: var(--mat-sys-label-large); text-align: center; }
  `,
})
export class ExamScreen {
  /** The exam id from the route. */
  readonly id = input.required<string>();

  private readonly api = inject(ExamsService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly exam = new Loadable<ExamDetail>();
  protected readonly detail = computed(() => this.exam.data());
  protected readonly view = signal<'marks' | 'results'>('marks');
  protected readonly saving = signal(false);
  protected readonly date = formatDate;

  /** What is typed in each box, by student then part. Starts from the saved marks. */
  private readonly typed = signal<Record<string, Record<string, string>>>({});
  private readonly absent = signal<Record<string, boolean>>({});

  protected readonly entries = computed<Entry[]>(() => {
    const d = this.detail();
    if (!d) return [];
    return d.students.map((student) => {
      const typed = this.typed()[student.enrolmentId] ?? {};
      const absent = this.absent()[student.enrolmentId] ?? student.absent;
      return { student, absent, preview: previewRow(d.parts, typed, d.bands), dirty: this.isDirty(d, student, typed, absent) };
    });
  });

  protected readonly dirtyCount = computed(() => this.entries().filter((e) => e.dirty).length);
  protected readonly hasInvalid = computed(() => this.entries().some((e) => e.dirty && !e.absent && e.preview.invalid));

  /** The saved results, best rank first; students without a rank follow (unfinished, then absent, then nothing entered). */
  protected readonly ranked = computed(() => {
    const order = (s: StudentResult) => (s.rank !== null ? 0 : s.absent ? 2 : s.total !== null ? 1 : 3);
    return [...(this.detail()?.students ?? [])].sort(
      (a, b) => order(a) - order(b) || (a.rank ?? 0) - (b.rank ?? 0) || a.indexNumber - b.indexNumber,
    );
  });

  constructor() {
    queueMicrotask(() => this.load());
    // The boxes start from the saved marks, and again after every save.
    effect(() => {
      const d = this.detail();
      if (d) untracked(() => this.reset(d));
    });
  }

  protected load(): void {
    this.exam.load(this.api.exam(this.id()));
  }

  protected text(student: StudentResult, part: ExamPart): string {
    return this.typed()[student.enrolmentId]?.[part.id] ?? '';
  }

  protected isBad(student: StudentResult, part: ExamPart): boolean {
    return parseMark(this.text(student, part), part.maxMark) === 'invalid';
  }

  protected setText(student: StudentResult, part: ExamPart, value: string): void {
    this.typed.update((all) => ({ ...all, [student.enrolmentId]: { ...all[student.enrolmentId], [part.id]: value } }));
  }

  protected toggleAbsent(student: StudentResult): void {
    const current = this.absent()[student.enrolmentId] ?? student.absent;
    this.absent.update((all) => ({ ...all, [student.enrolmentId]: !current }));
  }

  /** Enter moves to the next mark box, down the class, so marks can be typed without touching the screen. */
  protected next(event: Event): void {
    const inputs = Array.from(this.host.nativeElement.querySelectorAll<HTMLInputElement>('input.mark-input:not(:disabled)'));
    const next = inputs[inputs.indexOf(event.target as HTMLInputElement) + 1];
    event.preventDefault();
    next?.focus();
  }

  protected save(d: ExamDetail): void {
    const rows: MarkRow[] = this.entries()
      .filter((e) => e.dirty)
      .map((e) => ({
        enrolmentId: e.student.enrolmentId,
        absent: e.absent,
        marks: e.absent
          ? {}
          : Object.fromEntries(
              d.parts.map((p) => {
                const mark = parseMark(this.text(e.student, p), p.maxMark);
                return [p.id, mark === 'invalid' ? null : mark];
              }),
            ),
      }));
    this.saving.set(true);
    this.api.saveMarks(d.id, rows).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.exam.data.set(saved);
        this.toast.success('exams.saved');
      },
      error: () => this.saving.set(false),
    });
  }

  protected edit(d: ExamDetail): void {
    // Saving the exam reloads the grid, which would drop marks typed but not yet saved.
    if (this.dirtyCount() > 0) {
      this.toast.error('exams.save_first');
      return;
    }

    openExamDialog(this.dialog, { exam: d }).subscribe((saved) => {
      if (saved) {
        this.exam.data.set(saved);
        this.toast.success('exams.saved');
      }
    });
  }

  protected confirmDelete(d: ExamDetail): void {
    this.dialog
      .open<ConfirmDialog, { title: string; message: string; confirm: string }, boolean>(ConfirmDialog, {
        data: { title: 'exams.delete_title', message: 'exams.delete_message', confirm: 'exams.delete' },
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((yes) => {
        if (!yes) return;
        this.api.remove(d.id).subscribe(() => {
          this.toast.success('exams.deleted');
          void this.router.navigate(['/exams'], { queryParams: { classId: d.classId } });
        });
      });
  }

  private reset(d: ExamDetail): void {
    const typed: Record<string, Record<string, string>> = {};
    const absent: Record<string, boolean> = {};
    for (const s of d.students) {
      typed[s.enrolmentId] = Object.fromEntries(d.parts.map((p) => [p.id, s.marks[p.id] === undefined ? '' : String(s.marks[p.id])]));
      absent[s.enrolmentId] = s.absent;
    }

    this.typed.set(typed);
    this.absent.set(absent);
  }

  private isDirty(d: ExamDetail, student: StudentResult, typed: Record<string, string>, absent: boolean): boolean {
    if (absent !== student.absent) return true;
    if (absent) return false;
    return d.parts.some((p) => {
      const mark = parseMark(typed[p.id] ?? '', p.maxMark);
      return mark === 'invalid' || mark !== (student.marks[p.id] ?? null);
    });
  }
}
