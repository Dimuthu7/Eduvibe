import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { debounceTime } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { AdminOverviewService, AdminService, AuditPage, AuditRow, TeacherSummary } from '../../../core/api/admin.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslateService } from '../../../core/i18n/translate.service';
import { Loadable } from '../../../core/state/loadable';
import { formatDateTime } from '../../../shared/format';
import { SearchSelect } from '../../../shared/search-select';
import { changesOf, FieldChange, spaced } from './audit-diff';

const ACTIONS = ['created', 'updated', 'deleted'] as const;
const ICONS: Record<AuditRow['action'], string> = { created: 'add_circle', updated: 'edit', deleted: 'delete' };

/** Super Admin: every audited change across all teachers, newest first, narrowed by teacher, kind of record, action or dates. */
@Component({
  selector: 'app-admin-audit',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, SearchSelect, MatButtonModule, MatExpansionModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, TranslatePipe],
  template: `
    <a mat-button routerLink="/admin/dashboard" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'dashboard.title' | t }}
    </a>
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'audit.title' | t }}</h1>
        <p class="page-subtitle">{{ 'audit.subtitle' | t }}</p>
      </div>
    </header>

    <mat-expansion-panel class="filters">
      <mat-expansion-panel-header>
        <mat-panel-title>{{ 'audit.filters' | t }}</mat-panel-title>
        @if (filterCount() > 0) {
          <mat-panel-description>{{ filterCount() }} {{ 'audit.filters_on' | t }}</mat-panel-description>
        }
      </mat-expansion-panel-header>
      <form class="form-grid" [formGroup]="filters" novalidate>
        <app-search-select class="full" formControlName="teacherId" [label]="'audit.teacher' | t" [options]="teacherOptions()" />
        <mat-form-field>
          <mat-label>{{ 'audit.record' | t }}</mat-label>
          <mat-select formControlName="entity">
            <mat-option value="">{{ 'audit.all' | t }}</mat-option>
            @for (e of entities(); track e) {
              <mat-option [value]="e">{{ entityName(e) }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'audit.action' | t }}</mat-label>
          <mat-select formControlName="action">
            <mat-option value="">{{ 'audit.all' | t }}</mat-option>
            @for (a of actions; track a) {
              <mat-option [value]="a">{{ 'audit.action.' + a | t }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'audit.from' | t }}</mat-label>
          <input matInput type="date" formControlName="from" [max]="filters.controls.to.value || null" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'audit.to' | t }}</mat-label>
          <input matInput type="date" formControlName="to" [min]="filters.controls.from.value || null" />
        </mat-form-field>
      </form>
    </mat-expansion-panel>

    @switch (page.status()) {
      @case ('ready') {
        <div class="panel">
          @for (row of rows(); track row.id) {
            <div class="entry">
              <button type="button" class="panel-row head" [attr.aria-expanded]="open() === row.id" (click)="toggle(row.id)">
                <mat-icon class="kind" [class]="row.action" aria-hidden="true">{{ icon(row) }}</mat-icon>
                <div class="row-main">
                  <div class="row-title">{{ entityName(row.entity) }} · {{ 'audit.action.' + row.action | t }}</div>
                  <div class="row-meta">{{ when(row) }}</div>
                  <div class="row-meta">{{ who(row) }}</div>
                </div>
                <mat-icon class="chevron" aria-hidden="true">{{ open() === row.id ? 'expand_less' : 'expand_more' }}</mat-icon>
              </button>
              @if (open() === row.id) {
                <dl class="changes">
                  @for (c of changes(row); track c.field) {
                    <dt>{{ c.field }}</dt>
                    <dd>
                      @if (row.action === 'updated') {
                        <span class="from">{{ c.from ?? '–' }}</span>
                        <mat-icon class="to-arrow" aria-hidden="true">arrow_forward</mat-icon>
                        <span>{{ c.to ?? '–' }}</span>
                      } @else {
                        {{ (row.action === 'deleted' ? c.from : c.to) ?? '–' }}
                      }
                    </dd>
                  } @empty {
                    <dd class="muted">{{ 'audit.no_details' | t }}</dd>
                  }
                </dl>
              }
            </div>
          } @empty {
            <div class="empty">
              <mat-icon>history</mat-icon>
              <p>{{ 'audit.none' | t }}</p>
            </div>
          }
        </div>
        @if (hasMore()) {
          <div class="more">
            <button mat-stroked-button type="button" [disabled]="loadingMore()" (click)="more()">{{ 'audit.more' | t }}</button>
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
    .back { margin-left: -0.75rem; margin-bottom: 0.5rem; }
    .filters { margin-bottom: 1rem; }
    .filters .form-grid { padding-top: 0.5rem; }
    .entry + .entry { border-top: 1px solid var(--mat-sys-outline-variant); }
    button.head { width: 100%; border: 0; background: none; color: inherit; font: inherit; text-align: start; cursor: pointer; }
    button.head:hover { background: var(--mat-sys-surface-container); }
    .kind { flex: none; color: var(--mat-sys-primary); }
    .kind.deleted { color: var(--mat-sys-error); }
    .chevron { flex: none; color: var(--mat-sys-on-surface-variant); }
    .changes { display: grid; grid-template-columns: minmax(0, 9rem) minmax(0, 1fr); gap: 0.25rem 0.75rem; margin: 0; padding: 0 1rem 1rem 3.5rem; font: var(--mat-sys-body-small); }
    .changes dt { color: var(--mat-sys-on-surface-variant); overflow-wrap: anywhere; }
    .changes dd { margin: 0; overflow-wrap: anywhere; }
    .changes .muted { grid-column: 1 / -1; }
    .from { color: var(--mat-sys-on-surface-variant); text-decoration: line-through; }
    .to-arrow { width: 1rem; height: 1rem; font-size: 1rem; vertical-align: middle; margin: 0 0.125rem; }
    .more { display: flex; justify-content: center; margin: 1rem 0; }
  `,
})
export class AdminAudit {
  private readonly api = inject(AdminOverviewService);
  private readonly admin = inject(AdminService);
  private readonly i18n = inject(TranslateService);
  private readonly destroyed = inject(DestroyRef);

