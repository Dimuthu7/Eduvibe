import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { AttendanceService, AttendanceStatus, SessionDetail, SessionStudent } from '../../core/api/attendance.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { formatDate, formatTime } from '../../shared/format';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { SubmitButton } from '../../shared/submit-button';

const ICONS: Record<AttendanceStatus, string> = { Present: 'check_circle', Late: 'schedule', Absent: 'cancel' };
const LONG_PRESS_MS = 500;
const MOVE_LIMIT_PX = 8;

/**
 * Taking attendance for one class. Everyone starts as present; tap a student to mark them absent, press and hold for late,
 * or use the menu beside the name. Nothing is stored until Save.
 */
@Component({
  selector: 'app-attendance-mark',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, SubmitButton, MatButtonModule, MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    @switch (detail.status()) {
      @case ('ready') {
        @if (session(); as s) {
          <a mat-button class="back" routerLink="/attendance" [queryParams]="{ date: s.date }">
            <mat-icon>arrow_back</mat-icon>
            {{ 'attendance.title' | t }}
          </a>
          <header class="page-header">
            <div>
              <h1 class="page-title">{{ s.classTitle }}</h1>
              <p class="page-subtitle">{{ date(s.date) }} · {{ time(s.start) }} – {{ time(s.end) }}@if (s.isExtra) { · {{ 'attendance.extra_tag' | t }} }</p>
            </div>
            @if (canRemove()) {
              <button mat-stroked-button type="button" (click)="remove()">
                <mat-icon>delete</mat-icon>
                {{ 'attendance.remove' | t }}
              </button>
            }
          </header>

          @if (!s.editable) {
            <p class="note" role="status"><mat-icon>lock</mat-icon> {{ 'attendance.locked' | t }}</p>
          } @else if (students().length > 0) {
            <p class="muted hint">{{ 'attendance.hint' | t }}</p>
          }

          @if (students().length > 0) {
            <div class="counts" role="status">
              <span class="count present"><mat-icon aria-hidden="true">check_circle</mat-icon>{{ 'attendance.present' | t }} {{ counts().present }}</span>
              <span class="count late"><mat-icon aria-hidden="true">schedule</mat-icon>{{ 'attendance.late' | t }} {{ counts().late }}</span>
              <span class="count absent"><mat-icon aria-hidden="true">cancel</mat-icon>{{ 'attendance.absent' | t }} {{ counts().absent }}</span>
            </div>

            <div class="panel">
              @for (student of students(); track student.enrolmentId) {
                <div class="panel-row student" [class.late]="status(student) === 'Late'" [class.absent]="status(student) === 'Absent'">
                  <button
                    type="button"
                    class="hit"
                    [disabled]="!s.editable"
                    (click)="tap(student)"
                    (pointerdown)="press(student, $event)"
                    (pointermove)="move($event)"
                    (pointerup)="release()"
                    (pointerleave)="release()"
                    (pointercancel)="release()"
                    (contextmenu)="$event.preventDefault()"
                  >
                    <span class="avatar" aria-hidden="true">{{ student.indexNumber }}</span>
                    <span class="row-main">
                      <span class="row-title">{{ student.fullName }}</span>
                    </span>
                    <span class="state {{ status(student).toLowerCase() }}">
                      <mat-icon aria-hidden="true">{{ icon(status(student)) }}</mat-icon>
                      {{ 'attendance.' + status(student).toLowerCase() | t }}
                    </span>
                  </button>
                  @if (s.editable) {
                    <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [matMenuTriggerData]="{ student: student }" [attr.aria-label]="'common.actions' | t">
                      <mat-icon>more_vert</mat-icon>
                    </button>
                  }
                </div>
              }
            </div>

            <mat-menu #menu="matMenu">
              <ng-template matMenuContent let-student="student">
                @for (option of options; track option) {
                  <button mat-menu-item type="button" (click)="set(student, option)">
                    <mat-icon>{{ icon(option) }}</mat-icon>
                    <span>{{ 'attendance.' + option.toLowerCase() | t }}</span>
                  </button>
                }
              </ng-template>
            </mat-menu>

            @if (s.editable) {
              <div class="actions">
                <button mat-stroked-button type="button" (click)="allPresent()">{{ 'attendance.all_present' | t }}</button>
                <form (submit)="$event.preventDefault(); save()">
                  <app-submit-button label="attendance.save" [busy]="busy()" />
                </form>
              </div>
            }
          } @else {
            <div class="empty">
              <mat-icon>groups</mat-icon>
              <p>{{ 'attendance.no_students' | t }}</p>
              <a mat-stroked-button routerLink="/students" [queryParams]="{ classId: s.classId }">{{ 'students.add' | t }}</a>
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
    .back { margin-left: -0.75rem; margin-bottom: 0.5rem; }
    .hint { margin: 0 0 0.75rem; }
    .note { display: flex; gap: 0.5rem; align-items: center; margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); }
    .counts { display: flex; flex-wrap: wrap; gap: 0.5rem 1.25rem; margin-bottom: 0.75rem; font: var(--mat-sys-label-large); }
    .count { display: inline-flex; gap: 0.25rem; align-items: center; }
    .count mat-icon { width: 1.125rem; height: 1.125rem; font-size: 1.125rem; }
    .count.present mat-icon { color: var(--mat-sys-primary); }
    .count.late mat-icon { color: var(--mat-sys-tertiary); }
    .count.absent mat-icon { color: var(--mat-sys-error); }
    .student { padding: 0; gap: 0; }
    .student.absent { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .student.late { background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
    .hit { display: flex; flex: 1; gap: 0.875rem; align-items: center; min-width: 0; min-height: 3.5rem; padding: 0.5rem 0.5rem 0.5rem 1rem; border: 0; background: none; color: inherit; font: inherit; text-align: left; cursor: pointer; user-select: none; -webkit-user-select: none; touch-action: pan-y; -webkit-touch-callout: none; }
    .hit:disabled { cursor: default; }
    .hit:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: -2px; }
    .state { display: inline-flex; gap: 0.25rem; align-items: center; font: var(--mat-sys-label-medium); white-space: nowrap; }
    .state mat-icon { width: 1.125rem; height: 1.125rem; font-size: 1.125rem; }
    .state.present mat-icon { color: var(--mat-sys-primary); }
    .actions { display: flex; flex-wrap: wrap; gap: 0.75rem; align-items: center; justify-content: space-between; margin-top: 1rem; }
  `,
})
export class AttendanceMark {
  /** The session id from the route. */
  readonly id = input.required<string>();

  private readonly api = inject(AttendanceService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  protected readonly detail = new Loadable<SessionDetail>();
  protected readonly session = computed(() => this.detail.data()?.session);
  protected readonly students = computed(() => this.detail.data()?.students ?? []);
  protected readonly marks = signal<ReadonlyMap<string, AttendanceStatus>>(new Map());
  protected readonly busy = signal(false);
  protected readonly options: AttendanceStatus[] = ['Present', 'Late', 'Absent'];
  protected readonly time = formatTime;
  protected readonly date = formatDate;
  protected readonly icon = (status: AttendanceStatus) => ICONS[status];
  protected readonly counts = computed(() => {
    const all = this.students().map((s) => this.status(s));
    return {
      present: all.filter((x) => x === 'Present').length,
      late: all.filter((x) => x === 'Late').length,
      absent: all.filter((x) => x === 'Absent').length,
    };
  });
  protected readonly canRemove = computed(() => !!this.session()?.isExtra && !this.session()?.taken);

  private timer: ReturnType<typeof setTimeout> | null = null;
  private held = false;
  private origin = { x: 0, y: 0 };

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.detail.status.set('loading');
    this.api.session(this.id()).subscribe({
      next: (value) => {
        this.detail.data.set(value);
        this.marks.set(new Map(value.students.map((s) => [s.enrolmentId, s.status])));
        this.detail.status.set('ready');
      },
      error: (e) => {
        this.detail.error.set(e);
        this.detail.status.set('error');
      },
    });
  }

  protected status(student: SessionStudent): AttendanceStatus {
    return this.marks().get(student.enrolmentId) ?? 'Present';
  }

  protected set(student: SessionStudent, status: AttendanceStatus): void {
    this.marks.update((m) => new Map(m).set(student.enrolmentId, status));
  }

  /** A tap flips between present and absent. A press-and-hold has already set late, so its click is ignored. */
  protected tap(student: SessionStudent): void {
    if (this.held) {
      this.held = false;
      return;
    }
    this.set(student, this.status(student) === 'Present' ? 'Absent' : 'Present');
  }

  protected press(student: SessionStudent, event: PointerEvent): void {
    this.held = false;
    this.release();
    this.origin = { x: event.clientX, y: event.clientY };
    this.timer = setTimeout(() => {
      this.held = true;
      this.set(student, 'Late');
    }, LONG_PRESS_MS);
  }

  /** A finger that slides is scrolling the page, not holding: cancel the press so a swipe never marks anyone late. */
  protected move(event: PointerEvent): void {
    if (this.timer && Math.hypot(event.clientX - this.origin.x, event.clientY - this.origin.y) > MOVE_LIMIT_PX) this.release();
  }

  protected release(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
  }

  protected allPresent(): void {
    this.marks.set(new Map(this.students().map((s) => [s.enrolmentId, 'Present'])));
  }

  protected save(): void {
    const session = this.session();
    if (!session || this.busy()) return;
    this.busy.set(true);
    const marks = this.students()
      .map((s) => ({ enrolmentId: s.enrolmentId, status: this.status(s) }))
      .filter((m) => m.status !== 'Present');
    this.api.save(session.id, marks).subscribe({
      next: () => {
        this.toast.success('attendance.saved');
        void this.router.navigate(['/attendance'], { queryParams: { date: session.date } });
      },
      error: () => this.busy.set(false),
    });
  }

  protected remove(): void {
    const session = this.session();
    if (!session) return;
    this.dialog
      .open<ConfirmDialog, unknown, boolean>(ConfirmDialog, {
        data: { title: 'attendance.remove_title', message: 'attendance.remove_message', confirm: 'attendance.remove' },
        width: '26rem',
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((yes) => {
        if (!yes) return;
        this.api.remove(session.id).subscribe(() => {
          this.toast.success('attendance.removed');
          void this.router.navigate(['/attendance'], { queryParams: { date: session.date } });
        });
      });
  }
}
