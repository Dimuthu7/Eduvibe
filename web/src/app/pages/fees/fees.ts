import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { catchError, combineLatest, EMPTY, Observable, switchMap, tap } from 'rxjs';
import { ClassesService } from '../../core/api/classes.service';
import { ClassMonth, FeeOverview, FeesService } from '../../core/api/fees.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { FeeStatus } from '../../shared/fee-status';
import { fileNamePart } from '../../shared/download';
import { ExportService } from '../../shared/export.service';
import { WhatsAppActions } from '../../shared/whatsapp-actions';
import { formatLkr, formatMonth, monthOf, todayColombo } from '../../shared/format';
import { MonthNav } from '../../shared/month-nav';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { StudentAvatar } from '../../shared/student-avatar';

/** The class value that means "every class" in the dropdown and the address. */
export const ALL_CLASSES = 'all';

/** Who has paid this month's fee and who has not, for one class or for all of them. The class and month are kept in the address. */
@Component({
  selector: 'app-fees',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StudentAvatar, NgTemplateOutlet, ReactiveFormsModule, RouterLink, FeeStatus, MonthNav, SearchSelect, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'fees.title' | t }}</h1>
        <p class="page-subtitle">{{ 'fees.subtitle' | t }}</p>
      </div>
      <div class="row-wrap">
        <a mat-stroked-button routerLink="/fees/unpaid" [queryParams]="unpaidParams()">
          <mat-icon>notifications_active</mat-icon>
          {{ 'fees.unpaid_attendees' | t }}
        </a>
        @if (isClass()) {
          <button mat-stroked-button type="button" (click)="exportFees()">
            <mat-icon>ios_share</mat-icon>
            {{ 'export.button' | t }}
          </button>
          <button mat-stroked-button type="button" (click)="remind()">
            <mat-icon>chat</mat-icon>
            {{ 'whatsapp.remind' | t }}
          </button>
        }
      </div>
    </header>

    <app-search-select [formControl]="classControl" [label]="'fees.class' | t" [options]="classOptions()" />
    <app-month-nav [month]="chosenMonth()" [current]="currentMonth" (monthChange)="goTo($event)" />

    @if (!chosen()) {
      <div class="empty">
        <mat-icon>payments</mat-icon>
        <p>{{ (classOptions().length <= 1 && classesLoaded() ? 'fees.no_classes' : 'fees.pick_class') | t }}</p>
      </div>
    } @else if (isAll()) {
      @switch (all.status()) {
        @case ('ready') {
          @if (all.data(); as data) {
            <ng-container *ngTemplateOutlet="totals; context: { $implicit: data.totals }" />
            <div class="panel">
              @for (c of data.classes; track c.classId) {
                <button class="panel-row link" type="button" (click)="open(c.classId)">
                  <span class="avatar" aria-hidden="true"><mat-icon>menu_book</mat-icon></span>
                  <div class="row-main">
                    <div class="row-title">{{ c.title }}</div>
                    <div class="row-meta">{{ 'fees.collected' | t }} {{ money(c.totals.collected) }} · {{ 'fees.outstanding' | t }} {{ money(c.totals.outstanding) }}</div>
                    <div class="row-meta">
                      {{ 'fees.status.Paid' | t }} {{ c.totals.paid }} · {{ 'fees.status.PartlyPaid' | t }} {{ c.totals.partlyPaid }} · {{ 'fees.status.Unpaid' | t }} {{ c.totals.unpaid }}
                    </div>
                  </div>
                  <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
                </button>
              } @empty {
                <div class="panel-row muted">{{ 'fees.no_classes' | t }}</div>
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
    } @else {
      @switch (classMonth.status()) {
        @case ('ready') {
          @if (classMonth.data(); as data) {
            <ng-container *ngTemplateOutlet="totals; context: { $implicit: data.totals }" />

            <div class="panel">
              @for (row of data.rows; track row.enrolmentId) {
                <a class="panel-row link" [routerLink]="['/fees', row.enrolmentId]" [queryParams]="{ month: isCurrent() ? null : chosenMonth() }">
                  <app-student-avatar [name]="row.fullName" [of]="row.enrolmentId" [badge]="row.indexNumber" />
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

    <ng-template #totals let-sum>
      <div class="totals" role="group" [attr.aria-label]="'fees.totals' | t">
        <div class="total">
          <span class="muted">{{ 'fees.collected' | t }}</span>
          <strong>{{ money(sum.collected) }}</strong>
        </div>
        <div class="total">
          <span class="muted">{{ 'fees.outstanding' | t }}</span>
          <strong [class.owing]="sum.outstanding > 0">{{ money(sum.outstanding) }}</strong>
        </div>
        <div class="counts muted">
          {{ 'fees.status.Paid' | t }} {{ sum.paid }} · {{ 'fees.status.PartlyPaid' | t }} {{ sum.partlyPaid }} · {{ 'fees.status.Unpaid' | t }} {{ sum.unpaid }}
          @if (sum.freeCard > 0) {
            · {{ 'fees.status.FreeCard' | t }} {{ sum.freeCard }}
          }
        </div>
      </div>
    </ng-template>
  `,
  styles: `
    .totals { display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem 1rem; margin-bottom: 1rem; padding: 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); }
    .total { display: grid; gap: 0.125rem; min-width: 0; }
    .total strong { font: var(--mat-sys-title-large); overflow-wrap: anywhere; }
    .total strong.owing { color: var(--mat-sys-error); }
    .counts { grid-column: 1 / -1; font: var(--mat-sys-body-small); }
    a.link, button.link { color: inherit; text-decoration: none; }
    button.link { width: 100%; border-width: 0; background: none; font: inherit; text-align: start; cursor: pointer; }
    a.link:hover, button.link:hover { background: var(--mat-sys-surface-container); }
    .chevron { color: var(--mat-sys-on-surface-variant); }
  `,
})
export class Fees {
  /** Optional class (a class id, or "all") and month from the ?classId= and ?month= query parameters. */
  readonly classId = input<string>('');
  readonly month = input<string>('');

  private readonly api = inject(FeesService);
  private readonly exporter = inject(ExportService);
  private readonly whatsapp = inject(WhatsAppActions);
  private readonly router = inject(Router);
  private readonly i18n = inject(TranslateService);

  protected readonly currentMonth = monthOf(todayColombo());
  protected readonly money = formatLkr;
  protected readonly classMonth = new Loadable<ClassMonth>();
  protected readonly all = new Loadable<FeeOverview>();
  protected readonly classOptions = signal<SelectOption[]>([]);
  protected readonly classesLoaded = signal(false);
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly chosen = computed(() => this.classId());
  protected readonly isAll = computed(() => this.chosen() === ALL_CLASSES);
  protected readonly isClass = computed(() => !!this.chosen() && !this.isAll());
  protected readonly chosenMonth = computed(() => (/^\d{4}-\d{2}$/.test(this.month()) && this.month() <= this.currentMonth ? this.month() : this.currentMonth));
  protected readonly isCurrent = computed(() => this.chosenMonth() === this.currentMonth);
  protected readonly unpaidParams = computed(() => ({
    month: this.isCurrent() ? null : this.chosenMonth(),
    classId: this.isClass() ? this.chosen() : null,
  }));
  private readonly refresh = signal(0);

  constructor() {
    inject(ClassesService)
      .classes('active')
      .subscribe((list) => {
        this.classOptions.set([
          { value: ALL_CLASSES, label: this.i18n.translate('fees.all_classes') },
          ...list.map((c) => ({ value: c.id, label: c.title })),
        ]);
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

    const destroyed = inject(DestroyRef);
    combineLatest([toObservable(this.chosen), toObservable(this.chosenMonth), toObservable(this.refresh)])
      .pipe(
        switchMap(([classId, month]) => {
          if (!classId) return EMPTY;
          return classId === ALL_CLASSES
            ? this.fill(this.all, this.api.overview(month))
            : this.fill(this.classMonth, this.api.classMonth(classId, month));
        }),
        takeUntilDestroyed(destroyed),
      )
      .subscribe();
  }

  protected goTo(month: string): void {
    void this.router.navigate([], { queryParams: { month: month === this.currentMonth ? null : month }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  protected open(classId: string): void {
    this.setClass(classId);
  }

  protected reload(): void {
    this.refresh.update((n) => n + 1);
  }

  /** Offers WhatsApp reminders to the students who still owe for this class's month; the dialog shows who first. */
  protected remind(): void {
    void this.whatsapp.open({
      path: `/api/fees/classes/${this.chosen()}/whatsapp?month=${this.chosenMonth()}`,
      title: 'whatsapp.fees_title',
      intro: 'whatsapp.fees_intro',
    });
  }

  /** Opens the Export / Share sheet for this class's month. */
  protected exportFees(): void {
    const t = (key: string) => this.i18n.translate(key);
    const classTitle = this.classOptions().find((o) => o.value === this.chosen())?.label ?? '';
    this.exporter.open({
      key: 'fee-status',
      params: { classId: this.chosen(), month: this.chosenMonth() },
      title: t('fees.title'),
      facts: [`${t('export.class')}: ${classTitle}`, `${t('export.month')}: ${formatMonth(this.chosenMonth())}`],
      fileName: fileNamePart(`${t('fees.title')} ${classTitle} ${this.chosenMonth()}`),
    });
  }

  private fill<T>(target: Loadable<T>, source: Observable<T>): Observable<T> {
    target.status.set('loading');
    return source.pipe(
      tap((data) => {
        target.data.set(data);
        target.status.set('ready');
      }),
      catchError((e) => {
        target.error.set(e);
        target.status.set('error');
        return EMPTY;
      }),
    );
  }

  private setClass(id: string): void {
    void this.router.navigate([], { queryParams: { classId: id || null }, queryParamsHandling: 'merge', replaceUrl: true });
  }
}
