import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { forkJoin } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { AttendanceService, MyAttendance } from '../../core/api/attendance.service';
import { FeesService, MyFees } from '../../core/api/fees.service';
import { MyClass, StudentsService } from '../../core/api/students.service';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { FeeStatus } from '../../shared/fee-status';
import { formatDate, formatLkr, formatMonthShort, formatTime } from '../../shared/format';

interface MyClassView {
  klass: MyClass;
  attendance: MyAttendance | undefined;
  fees: MyFees | undefined;
}

/** Where a student lands after signing in: their classes, when each meets and how often they have come. Results arrive in a later sprint. */
@Component({
  selector: 'app-welcome',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FeeStatus, MatButtonModule, MatCardModule, MatIconModule, TranslatePipe],
  template: `
    <h1 class="page-title">{{ 'home.welcome' | t }}, {{ auth.user()?.firstName }}</h1>
    <h2 class="section-title first">{{ 'welcome.my_classes' | t }}</h2>

    @switch (data.status()) {
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

              @for (slot of item.klass.slots; track $index) {
                <div class="slot"><mat-icon aria-hidden="true">schedule</mat-icon> {{ 'day.' + slot.day | t }} · {{ time(slot.start) }} – {{ time(slot.end) }}</div>
              }

              @if (item.attendance && item.attendance.recent.length > 0) {
                <div class="recent" [attr.aria-label]="'welcome.recent' | t">
                  @for (r of item.attendance.recent; track $index) {
                    <span class="dot {{ r.status.toLowerCase() }}" [title]="date(r.date) + ': ' + ('attendance.' + r.status.toLowerCase() | t)">
                      <mat-icon aria-hidden="true">{{ r.status === 'Absent' ? 'close' : r.status === 'Late' ? 'schedule' : 'check' }}</mat-icon>
                      <span class="sr-only">{{ date(r.date) }}: {{ 'attendance.' + r.status.toLowerCase() | t }}</span>
                    </span>
                  }
                </div>
              }

              @if (item.fees; as fees) {
                <div class="fees">
                  <div class="fees-head">
                    <mat-icon aria-hidden="true">payments</mat-icon>
                    <strong>{{ 'welcome.fees' | t }}</strong>
                    <span class="muted">
                      @if (fees.outstanding > 0) {
                        {{ 'welcome.fees_owing' | t }} {{ money(fees.outstanding) }}
                      } @else {
                        {{ 'welcome.fees_clear' | t }}
                      }
                    </span>
                  </div>
                  <div class="months">
                    @for (m of fees.months.slice(0, 3); track m.month) {
                      <span class="month">
                        <span class="muted">{{ monthShort(m.month) }}</span>
                        <app-fee-status [status]="m.status" />
                      </span>
                    }
                  </div>
                  @if (fees.receipts.length > 0) {
                    <div class="row-meta receipt">{{ 'welcome.last_receipt' | t }}: {{ fees.receipts[0].receiptNo }} · {{ money(fees.receipts[0].amount) }} · {{ date(fees.receipts[0].paidOn) }}</div>
                  }
                </div>
              }
            </mat-card-content>
          </mat-card>
        } @empty {
          <mat-card appearance="outlined">
            <mat-card-content class="welcome">
              <mat-icon aria-hidden="true">waving_hand</mat-icon>
              <p class="muted">{{ 'welcome.no_classes' | t }}</p>
            </mat-card-content>
          </mat-card>
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
    .section-title.first { margin-top: 0; }
    .class-card { margin-bottom: 0.75rem; }
    .head { display: flex; gap: 1rem; align-items: flex-start; margin-bottom: 0.5rem; }
    .rate { display: grid; justify-items: end; color: var(--mat-sys-primary); }
    .rate strong { font: var(--mat-sys-headline-small); }
    .rate span { color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-label-small); }
    .rate.low { color: var(--mat-sys-error); }
    .slot { display: flex; gap: 0.5rem; align-items: center; padding: 0.125rem 0; color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-body-medium); }
    .slot mat-icon { width: 1.125rem; height: 1.125rem; font-size: 1.125rem; }
    .recent { display: flex; flex-wrap: wrap; gap: 0.375rem; margin-top: 0.75rem; }
    .dot { display: grid; place-items: center; width: 1.5rem; height: 1.5rem; border-radius: 50%; background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .dot.absent { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .dot.late { background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
    .dot mat-icon { width: 1rem; height: 1rem; font-size: 1rem; }
    .fees { margin-top: 0.75rem; padding-top: 0.75rem; border-top: 1px solid var(--mat-sys-outline-variant); }
    .fees-head { display: flex; flex-wrap: wrap; gap: 0.25rem 0.5rem; align-items: center; margin-bottom: 0.5rem; }
    .fees-head mat-icon { width: 1.125rem; height: 1.125rem; font-size: 1.125rem; color: var(--mat-sys-primary); }
    .months { display: flex; flex-wrap: wrap; gap: 0.5rem 0.75rem; }
    .month { display: inline-flex; flex-direction: column; gap: 0.125rem; font: var(--mat-sys-label-small); }
    .receipt { margin-top: 0.5rem; }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
    .welcome { display: grid; justify-items: center; gap: 0.5rem; text-align: center; padding: 1.5rem 0.5rem; }
    .welcome mat-icon { width: 3rem; height: 3rem; font-size: 3rem; color: var(--mat-sys-primary); }
    .welcome p { margin: 0; }
  `,
})
export class Welcome {
  protected readonly auth = inject(AuthService);
  private readonly students = inject(StudentsService);
  private readonly attendance = inject(AttendanceService);
  private readonly fees = inject(FeesService);

  protected readonly data = new Loadable<{ classes: MyClass[]; attendance: MyAttendance[]; fees: MyFees[] }>();
  protected readonly time = formatTime;
  protected readonly date = formatDate;
  protected readonly money = formatLkr;
  protected readonly monthShort = formatMonthShort;
  protected readonly views = computed<MyClassView[]>(() => {
    const value = this.data.data();
    return (value?.classes ?? []).map((klass) => ({
      klass,
      attendance: value?.attendance.find((a) => a.classId === klass.classId),
      fees: value?.fees.find((f) => f.classId === klass.classId),
    }));
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.data.load(forkJoin({ classes: this.students.myClasses(), attendance: this.attendance.mine(), fees: this.fees.mine() }));
  }
}
