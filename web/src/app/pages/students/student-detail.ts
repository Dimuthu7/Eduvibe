import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { PlansService } from '../../core/api/plans.service';
import { Student, StudentCreated, StudentEnrolment, StudentRow, StudentsService } from '../../core/api/students.service';
import { errorCode } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { ConfirmData, ConfirmDialog } from '../../shared/confirm-dialog';
import { TranslateService } from '../../core/i18n/translate.service';
import { formatLkr } from '../../shared/format';
import { StudentPhotos } from '../../shared/student-photos';
import { EnrolmentDialog, EnrolmentDialogData } from './enrolment-dialog';
import { PasswordDialog } from './password-dialog';
import { PhotoPicker } from './photo-picker';
import { StudentDialog } from './student-dialog';

/** One student: their details, the classes they are in, and the teacher's actions on them. */
@Component({
  selector: 'app-student-detail',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PhotoPicker, MatButtonModule, MatCardModule, MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    <a mat-button routerLink="/students" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'students.back' | t }}
    </a>

    @switch (record.status()) {
      @case ('ready') {
        @if (student(); as s) {
          <header class="page-header">
            <div class="who">
              <app-photo-picker #photo [studentId]="s.id" [name]="s.fullName" />
              <div>
                <h1 class="page-title">{{ s.fullName }}</h1>
                <p class="page-subtitle">{{ s.phone }}</p>
                @if (!hasPhoto()) {
                  <button mat-button type="button" class="add-photo" (click)="photo.open()">
                    <mat-icon>add_a_photo</mat-icon>
                    {{ 'students.photo.add' | t }}
                  </button>
                }
              </div>
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

          <h2 class="section-title parent-title">{{ 'students.parent.title' | t }}</h2>
          <mat-card appearance="outlined" class="parent-card">
            <mat-card-content>
              @if (parentLocked()) {
                <div class="parent-row">
                  <mat-icon aria-hidden="true">lock</mat-icon>
                  <div class="row-main">
                    <div>{{ 'students.parent.locked' | t }}</div>
                    @if (availableOn()) {
                      <div class="row-meta">{{ 'my_plan.available_on' | t }} <strong>{{ availableOn() }}</strong></div>
                    }
                  </div>
                </div>
              } @else if (!s.guardianPhone) {
                <div class="parent-row">
                  <mat-icon aria-hidden="true">family_restroom</mat-icon>
                  <div class="row-main">
                    <div>{{ 'students.parent.need_phone' | t }}</div>
                  </div>
                  <button mat-stroked-button type="button" (click)="edit(s)">{{ 'students.edit' | t }}</button>
                </div>
              } @else if (s.hasParent) {
                <div class="parent-row">
                  <mat-icon aria-hidden="true">family_restroom</mat-icon>
                  <div class="row-main">
                    <div class="row-title">{{ s.guardianName || s.guardianPhone }}</div>
                    <div class="row-meta">{{ 'students.parent.linked' | t }} · {{ s.guardianPhone }}</div>
                  </div>
                </div>
                <div class="parent-actions">
                  <button mat-stroked-button type="button" (click)="confirmResetParent(s)">
                    <mat-icon>lock_reset</mat-icon>
                    {{ 'students.reset_password' | t }}
                  </button>
                  <button mat-button type="button" (click)="confirmRemoveParent(s)">
                    <mat-icon>link_off</mat-icon>
                    {{ 'students.parent.remove' | t }}
                  </button>
                </div>
              } @else {
                <div class="parent-row">
                  <mat-icon aria-hidden="true">family_restroom</mat-icon>
                  <div class="row-main">
                    <div>{{ 'students.parent.intro' | t }}</div>
                    <div class="row-meta">{{ s.guardianName || '' }} {{ s.guardianPhone }}</div>
                  </div>
                  <button mat-flat-button type="button" (click)="invite(s)">{{ 'students.parent.invite' | t }}</button>
                </div>
              }
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
    .who { display: flex; align-items: center; gap: 1rem; min-width: 0; }
    .who > div { min-width: 0; }
    .add-photo { margin-left: -0.75rem; }
    .add-photo mat-icon { font-size: 1.125rem; width: 1.125rem; height: 1.125rem; }
    .facts { display: grid; grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr)); gap: 1rem; }
    .facts div { display: flex; flex-direction: column; overflow-wrap: anywhere; }
    .parent-title { margin-top: 1.5rem; }
    .parent-row { display: flex; flex-wrap: wrap; align-items: center; gap: 0.75rem; }
    .parent-row > mat-icon { color: var(--mat-sys-primary); }
    .parent-row .row-main { flex: 1 1 12rem; min-width: 0; overflow-wrap: anywhere; }
    .parent-actions { display: flex; flex-wrap: wrap; gap: 0.5rem; margin-top: 0.75rem; }
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

  private readonly photos = inject(StudentPhotos);
  protected readonly record = new Loadable<Student>();
  protected readonly hasPhoto = computed(() => this.photos.entryOf(this.id()) !== undefined);
  protected readonly student = computed(() => this.record.data());
  private readonly plans = inject(PlansService);
  protected readonly parentLocked = signal(false);
  protected readonly availableOn = signal<string | null>(null);

  constructor() {
    queueMicrotask(() => this.load());
    // Which plan has the parent portal is shown when the teacher's does not; if the plan cannot be read the invite button stays and the server decides.
    this.plans.mine().subscribe({
      next: (plan) => {
        const feature = plan.features.find((f) => f.key === 'parent_portal');
        this.parentLocked.set(feature !== undefined && !feature.enabled);
        this.availableOn.set(feature?.availableOn ?? null);
      },
      error: () => undefined,
    });
  }

  protected load(): void {
    this.photos.refresh();
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

  protected invite(s: Student): void {
    this.api.inviteParent(s.id).subscribe({
      next: (parent) => {
        this.load();
        if (parent.oneTimePassword) {
          this.showParentPassword(parent.name, parent.phone, parent.oneTimePassword, 'students.parent.invited_title');
        } else {
          this.toast.success('students.parent.linked_existing');
        }
      },
      error: (e) => {
        const code = errorCode(e);
        if (code === 'guardian_phone_missing' || code === 'guardian_phone_is_student') this.toast.error(`error.${code}`);
      },
    });
  }

  protected confirmResetParent(s: Student): void {
    this.dialog
      .open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, {
        data: { title: 'students.reset_password', message: 'students.parent.reset_confirm', confirm: 'students.reset_password' },
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((yes) => {
        if (!yes) return;
        this.api.resetParentPassword(s.id).subscribe({
          next: ({ oneTimePassword }) => this.showParentPassword(s.guardianName || s.guardianPhone || '', s.guardianPhone ?? '', oneTimePassword, 'students.parent.reset_title'),
          error: (e) => {
            if (errorCode(e) === 'account_not_resettable') this.toast.error('error.account_not_resettable');
          },
        });
      });
  }

  protected confirmRemoveParent(s: Student): void {
    this.dialog
      .open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, {
        data: { title: 'students.parent.remove', message: 'students.parent.remove_confirm', confirm: 'students.parent.remove' },
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((yes) => {
        if (!yes) return;
        this.api.removeParent(s.id).subscribe(() => {
          this.toast.success('students.parent.removed');
          this.load();
        });
      });
  }

  private showParentPassword(name: string, phone: string, oneTimePassword: string, title: string): void {
    this.dialog.open(PasswordDialog, {
      data: {
        name, phone, oneTimePassword, title,
        keys: { intro: 'students.parent.otp_intro', person: 'students.parent.person', share: 'students.parent.share_message' },
      },
      width: '28rem',
      maxWidth: 'calc(100vw - 1rem)',
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
