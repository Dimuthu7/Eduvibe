import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { AdminService, TeacherSummary } from '../../../core/api/admin.service';
import { CatalogStore } from '../../../core/api/catalog.service';
import { Assignment, PlansService } from '../../../core/api/plans.service';
import { PrivacyService } from '../../../core/api/privacy.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslateService } from '../../../core/i18n/translate.service';
import { Loadable } from '../../../core/state/loadable';
import { ToastService } from '../../../core/ui/toast.service';
import { fileNamePart, saveBlob } from '../../../shared/download';
import { CreatedTeacher, TeacherDialog } from './teacher-dialog';
import { TeacherEraseDialog } from './teacher-erase-dialog';
import { TeacherPlanDialog } from './teacher-plan-dialog';

interface Handout {
  name: string;
  phone: string;
  password: string;
}

/** Super Admin: every teacher in one list, a pop-up to add one, and one-time passwords to hand out. */
@Component({
  selector: 'app-admin-teachers',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    TranslatePipe,
  ],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'teachers.title' | t }}</h1>
        @if (teachers.status() === 'ready') {
          <p class="page-subtitle">{{ 'teachers.count' | t }}: {{ list().length }}</p>
        }
      </div>
      <button mat-flat-button type="button" (click)="add()">
        <mat-icon>add</mat-icon>
        {{ 'teachers.add' | t }}
      </button>
    </header>

    @if (handout(); as h) {
      <mat-card appearance="outlined" class="handout">
        <mat-card-content>
          <p>{{ 'teachers.password_for' | t }} <strong>{{ h.name }}</strong> ({{ h.phone }})</p>
          <p class="otp">{{ h.password }}</p>
          <p class="muted">{{ 'teachers.password_once' | t }}</p>
          <div class="row-wrap">
            <button mat-stroked-button type="button" (click)="copy(h.password)">
              <mat-icon>content_copy</mat-icon>
              {{ 'teachers.copy' | t }}
            </button>
            <a mat-stroked-button [href]="whatsapp(h)" target="_blank" rel="noopener">
              <mat-icon>send</mat-icon>
              {{ 'teachers.share' | t }}
            </a>
            <button mat-button type="button" (click)="handout.set(null)">{{ 'teachers.done' | t }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }

    @switch (teachers.status()) {
      @case ('ready') {
        @if (list().length > 0) {
          <mat-form-field class="search" subscriptSizing="dynamic">
            <mat-icon matPrefix>search</mat-icon>
            <input matInput type="search" [ngModel]="query()" (ngModelChange)="query.set($event)" [placeholder]="'teachers.search' | t" />
          </mat-form-field>
        }
        @if (shown().length > 0) {
          <div class="panel">
            @for (teacher of shown(); track teacher.id) {
              <div class="panel-row" [class.inactive]="!teacher.isActive">
                <span class="avatar" aria-hidden="true">{{ initials(teacher) }}</span>
                <div class="row-main">
                  <div class="row-title">
                    {{ teacher.fullName }}
                    @if (!teacher.isActive) {
                      <span class="status off">{{ 'teachers.inactive' | t }}</span>
                    } @else if (teacher.mustChangePassword) {
                      <span class="status wait">{{ 'teachers.invited' | t }}</span>
                    }
                    @if (planOf(teacher); as plan) {
                      <span class="status plan">{{ plan }}</span>
                    }
                  </div>
                  <div class="row-meta">
                    {{ teacher.phone }}
                    @if (teacher.district) {
                      · {{ teacher.district }}
                    }
                    @if (catalog.streamName(teacher.streamId)) {
                      · {{ catalog.streamName(teacher.streamId) }}
                    }
                  </div>
                  @if (teacher.subjectIds.length > 0) {
                    <mat-chip-set class="subjects" [attr.aria-label]="'person.subjects' | t">
                      @for (name of catalog.subjectNames(teacher.subjectIds); track name) {
                        <mat-chip>{{ name }}</mat-chip>
                      }
                    </mat-chip-set>
                  }
                </div>
                <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="'common.actions' | t">
                  <mat-icon>more_vert</mat-icon>
                </button>
                <mat-menu #menu="matMenu">
                  <button mat-menu-item type="button" (click)="openPlan(teacher)">
                    <mat-icon>workspace_premium</mat-icon>
                    <span>{{ 'teachers.plan' | t }}</span>
                  </button>
                  <button mat-menu-item type="button" (click)="reset(teacher)">
                    <mat-icon>key</mat-icon>
                    <span>{{ 'teachers.reset' | t }}</span>
                  </button>
                  <button mat-menu-item type="button" (click)="setActive(teacher, !teacher.isActive)">
                    <mat-icon>{{ teacher.isActive ? 'block' : 'check_circle' }}</mat-icon>
                    <span>{{ (teacher.isActive ? 'teachers.deactivate' : 'teachers.activate') | t }}</span>
                  </button>
                  <button mat-menu-item type="button" (click)="download(teacher)">
                    <mat-icon>download</mat-icon>
                    <span>{{ 'privacy.download' | t }}</span>
                  </button>
                  @if (!teacher.isActive) {
                    <button mat-menu-item type="button" (click)="erase(teacher)">
                      <mat-icon>delete_forever</mat-icon>
                      <span>{{ 'privacy.erase' | t }}</span>
                    </button>
                  }
                </mat-menu>
              </div>
            }
          </div>
        } @else {
          <div class="empty">
            <mat-icon>school</mat-icon>
            <p>{{ (list().length === 0 ? 'teachers.none' : 'teachers.no_match') | t }}</p>
            @if (list().length === 0) {
              <button mat-stroked-button type="button" (click)="add()">{{ 'teachers.add' | t }}</button>
            }
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
        <div class="skeleton"></div>
      }
    }
  `,
  styles: `
    .handout { margin-bottom: 1rem; }
    .otp { margin: 0.5rem 0; font: 600 1.75rem/1.2 ui-monospace, monospace; letter-spacing: 0.08em; user-select: all; }
    .search { width: 100%; max-width: 22rem; margin-bottom: 1rem; }
    .inactive .avatar, .inactive .row-title { opacity: 0.55; }
    .subjects { margin-top: 0.375rem; }
    .status { margin-left: 0.375rem; vertical-align: middle; padding: 0.125rem 0.625rem; border-radius: 999px; font: var(--mat-sys-label-small); white-space: nowrap; }
    .status.off { background: var(--mat-sys-surface-container-highest); color: var(--mat-sys-on-surface-variant); }
    .status.wait { background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
    .status.plan { background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
  `,
})
export class AdminTeachers {
  private readonly api = inject(AdminService);
  private readonly plans = inject(PlansService);
  private readonly privacy = inject(PrivacyService);
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(TranslateService);
  private readonly toast = inject(ToastService);
  protected readonly catalog = inject(CatalogStore);

  protected readonly teachers = new Loadable<TeacherSummary[]>();
  protected readonly handout = signal<Handout | null>(null);
  protected readonly query = signal('');
  /** The plan each teacher is on today, when it is not the default. */
  private readonly assignments = signal<Map<string, string>>(new Map());
  protected readonly list = computed(() => this.teachers.data() ?? []);
  protected readonly shown = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return this.list();
    return this.list().filter((t) =>
      [t.fullName, t.phone, t.district ?? '', this.catalog.streamName(t.streamId), ...this.catalog.subjectNames(t.subjectIds)]
        .join(' ')
        .toLowerCase()
        .includes(q),
    );
  });

  constructor() {
    this.catalog.ensureLoaded();
    this.load();
    this.loadAssignments();
  }

  protected load(): void {
    this.teachers.load(this.api.teachers());
  }

  private loadAssignments(): void {
    // The plan names are a nicety on the list; if they do not load, the list still works.
    this.plans.assignments().subscribe((rows: Assignment[]) => this.assignments.set(new Map(rows.map((a) => [a.teacherId, a.planName]))));
  }

  protected planOf(teacher: TeacherSummary): string | undefined {
    return this.assignments().get(teacher.id);
  }

  protected openPlan(teacher: TeacherSummary): void {
    this.dialog
      .open<TeacherPlanDialog, TeacherSummary>(TeacherPlanDialog, { data: teacher, width: '40rem', maxWidth: 'calc(100vw - 1rem)', autoFocus: 'first-tabbable' })
      .afterClosed()
      .subscribe(() => this.loadAssignments());
  }

  protected add(): void {
    this.catalog.ensureLoaded(true);
    this.dialog
      .open<TeacherDialog, void, CreatedTeacher>(TeacherDialog, { width: '40rem', maxWidth: 'calc(100vw - 1rem)', autoFocus: 'first-tabbable' })
      .afterClosed()
      .subscribe((created) => {
        if (!created) return;
        this.show(created.teacher, created.oneTimePassword);
        this.toast.success('teachers.created');
        this.teachers.update((all) => [...all, created.teacher].sort((a, b) => a.fullName.localeCompare(b.fullName)));
      });
  }

  protected initials(teacher: TeacherSummary): string {
    return (teacher.firstName.charAt(0) + teacher.lastName.charAt(0)).trim();
  }

  protected reset(teacher: TeacherSummary): void {
    this.api.resetPassword(teacher.id).subscribe({
      next: ({ oneTimePassword }) => {
        this.show(teacher, oneTimePassword);
        this.replace({ ...teacher, mustChangePassword: true });
      },
    });
  }

  protected setActive(teacher: TeacherSummary, isActive: boolean): void {
    this.api.setTeacherActive(teacher.id, isActive).subscribe({
      next: () => {
        this.toast.success(isActive ? 'teachers.activated' : 'teachers.deactivated');
        this.replace({ ...teacher, isActive });
      },
    });
  }

  protected download(teacher: TeacherSummary): void {
    this.privacy.exportTeacher(teacher.id).subscribe((file) => saveBlob(file, `${fileNamePart(`${this.i18n.translate('privacy.file')} ${teacher.fullName}`)}.json`));
  }

  protected erase(teacher: TeacherSummary): void {
    this.dialog
      .open<TeacherEraseDialog, TeacherSummary, boolean>(TeacherEraseDialog, { data: teacher, width: '32rem', maxWidth: 'calc(100vw - 1rem)', autoFocus: 'first-tabbable' })
      .afterClosed()
      .subscribe((done) => {
        if (!done) return;
        this.toast.success('privacy.erased');
        this.teachers.update((all) => all.filter((t) => t.id !== teacher.id));
      });
  }

  protected async copy(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.toast.success('teachers.copied');
    } catch {
      // Clipboard access can be refused; the password is selectable on screen.
      this.toast.error('teachers.copy_failed');
    }
  }

  protected whatsapp(h: Handout): string {
    const text = this.i18n.translate('teachers.share_message').replace('{password}', h.password);
    return `https://wa.me/${h.phone.replace('+', '')}?text=${encodeURIComponent(text)}`;
  }

  private replace(changed: TeacherSummary): void {
    this.teachers.update((all) => all.map((t) => (t.id === changed.id ? changed : t)));
  }

  private show(teacher: TeacherSummary, password: string): void {
    this.handout.set({ name: teacher.fullName, phone: teacher.phone, password });
  }
}
