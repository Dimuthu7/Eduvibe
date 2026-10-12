import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { catchError, combineLatest, EMPTY, switchMap, tap } from 'rxjs';
import { ClassesService } from '../../core/api/classes.service';
import { ClassMonth, FeesService } from '../../core/api/fees.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { FeeStatus } from '../../shared/fee-status';
import { addMonths, formatLkr, formatMonth, monthOf, todayColombo } from '../../shared/format';
import { SearchSelect, SelectOption } from '../../shared/search-select';

/** Who has paid this month's fee and who has not, for one class. The class and month are kept in the address. */
@Component({
  selector: 'app-fees',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, FeeStatus, SearchSelect, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'fees.title' | t }}</h1>
        <p class="page-subtitle">{{ 'fees.subtitle' | t }}</p>
      </div>
    </header>

    <app-search-select [formControl]="classControl" [label]="'fees.class' | t" [options]="classOptions()" />

    <nav class="month-nav" [attr.aria-label]="'fees.choose_month' | t">
      <button mat-icon-button type="button" (click)="go(-1)" [disabled]="!canGoBack()" [attr.aria-label]="'fees.previous_month' | t">
        <mat-icon>chevron_left</mat-icon>
      </button>
      <div class="month">
        <strong>{{ label() }}</strong>
        @if (isCurrent()) {
          <span class="tag">{{ 'fees.this_month' | t }}</span>
        }
      </div>
      <button mat-icon-button type="button" (click)="go(1)" [disabled]="isCurrent()" [attr.aria-label]="'fees.next_month' | t">
        <mat-icon>chevron_right</mat-icon>
      </button>
      @if (!isCurrent()) {
        <button mat-button type="button" (click)="goTo(currentMonth)">{{ 'fees.this_month' | t }}</button>
      }
    </nav>

    @if (!chosen()) {
      <div class="empty">
        <mat-icon>payments</mat-icon>
        <p>{{ (classOptions().length === 0 && classesLoaded() ? 'fees.no_classes' : 'fees.pick_class') | t }}</p>
      </div>
    } @else {
      @switch (overview.status()) {
        @case ('ready') {
          @if (overview.data(); as data) {
            <div class="totals" role="group" [attr.aria-label]="'fees.totals' | t">
              <div class="total">
                <span class="muted">{{ 'fees.collected' | t }}</span>
                <strong>{{ money(data.totals.collected) }}</strong>
              </div>
              <div class="total">
                <span class="muted">{{ 'fees.outstanding' | t }}</span>
                <strong [class.owing]="data.totals.outstanding > 0">{{ money(data.totals.outstanding) }}</strong>
              </div>
              <div class="counts muted">
                {{ 'fees.status.Paid' | t }} {{ data.totals.paid }} · {{ 'fees.status.PartlyPaid' | t }} {{ data.totals.partlyPaid }} ·
                {{ 'fees.status.Unpaid' | t }} {{ data.totals.unpaid }}
                @if (data.totals.freeCard > 0) {
                  · {{ 'fees.status.FreeCard' | t }} {{ data.totals.freeCard }}
                }
              </div>
            </div>

            <div class="panel">
              @for (row of data.rows; track row.enrolmentId) {
                <a class="panel-row link" [routerLink]="['/fees', row.enrolmentId]" [queryParams]="{ month: isCurrent() ? null : month() }">
                  <span class="avatar" aria-hidden="true">{{ row.indexNumber }}</span>
                  <div class="row-main">
                    <div class="row-title">{{ row.fullName }}</div>
                    <div class="row-meta">
                      @if (row.status === 'FreeCard') {
                        {{ 'fees.free_card' | t }}
                      } @else {
                        {{ money(row.paid) }} / {{ money(row.due) }}
                      }
                    </div>
                  </div>
                  <app-fee-status [status]="row.status" />
                  <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
                </a>
              } @empty {
                <div class="panel-row muted">{{ 'fees.no_students' | t }}</div>
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
        }
      }
    }
  `,
  styles: `
    .month-nav { display: flex; align-items: center; gap: 0.25rem; margin-bottom: 1rem; }
    .month { display: flex; flex: 1; flex-wrap: wrap; gap: 0.5rem; align-items: center; justify-content: center; min-width: 0; text-align: center; }
    .tag { padding: 0.125rem 0.625rem; border-radius: 999px; background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); font: var(--mat-sys-label-small); white-space: nowrap; }
    .totals { display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem 1rem; margin-bottom: 1rem; padding: 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); }
    .total { display: grid; gap: 0.125rem; min-width: 0; }
    .total strong { font: var(--mat-sys-title-large); overflow-wrap: anywhere; }
    .total strong.owing { color: var(--mat-sys-error); }
    .counts { grid-column: 1 / -1; font: var(--mat-sys-body-small); }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .chevron { color: var(--mat-sys-on-surface-variant); }
  `,
})
export class Fees {
  /** Optional class and month from the ?classId= and ?month= query parameters. */
  readonly classId = input<string>('');
  readonly month = input<string>('');

  private readonly api = inject(FeesService);
  private readonly router = inject(Router);

  protected readonly currentMonth = monthOf(todayColombo());
  protected readonly money = formatLkr;
  protected readonly overview = new Loadable<ClassMonth>();
  protected readonly classOptions = signal<SelectOption[]>([]);
  protected readonly classesLoaded = signal(false);
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly chosen = computed(() => this.classId());
  protected readonly chosenMonth = computed(() => (/^\d{4}-\d{2}$/.test(this.month()) && this.month() <= this.currentMonth ? this.month() : this.currentMonth));
  protected readonly isCurrent = computed(() => this.chosenMonth() === this.currentMonth);
  protected readonly canGoBack = computed(() => this.chosenMonth() > addMonths(this.currentMonth, -36));
  protected readonly label = computed(() => formatMonth(this.chosenMonth()));
  private readonly refresh = signal(0);

  constructor() {
    inject(ClassesService)
      .classes('active')
      .subscribe((list) => {
        this.classOptions.set(list.map((c) => ({ value: c.id, label: c.title })));
        this.classesLoaded.set(true);
        // Nothing chosen yet: start on the first class so the page is useful straight away.
        if (!this.classId() && list.length > 0) this.setClass(list[0].id);
      });

    // The dropdown follows the address, and the address follows the dropdown.
    effect(() => {
      const id = this.classId();
      untracked(() => {
        if (this.classControl.value !== id) this.classControl.setValue(id, { emitEvent: false });
      });
    });
    this.classControl.valueChanges.pipe(takeUntilDestroyed()).subscribe((id) => this.setClass(id));

    combineLatest([toObservable(this.chosen), toObservable(this.chosenMonth), toObservable(this.refresh)])
      .pipe(
        switchMap(([classId, month]) => {
          if (!classId) return EMPTY;
          this.overview.status.set('loading');
          return this.api.classMonth(classId, month).pipe(
            tap((data) => {
              this.overview.data.set(data);
              this.overview.status.set('ready');
            }),
            catchError((e) => {
              this.overview.error.set(e);
              this.overview.status.set('error');
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe();
  }

  protected go(months: number): void {
    this.goTo(addMonths(this.chosenMonth(), months));
  }

  protected goTo(month: string): void {
    void this.router.navigate([], { queryParams: { month: month === this.currentMonth ? null : month }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  protected reload(): void {
    this.refresh.update((n) => n + 1);
  }

  private setClass(id: string): void {
    void this.router.navigate([], { queryParams: { classId: id || null }, queryParamsHandling: 'merge', replaceUrl: true });
  }
}
