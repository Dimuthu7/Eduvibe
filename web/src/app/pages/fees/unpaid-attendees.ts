import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { catchError, combineLatest, EMPTY, switchMap, tap } from 'rxjs';
import { ClassesService } from '../../core/api/classes.service';
import { FeesService, UnpaidAttendees as UnpaidResult } from '../../core/api/fees.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { formatLkr, monthOf, todayColombo } from '../../shared/format';
import { MonthNav } from '../../shared/month-nav';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { StudentAvatar } from '../../shared/student-avatar';

/** Students who came to class in the month and still owe for it: the people to remind first. */
@Component({
  selector: 'app-unpaid-attendees',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StudentAvatar, ReactiveFormsModule, RouterLink, MonthNav, SearchSelect, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <a mat-button routerLink="/fees" [queryParams]="backParams()" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'fees.title' | t }}
    </a>
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'fees.unpaid_attendees' | t }}</h1>
        <p class="page-subtitle">{{ 'fees.unpaid_subtitle' | t }}</p>
      </div>
    </header>

    <app-search-select [formControl]="classControl" [label]="'fees.class' | t" [options]="classOptions()" />
    <app-month-nav [month]="chosenMonth()" [current]="currentMonth" (monthChange)="goTo($event)" />

    @switch (result.status()) {
      @case ('ready') {
        @if (result.data(); as data) {
          @if (data.rows.length > 0) {
            <p class="summary">
              <strong>{{ data.rows.length }}</strong>
              <span class="muted">{{ 'fees.unpaid_count' | t }} · {{ 'fees.outstanding' | t }} {{ money(data.outstanding) }}</span>
            </p>
            <div class="panel">
              @for (row of data.rows; track row.enrolmentId) {
                <a class="panel-row link" [routerLink]="['/fees', row.enrolmentId]" [queryParams]="{ month: isCurrent() ? null : chosenMonth() }">
                  <app-student-avatar [name]="row.fullName" [of]="row.enrolmentId" [badge]="row.indexNumber" />
                  <div class="row-main">
                    <div class="row-title">{{ row.fullName }}</div>
                    <div class="row-meta">{{ row.classTitle }} · {{ 'fees.came' | t }} {{ row.attended }}</div>
                    @if (row.paid > 0) {
                      <div class="row-meta">{{ money(row.paid) }} / {{ money(row.due) }}</div>
                    }
                  </div>
                  <strong class="owing">{{ money(row.outstanding) }}</strong>
                  <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
                </a>
              }
            </div>
          } @else {
            <div class="empty">
              <mat-icon>task_alt</mat-icon>
              <p>{{ 'fees.unpaid_none' | t }}</p>
            </div>
          }
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
      }
    }
  `,
  styles: `
    .back { margin-left: -0.75rem; margin-bottom: 0.5rem; }
    .summary { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: baseline; margin: 0 0 0.75rem; }
    .summary strong { font: var(--mat-sys-headline-small); }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .owing { color: var(--mat-sys-error); white-space: nowrap; }
    .chevron { color: var(--mat-sys-on-surface-variant); }
  `,
})
export class UnpaidAttendees {
  /** Optional class and month from the ?classId= and ?month= query parameters. No class means all of them. */
  readonly classId = input<string>('');
  readonly month = input<string>('');

  private readonly api = inject(FeesService);
  private readonly router = inject(Router);
  private readonly i18n = inject(TranslateService);

  protected readonly currentMonth = monthOf(todayColombo());
  protected readonly money = formatLkr;
  protected readonly result = new Loadable<UnpaidResult>();
  protected readonly classOptions = signal<SelectOption[]>([]);
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly chosenMonth = computed(() => (/^\d{4}-\d{2}$/.test(this.month()) && this.month() <= this.currentMonth ? this.month() : this.currentMonth));
  protected readonly isCurrent = computed(() => this.chosenMonth() === this.currentMonth);
  protected readonly backParams = computed(() => ({ classId: this.classId() || 'all', month: this.isCurrent() ? null : this.chosenMonth() }));
  private readonly refresh = signal(0);

  constructor() {
    inject(ClassesService)
      .classes('active')
      .subscribe((list) =>
        this.classOptions.set([{ value: '', label: this.i18n.translate('fees.all_classes') }, ...list.map((c) => ({ value: c.id, label: c.title }))]),
      );

    effect(() => {
      const id = this.classId();
      untracked(() => {
        if (this.classControl.value !== id) this.classControl.setValue(id, { emitEvent: false });
      });
    });
    this.classControl.valueChanges.pipe(takeUntilDestroyed()).subscribe((id) => this.navigate({ classId: id || null }));

    combineLatest([toObservable(this.classId), toObservable(this.chosenMonth), toObservable(this.refresh)])
      .pipe(
        switchMap(([classId, month]) => {
          this.result.status.set('loading');
          return this.api.unpaidAttendees(month, classId || undefined).pipe(
            tap((data) => {
              this.result.data.set(data);
              this.result.status.set('ready');
            }),
            catchError((e) => {
              this.result.error.set(e);
              this.result.status.set('error');
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe();
  }

  protected goTo(month: string): void {
    this.navigate({ month: month === this.currentMonth ? null : month });
  }

  protected reload(): void {
    this.refresh.update((n) => n + 1);
  }

  private navigate(queryParams: Record<string, string | null>): void {
    void this.router.navigate([], { queryParams, queryParamsHandling: 'merge', replaceUrl: true });
  }
}
