import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { forkJoin } from 'rxjs';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { AttendanceService, MyAttendance } from '../../core/api/attendance.service';
import { ExamsService, MyExam } from '../../core/api/exams.service';
import { FeesService, MyFees } from '../../core/api/fees.service';
import { Child, ChildClass, StudentsService } from '../../core/api/students.service';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { FeeStatus } from '../../shared/fee-status';
import { formatDate, formatLkr, formatMonthShort } from '../../shared/format';
import { slotSummary } from '../../shared/schedule';
import { TranslateService } from '../../core/i18n/translate.service';
import { short } from '../exams/exam-analysis';

interface ChildData {
  attendance: MyAttendance[];
  fees: MyFees[];
  results: MyExam[];
}

interface ClassView {
  klass: ChildClass;
  attendance: MyAttendance | undefined;
  fees: MyFees | undefined;
  results: MyExam[];
}

/**
 * Where a parent lands after signing in: their children, then for the child they pick each class with how often the child has come,
 * the fees and the published results. Read-only. A class whose teacher has not turned the parent view on is listed but says so.
 */
@Component({
  selector: 'app-family',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, FeeStatus, MatButtonModule, MatButtonToggleModule, MatCardModule, MatIconModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'family.title' | t }}</h1>
        <p class="page-subtitle">{{ 'family.subtitle' | t }}</p>
      </div>
    </header>
    @if (auth.hasRole('Student')) {
      <a mat-button routerLink="/welcome" class="switch"><mat-icon>school</mat-icon> {{ 'family.my_classes' | t }}</a>
    }

    @switch (children.status()) {
      @case ('ready') {
        @if (children.data()!.length === 0) {
          <mat-card appearance="outlined">
            <mat-card-content class="welcome">
              <mat-icon aria-hidden="true">family_restroom</mat-icon>
              <p class="muted">{{ 'family.no_children' | t }}</p>
            </mat-card-content>
          </mat-card>
        } @else {
          @if (children.data()!.length > 1) {
            <mat-button-toggle-group class="picker" [value]="selected().studentId" (change)="select($event.value)" [attr.aria-label]="'family.pick' | t" hideSingleSelectionIndicator>
              @for (c of children.data(); track c.studentId) {
                <mat-button-toggle [value]="c.studentId">{{ c.firstName }}</mat-button-toggle>
              }
            </mat-button-toggle-group>
          }
          @if (selected(); as child) {
            <h2 class="section-title child-name">{{ child.fullName }}</h2>
            @switch (details.status()) {
              @case ('ready') {
                @for (item of views(); track item.klass.classId) {
                  <mat-card appearance="outlined" class="class-card">
                    <mat-card-content>
                      <div class="head">
                        <div class="row-main">
                          <div class="row-title">{{ item.klass.classTitle }}</div>
                          @if (item.klass.teacherName) {
                            <div class="row-meta">{{ item.klass.teacherName }}</div>
                          }
                        </div>
                        @if (item.attendance && item.attendance.sessions > 0) {
                          <div class="rate" [class.low]="item.attendance.percent < 75">
                            <strong>{{ item.attendance.percent }}%</strong>
                            <span>{{ 'welcome.attended' | t }}</span>
                          </div>
                        }
                      </div>

                      @if (item.klass.slots.length > 0) {
                        <div class="slot"><mat-icon aria-hidden="true">schedule</mat-icon> {{ schedule(item.klass.slots) }}</div>
                      }

                      @if (item.klass.locked) {
                        <div class="locked">
                          <mat-icon aria-hidden="true">lock</mat-icon>
                          <span>{{ 'family.locked' | t }}</span>
                        </div>
                      } @else {
                        <section class="block" [attr.aria-label]="'family.attendance' | t">
                          <div class="block-head"><mat-icon aria-hidden="true">event_available</mat-icon><strong>{{ 'family.attendance' | t }}</strong></div>
                          @if (item.attendance && item.attendance.sessions > 0) {
                            <div class="counts">
                              <span>{{ 'attendance.present' | t }} <strong>{{ item.attendance.present }}</strong></span>
                              <span>{{ 'attendance.late' | t }} <strong>{{ item.attendance.late }}</strong></span>
                              <span>{{ 'attendance.absent' | t }} <strong>{{ item.attendance.absent }}</strong></span>
                            </div>
                            <div class="recent" [attr.aria-label]="'welcome.recent' | t">
                              @for (r of item.attendance.recent; track $index) {
                                <span class="dot {{ r.status.toLowerCase() }}" [title]="date(r.date) + ': ' + ('attendance.' + r.status.toLowerCase() | t)">
                                  <mat-icon aria-hidden="true">{{ r.status === 'Absent' ? 'close' : r.status === 'Late' ? 'schedule' : 'check' }}</mat-icon>
                                  <span class="sr-only">{{ date(r.date) }}: {{ 'attendance.' + r.status.toLowerCase() | t }}</span>
                                </span>
                              }
                            </div>
                          } @else {
                            <p class="muted none">{{ 'family.no_attendance' | t }}</p>
                          }
                        </section>

                        <section class="block" [attr.aria-label]="'family.fees' | t">
                          <div class="block-head">
                            <mat-icon aria-hidden="true">payments</mat-icon>
                            <strong>{{ 'family.fees' | t }}</strong>
                            @if (item.fees) {
                              <span class="muted">
                                @if (item.fees.outstanding > 0) {
                                  {{ 'family.owing' | t }} {{ money(item.fees.outstanding) }}
                                } @else {
                                  {{ 'welcome.fees_clear' | t }}
                                }
                              </span>
                            }
                          </div>
                          @if (item.fees; as fees) {
                            <div class="months">
                              @for (m of fees.months.slice(0, 4); track m.month) {
                                <span class="month">
                                  <span class="muted">{{ monthShort(m.month) }}</span>
                                  <app-fee-status [status]="m.status" />
                                </span>
                              }
                            </div>
                            @if (fees.receipts.length > 0) {
                              <div class="row-meta receipt">{{ 'welcome.last_receipt' | t }}: {{ fees.receipts[0].receiptNo }} · {{ money(fees.receipts[0].amount) }} · {{ date(fees.receipts[0].paidOn) }}</div>
                            }
                          }
                        </section>

                        <section class="block" [attr.aria-label]="'family.results' | t">
                          <div class="block-head"><mat-icon aria-hidden="true">quiz</mat-icon><strong>{{ 'family.results' | t }}</strong></div>
                          @for (r of item.results; track r.examId) {
                            <div class="result">
                              <div class="row-main">
                                <div>{{ r.name }}</div>
                                <div class="row-meta">
                                  {{ date(r.heldOn) }} ·
                                  @if (r.absent) {
                                    {{ 'exams.absent' | t }}
                                  } @else if (r.percent !== null) {
                                    {{ num(r.total!) }} / {{ num(r.maxTotal) }} · {{ r.percent }}%
                                    @if (r.rank !== null) {
                                      · {{ 'exams.rank' | t }} {{ r.rank }} {{ 'exams.of' | t }} {{ r.ranked }}
                                    }
                                  } @else {
                                    {{ 'results.not_complete' | t }}
                                  }
                                </div>
                              </div>
                              @if (r.grade) {
                                <span class="grade-chip" [attr.aria-label]="('exams.grade' | t) + ' ' + r.grade">{{ r.grade }}</span>
                              }
                            </div>
                          } @empty {
                            <p class="muted none">{{ 'family.no_results' | t }}</p>
                          }
                        </section>
                      }
                    </mat-card-content>
                  </mat-card>
                }
              }
              @case ('error') {
                <div class="empty">
                  <p>{{ 'error.load_failed' | t }}</p>
                  <button mat-stroked-button type="button" (click)="loadDetails()">{{ 'status.retry' | t }}</button>
                </div>
              }
              @default {
                <div class="skeleton"></div>
                <div class="skeleton"></div>
              }
            }
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
    .switch { margin-left: -0.75rem; margin-bottom: 0.5rem; }
    .picker { display: flex; width: 100%; margin-bottom: 1rem; overflow-x: auto; }
    .picker .mat-button-toggle { flex: 1 0 auto; }
    .child-name { margin-top: 0; }
    .class-card { margin-bottom: 0.75rem; }
    .head { display: flex; gap: 1rem; align-items: flex-start; margin-bottom: 0.5rem; }
    .rate { display: grid; justify-items: end; color: var(--mat-sys-primary); }
    .rate strong { font: var(--mat-sys-headline-small); }
    .rate span { color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-label-small); }
    .rate.low { color: var(--mat-sys-error); }
    .slot { display: flex; gap: 0.5rem; align-items: center; padding: 0.125rem 0; color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-body-medium); }
    .slot mat-icon { width: 1.125rem; height: 1.125rem; font-size: 1.125rem; }
    .locked { display: flex; gap: 0.5rem; align-items: flex-start; margin-top: 0.75rem; padding: 0.75rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container-high); color: var(--mat-sys-on-surface-variant); }
    .locked mat-icon { flex: none; width: 1.25rem; height: 1.25rem; font-size: 1.25rem; }
    .block { margin-top: 0.75rem; padding-top: 0.75rem; border-top: 1px solid var(--mat-sys-outline-variant); }
    .block-head { display: flex; flex-wrap: wrap; gap: 0.25rem 0.5rem; align-items: center; margin-bottom: 0.5rem; }
    .block-head mat-icon { width: 1.125rem; height: 1.125rem; font-size: 1.125rem; color: var(--mat-sys-primary); }
    .counts { display: flex; flex-wrap: wrap; gap: 0.25rem 1rem; color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-body-medium); }
    .counts strong { color: var(--mat-sys-on-surface); }
    .recent { display: flex; flex-wrap: wrap; gap: 0.375rem; margin-top: 0.5rem; }
    .dot { display: grid; place-items: center; width: 1.5rem; height: 1.5rem; border-radius: 50%; background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .dot.absent { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .dot.late { background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
    .dot mat-icon { width: 1rem; height: 1rem; font-size: 1rem; }
    .months { display: flex; flex-wrap: wrap; gap: 0.5rem 0.75rem; }
    .month { display: inline-flex; flex-direction: column; gap: 0.125rem; font: var(--mat-sys-label-small); }
    .receipt { margin-top: 0.5rem; }
    .result { display: flex; gap: 0.75rem; align-items: center; padding: 0.375rem 0; }
    .none { margin: 0; }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
    .welcome { display: grid; justify-items: center; gap: 0.5rem; text-align: center; padding: 1.5rem 0.5rem; }
    .welcome mat-icon { width: 3rem; height: 3rem; font-size: 3rem; color: var(--mat-sys-primary); }
    .welcome p { margin: 0; }
  `,
})
export class Family {
  protected readonly auth = inject(AuthService);
  private readonly students = inject(StudentsService);
  private readonly attendance = inject(AttendanceService);
  private readonly fees = inject(FeesService);
  private readonly exams = inject(ExamsService);

  protected readonly children = new Loadable<Child[]>();
  protected readonly details = new Loadable<ChildData>();
  protected readonly selectedId = signal<string | null>(null);
  protected readonly num = short;
  private readonly i18n = inject(TranslateService);
  protected readonly schedule = (slots: { day: number; start: string; end: string }[]) => slotSummary(slots, (key) => this.i18n.translate(key));
  protected readonly date = formatDate;
  protected readonly money = formatLkr;
  protected readonly monthShort = formatMonthShort;

  protected readonly selected = computed(() => {
    const all = this.children.data() ?? [];
    return all.find((c) => c.studentId === this.selectedId()) ?? all[0];
  });

  protected readonly views = computed<ClassView[]>(() => {
    const child = this.selected();
    const value = this.details.data();
    return (child?.classes ?? []).map((klass) => ({
      klass,
      attendance: value?.attendance.find((a) => a.classId === klass.classId),
      fees: value?.fees.find((f) => f.classId === klass.classId),
      // Results come newest first.
      results: (value?.results ?? []).filter((r) => r.classId === klass.classId),
    }));
  });

  constructor() {
    this.load();
    // Open the first child as soon as the list is there; with one child there is no picker.
    effect(() => {
      if (this.children.status() === 'ready') untracked(() => this.loadDetails());
    });
  }

  protected load(): void {
    this.children.load(this.students.family());
  }

  protected select(studentId: string): void {
    this.selectedId.set(studentId);
    this.loadDetails();
  }

  protected loadDetails(): void {
    const child = this.selected();
    if (!child) return;
    this.details.load(
      forkJoin({
        attendance: this.attendance.child(child.studentId),
        fees: this.fees.child(child.studentId),
        results: this.exams.child(child.studentId),
      }),
    );
  }
}
