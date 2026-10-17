import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { combineLatest, debounceTime, distinctUntilChanged, map, startWith, switchMap } from 'rxjs';
import { ClassesService } from '../../core/api/classes.service';
import { StudentCreated, StudentRow, StudentsService } from '../../core/api/students.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { StudentAvatar } from '../../shared/student-avatar';
import { StudentPhotos } from '../../shared/student-photos';
import { PasswordDialog } from './password-dialog';
import { StudentDialog } from './student-dialog';

type View = 'current' | 'left';

/** A teacher's students across all their classes: search, filter by class, add, and approve sign-up requests. */
@Component({
  selector: 'app-students',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, SearchSelect, StudentAvatar, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'students.title' | t }}</h1>
        @if (students.status() === 'ready') {
          <p class="page-subtitle">{{ 'students.count' | t }}: {{ shown().length }}</p>
        }
      </div>
      <button mat-flat-button type="button" (click)="add()">
        <mat-icon>add</mat-icon>
        {{ 'students.add' | t }}
      </button>
    </header>

    @if (pending().length > 0) {
      <h2 class="section-title first">{{ 'students.pending' | t }} ({{ pending().length }})</h2>
      <p class="muted pending-note">{{ 'students.pending_note' | t }}</p>
      <div class="panel pending">
        @for (row of pending(); track row.enrolmentId) {
          <div class="panel-row request">
            <app-student-avatar [name]="row.fullName" [of]="row.studentId" />
            <div class="row-main">
              <div class="row-title">{{ row.fullName }}</div>
              <div class="row-meta">{{ row.classTitle }}</div>
              <div class="row-meta">{{ row.phone }}</div>
            </div>
            <div class="row-wrap">
              <button mat-icon-button type="button" (click)="reject(row)" [attr.aria-label]="'students.reject' | t">
                <mat-icon>close</mat-icon>
              </button>
              <button mat-flat-button type="button" (click)="approve(row)">{{ 'students.approve' | t }}</button>
            </div>
          </div>
        }
      </div>
    }

    <div class="filters">
      <mat-form-field class="search">
        <mat-label>{{ 'students.search' | t }}</mat-label>
        <input matInput type="search" [formControl]="search" autocomplete="off" />
        <mat-icon matSuffix aria-hidden="true">search</mat-icon>
      </mat-form-field>
      <app-search-select class="class-filter" [formControl]="classFilter" [label]="'students.filter_class' | t" [options]="classOptions()" />
      <mat-button-toggle-group [value]="view()" (change)="view.set($event.value)" [attr.aria-label]="'students.title' | t" hideSingleSelectionIndicator>
        <mat-button-toggle value="current">{{ 'students.current' | t }}</mat-button-toggle>
        <mat-button-toggle value="left">{{ 'students.status.Left' | t }}</mat-button-toggle>
      </mat-button-toggle-group>
    </div>

    @switch (students.status()) {
      @case ('ready') {
        @if (shown().length > 0) {
          <div class="panel">
            @for (row of shown(); track row.enrolmentId) {
              <a class="panel-row link" [routerLink]="['/students', row.studentId]">
                <app-student-avatar [name]="row.fullName" [of]="row.studentId" />
                <div class="row-main">
                  <div class="row-title">{{ row.fullName }}</div>
                  <div class="row-meta">{{ row.classTitle }} · #{{ row.indexNumber }}</div>
                  <div class="row-meta">{{ row.phone }}</div>
                </div>
                @if (row.status === 'Left') {
                  <span class="chip">{{ 'students.status.Left' | t }}</span>
                } @else if (row.feePlan !== 'Full') {
                  <span class="chip">{{ 'students.plan.' + row.feePlan | t }}</span>
                }
                <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
              </a>
            }
          </div>
        } @else {
          <div class="empty">
            <mat-icon>groups</mat-icon>
            <p>{{ emptyKey() | t }}</p>
            @if (!filtered()) {
              <button mat-stroked-button type="button" (click)="add()">{{ 'students.add' | t }}</button>
            }
          </div>
        }
      }
      @case ('error') {
        <div class="empty">
          <p>{{ 'error.load_failed' | t }}</p>
          <button mat-stroked-button type="button" (click)="reload()">{{ 'status.retry' | t }}</button>
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
    .section-title.first { margin-top: 0; }
    .pending-note { margin: 0 0 0.5rem; }
    .pending { margin-bottom: 1.5rem; }
    .request { flex-wrap: wrap; }
    .request .row-main { flex: 1 1 10rem; }
    .request .row-wrap { margin-left: auto; flex-wrap: nowrap; }
    .filters { display: flex; flex-wrap: wrap; gap: 0 1rem; align-items: flex-start; margin-bottom: 0.75rem; }
    .search { flex: 2; min-width: 14rem; }
    .class-filter { flex: 1; min-width: 12rem; }
    mat-button-toggle-group { margin-bottom: 1.5rem; }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .chevron { color: var(--mat-sys-on-surface-variant); }
    .chip { padding: 0.125rem 0.625rem; border-radius: 999px; background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); font: var(--mat-sys-label-small); white-space: nowrap; }
  `,
})
export class Students {
  /** Optional class to start filtered on, from the ?classId= query parameter. */
  readonly classId = input<string>('');

  private readonly api = inject(StudentsService);
  private readonly classesApi = inject(ClassesService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly students = new Loadable<StudentRow[]>();
  protected readonly pendingRows = new Loadable<StudentRow[]>();
  protected readonly view = signal<View>('current');
  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly classFilter = new FormControl('', { nonNullable: true });
  protected readonly classOptions = signal<SelectOption[]>([]);
  private readonly refresh = signal(0);
  private readonly view$ = toObservable(this.view);
  private readonly refresh$ = toObservable(this.refresh);

  protected readonly pending = computed(() => this.pendingRows.data() ?? []);
  /** The list for the chosen view: current students, or those who left. */
  protected readonly shown = computed(() => this.students.data() ?? []);
  private readonly searchText = toSignal(this.search.valueChanges, { initialValue: '' });
  private readonly classChoice = toSignal(this.classFilter.valueChanges, { initialValue: '' });
  protected readonly filtered = computed(() => !!this.searchText() || !!this.classChoice());
  protected readonly emptyKey = computed(() =>
    this.filtered() ? 'students.none_match' : this.view() === 'current' ? 'students.none' : 'students.none_left',
  );

  constructor() {
    inject(StudentPhotos).refresh();
    this.classesApi.classes('all').subscribe((list) => this.classOptions.set(list.map((c) => ({ value: c.id, label: c.title }))));

    queueMicrotask(() => {
      if (this.classId()) this.classFilter.setValue(this.classId());
      const text$ = this.search.valueChanges.pipe(startWith(this.search.value), debounceTime(250), map((q) => q.trim()), distinctUntilChanged());
      const cls$ = this.classFilter.valueChanges.pipe(startWith(this.classFilter.value), distinctUntilChanged());
      combineLatest([text$, cls$, this.view$, this.refresh$])
        .pipe(
          switchMap(([q, classId, view]) => {
            this.students.status.set('loading');
            return this.api.students({ q, classId, status: view === 'left' ? 'left' : 'active' });
          }),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe({
          next: (rows) => {
            this.students.data.set(rows);
            this.students.status.set('ready');
          },
          error: (e) => {
            this.students.error.set(e);
            this.students.status.set('error');
          },
        });
    });
    this.loadPending();
  }

  protected reload(): void {
    this.refresh.update((n) => n + 1);
    this.loadPending();
  }

  private loadPending(): void {
    this.pendingRows.load(this.api.students({ status: 'pending' }));
  }

  protected add(): void {
    this.dialog
      .open<StudentDialog, { classId?: string }, StudentCreated>(StudentDialog, {
        data: { classId: this.classFilter.value || undefined },
        width: '44rem',
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((created) => {
        if (!created) return;
        this.toast.success('students.saved');
        this.reload();
        if (created.oneTimePassword) {
          this.dialog.open(PasswordDialog, {
            data: { name: created.row.fullName, phone: created.row.phone, oneTimePassword: created.oneTimePassword, title: 'students.otp_title' },
            width: '28rem',
            maxWidth: 'calc(100vw - 1rem)',
          });
        }
      });
  }

  protected approve(row: StudentRow): void {
    this.api.approve(row.enrolmentId).subscribe(() => {
      this.toast.success('students.approved');
      this.reload();
    });
  }

  protected reject(row: StudentRow): void {
    this.api.reject(row.enrolmentId).subscribe(() => {
      this.toast.success('students.rejected');
      this.reload();
    });
  }
}
