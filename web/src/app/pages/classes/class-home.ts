import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { AttendanceService, ClassSession } from '../../core/api/attendance.service';
import { CatalogStore } from '../../core/api/catalog.service';
import { ClassesService, TuitionClass } from '../../core/api/classes.service';
import { ExamListItem, ExamsService } from '../../core/api/exams.service';
import { ClassMonth, FeesService } from '../../core/api/fees.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { fileNamePart } from '../../shared/download';
import { CardActions } from '../../shared/card-actions';
import { ExportService } from '../../shared/export.service';
import { formatDate, formatLkr, formatMonth, formatTime, monthOf, todayColombo } from '../../shared/format';
import { InviteLinks } from '../students/invite-links';
import { openExamDialog } from '../exams/exam-dialog';
import { ClassDialog } from './class-dialog';
import { openExtraClass } from './extra-session-dialog';

/**
 * One class: the teacher's home screen for it. Details, weekly times and attendance for now; unpaid
 * fees and the latest exam fill in as those features arrive in later sprints.
 */
@Component({
  selector: 'app-class-home',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, InviteLinks, MatButtonModule, MatCardModule, MatChipsModule, MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    <a mat-button routerLink="/classes" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'classes.back' | t }}
    </a>

    @switch (klass.status()) {
      @case ('ready') {
        @if (current(); as c) {
          <header class="page-header">
            <div>
              <h1 class="page-title">{{ c.title }}</h1>
              <p class="page-subtitle">
                {{ catalog.subjectName(c.subjectId) }} · {{ catalog.streamName(c.streamId) }} {{ c.examYear }} · {{ 'medium.' + c.medium | t }}
              </p>
            </div>
            <div class="row-wrap">
              <a mat-stroked-button routerLink="/students" [queryParams]="{ classId: c.id }">
                <mat-icon>groups</mat-icon>
                {{ 'nav.students' | t }}
              </a>
              <button mat-stroked-button type="button" (click)="exportClassList(c)">
                <mat-icon>ios_share</mat-icon>
                {{ 'export.button' | t }}
              </button>
              @if (c.status === 'Active') {
                <button mat-stroked-button type="button" (click)="edit(c)">
                  <mat-icon>edit</mat-icon>
                  {{ 'classes.edit' | t }}
                </button>
              }
              <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="'common.actions' | t">
                <mat-icon>more_vert</mat-icon>
              </button>
              <mat-menu #menu="matMenu">
                @if (c.status === 'Active') {
                  <button mat-menu-item type="button" (click)="printCards(c)">
                    <mat-icon>badge</mat-icon>
                    <span>{{ 'cards.title' | t }}</span>
                  </button>
                  <button mat-menu-item type="button" (click)="archive(c)">
                    <mat-icon>archive</mat-icon>
                    <span>{{ 'classes.archive' | t }}</span>
                  </button>
                } @else {
                  <button mat-menu-item type="button" (click)="restore(c)">
                    <mat-icon>unarchive</mat-icon>
                    <span>{{ 'classes.restore' | t }}</span>
                  </button>
                }
              </mat-menu>
            </div>
          </header>

          @if (c.status === 'Archived') {
            <p class="note" role="status"><mat-icon>archive</mat-icon> {{ 'classes.archived_note' | t }}</p>
          }

          <mat-card appearance="outlined">
            <mat-card-content class="facts">
              <div><span class="muted">{{ 'classes.place' | t }}</span><strong>{{ c.placeName }}</strong></div>
              <div><span class="muted">{{ 'classes.fee' | t }}</span><strong>{{ money(c.monthlyFee) }} {{ 'classes.per_month' | t }}</strong></div>
            </mat-card-content>
          </mat-card>

          <h2 class="section-title">{{ 'classes.schedule' | t }}</h2>
          <div class="panel">
            @for (slot of c.slots; track slot.id) {
              <div class="panel-row">
                <span class="avatar day" aria-hidden="true">{{ 'day.short.' + slot.day | t }}</span>
                <div class="row-main">
                  <div class="row-title">{{ 'day.' + slot.day | t }}</div>
                  <div class="row-meta">{{ time(slot.start) }} – {{ time(slot.end) }}</div>
                </div>
              </div>
            }
          </div>

          @if (c.status === 'Active') {
            <h2 class="section-title">{{ 'classes.attendance' | t }}</h2>
            <div class="panel">
              @for (session of todays(); track session.id) {
                <a class="panel-row link" [routerLink]="['/attendance', session.id]">
                  <mat-icon class="lead" aria-hidden="true">today</mat-icon>
                  <div class="row-main">
                    <div class="row-title">{{ 'classes.today' | t }} · {{ time(session.start) }} – {{ time(session.end) }}</div>
                    <div class="row-meta">
                      @if (session.taken) {
                        {{ 'attendance.present' | t }} {{ session.present + session.late }} · {{ 'attendance.absent' | t }} {{ session.absent }}
                      } @else {
                        {{ 'attendance.not_marked' | t }}
                      }
                    </div>
                  </div>
                  <span class="chevron-label">{{ (session.taken ? 'attendance.review' : 'attendance.mark') | t }}</span>
                  <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
                </a>
              } @empty {
                <div class="panel-row">
                  <mat-icon class="lead" aria-hidden="true">today</mat-icon>
                  <div class="row-main">
                    <div class="row-title">{{ 'classes.today' | t }}</div>
                    <div class="row-meta">{{ 'attendance.no_class_today' | t }}</div>
                  </div>
                </div>
              }
              <button class="panel-row link" type="button" (click)="addExtra(c)">
                <mat-icon class="lead" aria-hidden="true">event_available</mat-icon>
                <div class="row-main">
                  <div class="row-title">{{ 'classes.extra' | t }}</div>
                  <div class="row-meta">{{ 'classes.extra_hint' | t }}</div>
                </div>
                <mat-icon class="chevron" aria-hidden="true">add</mat-icon>
              </button>
              <a class="panel-row link" routerLink="/attendance/summary" [queryParams]="{ classId: c.id }">
                <mat-icon class="lead" aria-hidden="true">insights</mat-icon>
                <div class="row-main">
                  <div class="row-title">{{ 'attendance.summary' | t }}</div>
                  <div class="row-meta">{{ 'attendance.summary_subtitle' | t }}</div>
                </div>
                <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
              </a>
            </div>
          }

          @if (c.status === 'Active') {
            <h2 class="section-title">{{ 'classes.fees' | t }}</h2>
            <div class="panel">
              <a class="panel-row link" routerLink="/fees" [queryParams]="{ classId: c.id }">
                <mat-icon class="lead" aria-hidden="true">payments</mat-icon>
                <div class="row-main">
                  <div class="row-title">{{ monthName(thisMonth) }}</div>
                  <div class="row-meta">
                    @if (monthFees(); as m) {
                      {{ 'fees.status.Paid' | t }} {{ m.totals.paid }} · {{ 'fees.status.PartlyPaid' | t }} {{ m.totals.partlyPaid }} · {{ 'fees.status.Unpaid' | t }} {{ m.totals.unpaid }}
                      · {{ 'fees.outstanding' | t }} {{ money(m.totals.outstanding) }}
                    } @else {
                      {{ 'fees.subtitle' | t }}
                    }
                  </div>
                </div>
                <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
              </a>
            </div>
          }

          <app-invite-links [classId]="c.id" [archived]="c.status === 'Archived'" />

          @if (c.status === 'Active') {
            <h2 class="section-title">{{ 'classes.latest_exam' | t }}</h2>
            <div class="panel">
              @if (latestExam(); as e) {
                <a class="panel-row link" [routerLink]="['/exams', e.id]">
                  <mat-icon class="lead" aria-hidden="true">quiz</mat-icon>
                  <div class="row-main">
                    <div class="row-title">{{ e.name }}</div>
                    <div class="row-meta">
                      {{ date(e.heldOn) }} · {{ 'exams.marked_of' | t }} {{ e.marked }} / {{ e.students }}
                      @if (e.averagePercent !== null) {
                        · {{ 'exams.average' | t }} {{ e.averagePercent }}%
                      }
                    </div>
                  </div>
                  <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
                </a>
              } @else {
                <div class="panel-row">
                  <mat-icon class="lead" aria-hidden="true">quiz</mat-icon>
                  <div class="row-main">
                    <div class="row-title">{{ 'classes.latest_exam' | t }}</div>
                    <div class="row-meta">{{ 'exams.none_class' | t }}</div>
                  </div>
                </div>
              }
              <button class="panel-row link" type="button" (click)="newExam(c)">
                <mat-icon class="lead" aria-hidden="true">note_add</mat-icon>
                <div class="row-main">
                  <div class="row-title">{{ 'exams.new' | t }}</div>
                  <div class="row-meta">{{ 'exams.new_hint' | t }}</div>
                </div>
                <mat-icon class="chevron" aria-hidden="true">add</mat-icon>
              </button>
              <a class="panel-row link" routerLink="/exams" [queryParams]="{ classId: c.id }">
                <mat-icon class="lead" aria-hidden="true">list_alt</mat-icon>
                <div class="row-main">
                  <div class="row-title">{{ 'exams.title' | t }}</div>
                  <div class="row-meta">{{ 'exams.subtitle' | t }}</div>
                </div>
                <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
              </a>
            </div>
          }
        }
      }
      @case ('error') {
        <div class="empty">
          <p>{{ 'classes.not_found' | t }}</p>
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
    .facts { display: grid; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); gap: 1rem; }
    .facts div { display: flex; flex-direction: column; }
    .note { display: flex; gap: 0.5rem; align-items: center; margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); }
    .avatar.day { font-size: 0.8125rem; }
    a.link { color: inherit; text-decoration: none; }
    button.link { width: 100%; border-width: 0; background: none; color: inherit; font: inherit; text-align: start; cursor: pointer; }
    a.link:hover, button.link:hover { background: var(--mat-sys-surface-container); }
    .lead { color: var(--mat-sys-primary); }
    .chevron { color: var(--mat-sys-on-surface-variant); }
    .chevron-label { color: var(--mat-sys-primary); font: var(--mat-sys-label-large); }
  `,
})
export class ClassHome {
  /** The class id from the route. */
  readonly id = input.required<string>();

  private readonly api = inject(ClassesService);
  private readonly attendance = inject(AttendanceService);
  private readonly fees = inject(FeesService);
  private readonly exams = inject(ExamsService);
  private readonly exporter = inject(ExportService);
  private readonly cardActions = inject(CardActions);
  private readonly i18n = inject(TranslateService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  protected readonly catalog = inject(CatalogStore);

  protected readonly klass = new Loadable<TuitionClass>();
  protected readonly current = computed(() => this.klass.data());
  protected readonly todays = signal<ClassSession[]>([]);
  protected readonly monthFees = signal<ClassMonth | null>(null);
  /** The newest exam of this class; null when there is none yet. */
  protected readonly latestExam = signal<ExamListItem | null>(null);
  protected readonly thisMonth = monthOf(todayColombo());
  protected readonly monthName = formatMonth;
  protected readonly money = formatLkr;
  protected readonly time = formatTime;
  protected readonly date = formatDate;

  constructor() {
    this.catalog.ensureLoaded();
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.klass.load(this.api.classById(this.id()));
    this.attendance.sessions(todayColombo()).subscribe((list) => this.todays.set(list.filter((x) => x.classId === this.id())));
    this.fees.classMonth(this.id(), this.thisMonth).subscribe((m) => this.monthFees.set(m));
    this.exams.list(this.id(), 1).subscribe((list) => this.latestExam.set(list[0] ?? null));
  }

  protected edit(c: TuitionClass): void {
    this.dialog
      .open<ClassDialog, TuitionClass, TuitionClass>(ClassDialog, { data: c, width: '44rem', maxWidth: 'calc(100vw - 1rem)' })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.klass.data.set(saved);
        this.toast.success('classes.saved');
      });
  }

  /** Opens the Export / Share sheet for the class list: every active student with contact details and fee plan. */
  protected exportClassList(c: TuitionClass): void {
    const t = (key: string) => this.i18n.translate(key);
    this.exporter.open({
      key: 'class-list',
      params: { classId: c.id },
      title: t('classes.list_title'),
      facts: [`${t('export.class')}: ${c.title}`],
      fileName: fileNamePart(`${t('classes.list_title')} ${c.title}`),
    });
  }

  protected printCards(c: TuitionClass): void {
    void this.cardActions.open({ classId: c.id, classTitle: c.title, fileName: fileNamePart(`${this.i18n.translate('cards.title')} ${c.title}`) });
  }

  protected addExtra(c: TuitionClass): void {
    openExtraClass(this.dialog, c.id).subscribe((session) => {
      if (!session) return;
      this.toast.success('classes.extra_saved');
      void this.router.navigate(['/attendance'], { queryParams: { date: session.date } });
    });
  }

  protected newExam(c: TuitionClass): void {
    openExamDialog(this.dialog, { classId: c.id }).subscribe((exam) => {
      if (exam) void this.router.navigate(['/exams', exam.id]);
    });
  }

  protected archive(c: TuitionClass): void {
    this.api.archiveClass(c.id).subscribe((saved) => {
      this.klass.data.set(saved);
      this.toast.success('classes.archived_toast');
    });
  }

  protected restore(c: TuitionClass): void {
    this.api.restoreClass(c.id).subscribe((saved) => {
      this.klass.data.set(saved);
      this.toast.success('classes.restored_toast');
    });
  }
}
