import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AttendanceService, AttendanceStatus, ReportEntry, StudentReport } from '../../core/api/attendance.service';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { drawAttendanceReport } from '../../shared/attendance-report';
import { fileNamePart } from '../../shared/download';
import { formatDate, formatTime, monthRange, todayColombo } from '../../shared/format';
import { DateRange, PeriodPicker } from '../../shared/period-picker';
import { shareOrSave } from '../../shared/receipt';
import { SearchSelect } from '../../shared/search-select';

const ICONS: Record<AttendanceStatus, string> = { Present: 'check_circle', Late: 'schedule', Absent: 'cancel' };

/** How one student came to a class over a period, class by class, with a PDF the teacher can share or print. */
@Component({
  selector: 'app-student-attendance-report',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, PeriodPicker, SearchSelect, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <a mat-button routerLink="/attendance/summary" [queryParams]="{ classId: report()?.classId, from: from(), to: to() }" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'attendance.summary' | t }}
    </a>

    @switch (record.status()) {
      @case ('ready') {
        @if (report(); as r) {
          <header class="page-header">
            <div>
              <h1 class="page-title">{{ r.fullName }}</h1>
              <p class="page-subtitle">#{{ r.indexNumber }} · {{ r.classTitle }}</p>
            </div>
            <button mat-flat-button type="button" (click)="download(r)" [disabled]="making()">
              @if (making()) {
                <mat-spinner diameter="18" />
              } @else {
                <mat-icon>picture_as_pdf</mat-icon>
              }
              {{ 'attendance.pdf' | t }}
            </button>
          </header>

          @if (r.classes.length > 1) {
            <app-search-select [formControl]="classControl" [label]="'attendance.class' | t" [options]="classOptions()" />
          }
          <app-period-picker [from]="from()" [to]="to()" (rangeChange)="setRange($event)" />

          @if (r.sessions === 0) {
            <div class="empty">
              <mat-icon>event_busy</mat-icon>
              <p>{{ 'attendance.report_none' | t }}</p>
            </div>
          } @else {
            <div class="totals" role="group" [attr.aria-label]="'attendance.overall' | t">
              <div class="total main">
                <strong [class.low]="r.percent < 75">{{ r.percent }}%</strong>
                <span>{{ 'attendance.overall' | t }}</span>
              </div>
              <div class="total"><strong>{{ r.present }}</strong><span>{{ 'attendance.present' | t }}</span></div>
              <div class="total"><strong>{{ r.late }}</strong><span>{{ 'attendance.late' | t }}</span></div>
              <div class="total"><strong>{{ r.absent }}</strong><span>{{ 'attendance.absent' | t }}</span></div>
            </div>

            <div class="panel">
              @for (entry of r.entries; track $index) {
                <div class="panel-row">
                  <div class="row-main">
                    <div class="row-title">{{ date(entry.date) }}</div>
                    <div class="row-meta">
                      {{ time(entry) }}
                      @if (entry.isExtra) {
                        · {{ 'attendance.extra_tag' | t }}
                      }
                    </div>
                  </div>
                  <span class="state {{ entry.status.toLowerCase() }}">
                    <mat-icon aria-hidden="true">{{ icon(entry.status) }}</mat-icon>
                    {{ 'attendance.' + entry.status.toLowerCase() | t }}
                  </span>
                </div>
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
    .back { margin-left: -0.75rem; margin-bottom: 0.5rem; }
    .totals { display: grid; grid-template-columns: repeat(3, 1fr); gap: 0.5rem; margin-bottom: 1rem; }
    .total { display: grid; gap: 0.125rem; padding: 0.75rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); }
    .total strong { font: var(--mat-sys-headline-small); }
    .total span { font: var(--mat-sys-label-medium); color: var(--mat-sys-on-surface-variant); }
    .total.main { grid-column: 1 / -1; background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .total.main span { color: inherit; opacity: 0.8; }
    .total strong.low { color: var(--mat-sys-error); }
    .state { display: inline-flex; gap: 0.25rem; align-items: center; font: var(--mat-sys-label-large); white-space: nowrap; }
    .state mat-icon { width: 1.125rem; height: 1.125rem; font-size: 1.125rem; }
    .state.present mat-icon { color: var(--mat-sys-primary); }
    .state.late { color: var(--mat-sys-tertiary); }
    .state.absent { color: var(--mat-sys-error); }
  `,
})
export class StudentAttendanceReport {
  /** The enrolment from the address, and the dates the teacher was looking at on the summary (this month when missing). */
  readonly enrolmentId = input.required<string>();
  readonly fromParam = input<string>('', { alias: 'from' });
  readonly toParam = input<string>('', { alias: 'to' });

  private readonly api = inject(AttendanceService);
  private readonly router = inject(Router);
  private readonly i18n = inject(TranslateService);
  private readonly auth = inject(AuthService);

  protected readonly record = new Loadable<StudentReport>();
  protected readonly report = computed(() => this.record.data());
  protected readonly making = signal(false);
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly classOptions = computed(() => (this.report()?.classes ?? []).map((c) => ({ value: c.enrolmentId, label: c.title })));

  private readonly today = todayColombo();
  protected readonly from = computed(() => this.fromParam() || monthRange(this.today.slice(0, 7), this.today).from);
  protected readonly to = computed(() => this.toParam() || this.today);
  protected readonly date = formatDate;
  protected readonly icon = (status: AttendanceStatus) => ICONS[status];

  constructor() {
    // The address holds the student and the dates, so a change of either loads the report again.
    effect(() => this.load());

    this.classControl.valueChanges.subscribe((enrolmentId) => {
      if (enrolmentId && enrolmentId !== this.enrolmentId()) {
        void this.router.navigate(['/attendance/summary/student', enrolmentId], { queryParams: { from: this.from(), to: this.to() }, replaceUrl: true });
      }
    });
    effect(() => {
      const id = this.enrolmentId();
      if (this.classControl.value !== id) this.classControl.setValue(id, { emitEvent: false });
    });
  }

  protected load(): void {
    this.record.load(this.api.studentReport(this.enrolmentId(), this.from(), this.to()));
  }

  protected setRange(range: DateRange): void {
    void this.router.navigate([], { queryParams: { from: range.from, to: range.to }, replaceUrl: true });
  }

  protected time(entry: ReportEntry): string {
    return `${formatTime(entry.start)} – ${formatTime(entry.end)}`;
  }

  /** Makes the report as a PDF and offers it to share (WhatsApp, for example), or saves it. */
  protected async download(r: StudentReport): Promise<void> {
    const t = (key: string) => this.i18n.translate(key);
    const user = this.auth.user();
    this.making.set(true);
    try {
      const pdf = await drawAttendanceReport(
        {
          teacher: `${user?.firstName ?? ''} ${user?.lastName ?? ''}`.trim(),
          student: r.fullName,
          className: r.classTitle,
          period: `${formatDate(r.from)} – ${formatDate(r.to)}`,
          madeOn: formatDate(this.today),
          sessions: String(r.sessions),
          present: String(r.present),
          late: String(r.late),
          absent: String(r.absent),
          percent: r.sessions > 0 ? `${r.percent}%` : '–',
          rows: r.entries.map((e) => ({
            date: formatDate(e.date),
            time: this.time(e),
            status: e.status,
            statusText: t(`attendance.${e.status.toLowerCase()}`),
            extra: e.isExtra,
          })),
        },
        {
          title: t('attendance.report_title'),
          student: t('receipt.student'),
          className: t('receipt.class'),
          period: t('attendance.period'),
          sessions: t('attendance.sessions'),
          present: t('attendance.present'),
          late: t('attendance.late'),
          absent: t('attendance.absent'),
          rate: t('attendance.overall'),
          number: t('attendance.number'),
          date: t('attendance.date'),
          time: t('attendance.time'),
          status: t('attendance.status'),
          extra: t('attendance.extra_tag'),
          none: t('attendance.report_none'),
          page: t('attendance.page'),
          of: t('attendance.of'),
          madeOn: t('attendance.made_on'),
        },
      );
      const name = `${fileNamePart(`${t('attendance.report_title')} ${r.fullName} ${r.classTitle} ${r.from} ${r.to}`)}.pdf`;
      await shareOrSave(pdf, name, `${t('attendance.report_title')} ${r.fullName}`);
    } finally {
      this.making.set(false);
    }
  }
}