  protected readonly actions = ACTIONS;
  protected readonly page = new Loadable<AuditPage>();
  protected readonly rows = computed(() => this.page.data()?.items ?? []);
  protected readonly hasMore = computed(() => this.page.data()?.hasMore ?? false);
  protected readonly loadingMore = signal(false);
  protected readonly open = signal<string | null>(null);
  protected readonly entities = signal<string[]>([]);
  private readonly teachers = signal<TeacherSummary[]>([]);
  protected readonly teacherOptions = computed(() => this.teachers().map((t) => ({ value: t.id, label: t.fullName })));

  protected readonly filters = inject(FormBuilder).nonNullable.group({ teacherId: [''], entity: [''], action: [''], from: [''], to: [''] });
  /** How many filters are set, shown on the collapsed panel. */
  protected readonly filterCount = signal(0);

  constructor() {
    this.admin.teachers().subscribe((list) => this.teachers.set(list));
    this.api.auditEntities().subscribe((list) => this.entities.set(list));
    this.filters.valueChanges.pipe(debounceTime(250), takeUntilDestroyed(this.destroyed)).subscribe(() => {
      this.filterCount.set(Object.values(this.filters.getRawValue()).filter((v) => v !== '').length);
      this.reload();
    });
    this.reload();
  }

  protected reload(): void {
    this.open.set(null);
    this.page.load(this.api.audit(this.filters.getRawValue()));
  }

  protected more(): void {
    this.loadingMore.set(true);
    const shown = this.page.data();
    this.api.audit({ ...this.filters.getRawValue(), skip: this.rows().length }).subscribe({
      next: (next) => {
        // The filters may have changed while this loaded; those rows belong to the old list.
        if (this.page.data() !== shown) return this.loadingMore.set(false);
        this.page.update((all) => ({ items: [...all.items, ...next.items], hasMore: next.hasMore }));
        this.loadingMore.set(false);
      },
      error: () => this.loadingMore.set(false),
    });
  }

  protected toggle(id: string): void {
    this.open.update((current) => (current === id ? null : id));
  }

  protected icon(row: AuditRow): string {
    return ICONS[row.action];
  }

  protected entityName(entity: string): string {
    const key = `audit.entity.${entity}`;
    const text = this.i18n.translate(key);
    return text === key ? spaced(entity) : text;
  }

  protected when(row: AuditRow): string {
    return formatDateTime(row.at);
  }

  protected who(row: AuditRow): string {
    const by = row.userName ?? this.i18n.translate('audit.system');
    const teacher = this.teachers().find((t) => t.id === row.tenantId)?.fullName;
    return teacher ? `${by} · ${this.i18n.translate('audit.for')} ${teacher}` : by;
  }

  protected changes(row: AuditRow): FieldChange[] {
    return changesOf(row.before, row.after);
  }
}
