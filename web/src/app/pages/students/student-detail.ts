import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { Student, StudentCreated, StudentEnrolment, StudentRow, StudentsService } from '../../core/api/students.service';
import { errorCode } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { ConfirmData, ConfirmDialog } from '../../shared/confirm-dialog';
import { TranslateService } from '../../core/i18n/translate.service';
import { formatLkr } from '../../shared/format';
import { EnrolmentDialog, EnrolmentDialogData } from './enrolment-dialog';
import { PasswordDialog } from './password-dialog';
import { StudentDialog } from './student-dialog';

/** One student: their details, the classes they are in, and the teacher's actions on them. */
@Component({
  selector: 'app-student-detail',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    <a mat-button routerLink="/students" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'students.back' | t }}
    </a>

    @switch (record.status()) {
      @case ('ready') {
        @if (student(); as s) {
          <header class="page-header">
            <div>
              <h1 class="page-title">{{ s.fullName }}</h1>
              <p class="page-subtitle">{{ s.phone }}</p>
            </div>
            <div class="row-wrap">
              <button mat-stroked-button type="button" (click)="edit(s)">
                <mat-icon>edit</mat-icon>
                {{ 'students.edit' | t }}
              </button>
              <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="'common.actions' | t">
                <mat-icon>more_vert</mat-icon>
              </button>
              <mat-menu #menu="matMenu">
                <button mat-menu-item type="button" [disabled]="!s.hasAccount" (click)="confirmReset(s)">
                  <mat-icon>lock_reset</mat-icon>
                  <span>{{ 'students.reset_password' | t }}</span>
                </button>
              </mat-menu>
            </div>
          </header>

          <mat-card appearance="outlined">
            <mat-card-content class="facts">
              <div><span class="muted">{{ 'students.school' | t }}</span><strong>{{ s.school || '–' }}</strong></div>
              <div><span class="muted">{{ 'students.grade' | t }}</span><strong>{{ s.grade || '–' }}</strong></div>
              <div><span class="muted">{{ 'students.guardian' | t }}</span><strong>{{ s.guardianName || '–' }}</strong></div>
              <div><span class="muted">{{ 'students.guardian_phone' | t }}</span><strong>{{ s.guardianPhone || '–' }}</strong></div>
            </mat-card-content>
          </mat-card>

          <div class="section-head">
            <h2 class="section-title">{{ 'students.classes' | t }}</h2>
            <button mat-stroked-button type="button" (click)="enrolAnother(s)">
              <mat-icon>add</mat-icon>
              {{ 'students.enrol_another' | t }}
            </button>
          </div>
          <div class="panel">
            @for (e of s.enrolments; track e.id) {
              <div class="panel-row">
                <span class="avatar" aria-hidden="true">#{{ e.indexNumber }}</span>
                <div class="row-main">
                  <div class="row-title">{{ e.classTitle }}</div>
                  <div class="row-meta">{{ planText(e) }}</div>
                </div>
                @if (e.status !== 'Active') {
                  <span class="chip" [class.attention]="e.status === 'Pending'">{{ 'students.status.' + e.status | t }}</span>
                }
                @if (e.status === 'Pending') {
                  <button mat-flat-button type="button" (click)="approve(e)">{{ 'students.approve' | t }}</button>
                } @else {
                  <button mat-icon-button type="button" (click)="editEnrolment(s, e)" [attr.aria-label]="'students.edit_enrolment' | t">
                    <mat-icon>edit</mat-icon>
                  </button>
                }
              </div>
            }
          </div>
        }
      }
      @case ('error') {
        <div class="empty">
          <p>{{ 'students.not_found' | t }}</p>
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
    .facts { display: grid; grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr)); gap: 1rem; }
    .facts div { display: flex; flex-direction: column; overflow-wrap: anywhere; }
    .section-head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 0.5rem; margin-top: 1.5rem; }
    .section-head .section-title { margin: 0; }
    .section-head + .panel { margin-top: 0.75rem; }
    .chip { padding: 0.125rem 0.625rem; border-radius: 999px; background: var(--mat-sys-surface-container-highest); color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-label-small); white-space: nowrap; }
    .chip.attention { background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
    .avatar { font-size: 0.8125rem; }
    .panel-row { flex-wrap: wrap; }
    .row-main { flex: 1 1 9rem; }
    .panel-row .chip { margin-left: auto; }
  `,
})
export class StudentDetail {
  /** The student id from the route. */
  readonly id = input.required<string>();

  private readonly api = inject(StudentsService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  protected readonly record = new Loadable<Student>();
  protected readonly student = computed(() => this.record.data());

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.record.load(this.api.student(this.id()));
  }

  protected planText(e: StudentEnrolment): string {
    return e.feePlan === 'Discount' ? `${this.plan(e.feePlan)} · ${formatLkr(e.discountAmount)}` : this.plan(e.feePlan);
  }

  private readonly i18n = inject(TranslateService);
  private plan = (p: string) => this.i18n.translate(`students.plan.${p}`);

  protected edit(s: Student): void {
    this.dialog
      .open<StudentDialog, { student: Student }, StudentCreated | Student>(StudentDialog, {
        data: { student: s },
        width: '44rem',
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.toast.success('students.saved');
        this.load();
      });
  }

  protected enrolAnother(s: Student): void {
    this.dialog
      .open<EnrolmentDialog, EnrolmentDialogData, StudentRow>(EnrolmentDialog, {
        data: { studentId: s.id, takenClassIds: s.enrolments.filter((e) => e.status !== 'Left').map((e) => e.classId) },
        width: '36rem',
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((row) => {
        if (!row) return;
        this.toast.success('students.saved');
        this.load();
      });
  }

  protected editEnrolment(s: Student, e: StudentEnrolment): void {
    this.dialog
      .open<EnrolmentDialog, EnrolmentDialogData, StudentRow>(EnrolmentDialog, {
        data: { studentId: s.id, enrolment: e },
        width: '36rem',
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((row) => {
        if (!row) return;
        this.toast.success('students.saved');
        this.load();
      });
  }

  protected approve(e: StudentEnrolment): void {
    this.api.approve(e.id).subscribe(() => {
      this.toast.success('students.approved');
      this.load();
    });
  }

  protected confirmReset(s: Student): void {
    this.dialog
      .open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, {
        data: { title: 'students.reset_password', message: 'students.reset_confirm', confirm: 'students.reset_password' },
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((yes) => {
        if (yes) this.reset(s);
      });
  }

  private reset(s: Student): void {
    this.api.resetPassword(s.id).subscribe({
      next: ({ oneTimePassword }) =>
        this.dialog.open(PasswordDialog, {
          data: { name: s.fullName, phone: s.phone, oneTimePassword, title: 'students.reset_title' },
          width: '28rem',
          maxWidth: 'calc(100vw - 1rem)',
        }),
      error: (e) => {
        if (errorCode(e) === 'account_not_resettable') this.toast.error('error.account_not_resettable');
      },
    });
  }
}
