import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { AdminService } from '../../core/api/admin.service';
import { CatalogStore } from '../../core/api/catalog.service';
import { ClassesService, TuitionClass } from '../../core/api/classes.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { formatLkr } from '../../shared/format';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { slotSummary } from '../../shared/schedule';
import { TranslateService } from '../../core/i18n/translate.service';
import { ClassDialog } from './class-dialog';

type View = 'active' | 'archived';

/** A teacher's classes: filter by institute, add a class in a pop-up, open one for its home screen. */
@Component({
  selector: 'app-classes',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, SearchSelect, MatButtonModule, MatButtonToggleModule, MatIconModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'classes.title' | t }}</h1>
        @if (classes.status() === 'ready') {
          <p class="page-subtitle">{{ 'classes.count' | t }}: {{ shown().length }}</p>
        }
      </div>
      <button mat-flat-button type="button" (click)="add()">
        <mat-icon>add</mat-icon>
        {{ 'classes.add' | t }}
      </button>
    </header>

    <div class="filters">
      <mat-button-toggle-group [value]="view()" (change)="setView($event.value)" [attr.aria-label]="'classes.title' | t" hideSingleSelectionIndicator>
        <mat-button-toggle value="active">{{ 'classes.active' | t }}</mat-button-toggle>
        <mat-button-toggle value="archived">{{ 'classes.archived' | t }}</mat-button-toggle>
      </mat-button-toggle-group>
      @if (instituteOptions().length > 0) {
        <app-search-select class="institute" [formControl]="institute" [label]="'classes.filter_institute' | t" [options]="instituteOptions()" />
      }
    </div>

    @switch (classes.status()) {
      @case ('ready') {
        @if (shown().length > 0) {
          <div class="panel">
            @for (c of shown(); track c.id) {
              <a class="panel-row link" [routerLink]="['/classes', c.id]">
                <span class="avatar" aria-hidden="true"><mat-icon>menu_book</mat-icon></span>
                <div class="row-main">
                  <div class="row-title">{{ c.title }}</div>
                  <div class="row-meta">
                    {{ catalog.subjectName(c.subjectId) }} · {{ catalog.streamName(c.streamId) }} {{ c.examYear }} · {{ 'medium.' + c.medium | t }}
                  </div>
                  <div class="row-meta">{{ c.placeName }} · {{ money(c.monthlyFee) }}</div>
                  <div class="row-meta schedule">{{ schedule(c) }}</div>
                </div>
                <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
              </a>
            }
          </div>
        } @else {
          <div class="empty">
            <mat-icon>menu_book</mat-icon>
            <p>{{ emptyKey() | t }}</p>
            @if (view() === 'active' && !instituteFilter()) {
              <button mat-stroked-button type="button" (click)="add()">{{ 'classes.add' | t }}</button>
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
    .filters { display: flex; flex-wrap: wrap; gap: 0.75rem 1rem; align-items: flex-start; margin-bottom: 0.75rem; }
    .institute { flex: 1; min-width: 14rem; max-width: 22rem; }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .chevron { color: var(--mat-sys-on-surface-variant); }
    .schedule { margin-top: 0.125rem; }
  `,
})
export class Classes {
  private readonly api = inject(ClassesService);
  private readonly admin = inject(AdminService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(TranslateService);
  protected readonly catalog = inject(CatalogStore);

  protected readonly classes = new Loadable<TuitionClass[]>();
  protected readonly view = signal<View>('active');
  protected readonly institute = new FormControl('', { nonNullable: true });
  protected readonly instituteFilter = toSignal(this.institute.valueChanges, { initialValue: '' });
  protected readonly instituteOptions = signal<SelectOption[]>([]);

  protected readonly shown = computed(() => {
    const filter = this.instituteFilter();
    return (this.classes.data() ?? []).filter((c) => !filter || c.instituteId === filter);
  });
  protected readonly emptyKey = computed(() =>
    this.instituteFilter() ? 'classes.none_match' : this.view() === 'active' ? 'classes.none' : 'classes.none_archived',
  );

  constructor() {
    this.catalog.ensureLoaded();
    this.load();
    this.admin.myInstitutes().subscribe((list) => this.instituteOptions.set(list.map((i) => ({ value: i.id, label: i.name }))));
  }

  protected load(): void {
    this.classes.load(this.api.classes(this.view()));
  }

  protected setView(view: View): void {
    this.view.set(view);
    this.load();
  }

  protected add(): void {
    this.dialog
      .open<ClassDialog, null, TuitionClass>(ClassDialog, { data: null, width: '44rem', maxWidth: 'calc(100vw - 1rem)' })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.toast.success('classes.saved');
        if (this.view() === 'active') this.classes.update((all) => [...all, saved].sort((a, b) => a.title.localeCompare(b.title)));
      });
  }

  protected money = formatLkr;
  protected schedule = (c: TuitionClass) => slotSummary(c.slots, (key) => this.i18n.translate(key));
}
