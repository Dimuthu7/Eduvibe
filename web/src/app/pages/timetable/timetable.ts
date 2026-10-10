import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AdminService } from '../../core/api/admin.service';
import { CatalogStore } from '../../core/api/catalog.service';
import { ClassesService, TimetableEntry } from '../../core/api/classes.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { formatTime } from '../../shared/format';
import { SearchSelect, SelectOption } from '../../shared/search-select';

interface DayGroup {
  day: number;
  entries: TimetableEntry[];
}

/** The teacher's week: every active class by day and time, with clashing times marked. */
@Component({
  selector: 'app-timetable',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, SearchSelect, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'timetable.title' | t }}</h1>
        <p class="page-subtitle">{{ 'timetable.subtitle' | t }}</p>
      </div>
    </header>

    @if (instituteOptions().length > 0) {
      <app-search-select class="institute" [formControl]="institute" [label]="'classes.filter_institute' | t" [options]="instituteOptions()" />
    }

    @switch (entries.status()) {
      @case ('ready') {
        @if (overlapCount() > 0) {
          <p class="warning" role="alert"><mat-icon>warning</mat-icon> {{ 'timetable.overlap_banner' | t }}</p>
        }
        @for (group of groups(); track group.day) {
          <h2 class="section-title day-title">
            {{ 'day.' + group.day | t }}
            @if (group.day === today) {
              <span class="today">{{ 'timetable.today' | t }}</span>
            }
          </h2>
          <div class="panel">
            @for (entry of group.entries; track entry.classId + entry.start) {
              <a class="panel-row link" [class.clash]="entry.overlapsWith.length > 0" [routerLink]="['/classes', entry.classId]">
                <div class="time">
                  <strong>{{ time(entry.start) }}</strong>
                  <span class="muted">{{ time(entry.end) }}</span>
                </div>
                <div class="row-main">
                  <div class="row-title">{{ entry.title }}</div>
                  <div class="row-meta">{{ catalog.subjectName(entry.subjectId) }} · {{ entry.placeName }}</div>
                  @if (entry.overlapsWith.length > 0) {
                    <div class="overlap">
                      <mat-icon>warning</mat-icon>
                      {{ 'timetable.overlaps_with' | t }} {{ titles(entry.overlapsWith) }}
                    </div>
                  }
                </div>
                <mat-icon class="chevron" aria-hidden="true">chevron_right</mat-icon>
              </a>
            }
          </div>
        } @empty {
          <div class="empty">
            <mat-icon>calendar_month</mat-icon>
            <p>{{ 'timetable.none' | t }}</p>
            <a mat-stroked-button routerLink="/classes">{{ 'classes.title' | t }}</a>
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
      }
    }
  `,
  styles: `
    .institute { display: block; max-width: 22rem; margin-bottom: 0.5rem; }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .day-title { display: flex; align-items: center; gap: 0.5rem; }
    .today { padding: 0.125rem 0.625rem; border-radius: 999px; background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); font: var(--mat-sys-label-small); }
    .time { display: flex; flex-direction: column; min-width: 4.75rem; font-size: 0.875rem; }
    .chevron { color: var(--mat-sys-on-surface-variant); }
    .warning { display: flex; gap: 0.5rem; align-items: center; margin: 0 0 0.5rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .clash { box-shadow: inset 4px 0 0 var(--mat-sys-error); }
    .overlap { display: flex; gap: 0.25rem; align-items: center; margin-top: 0.25rem; color: var(--mat-sys-error); font: var(--mat-sys-label-medium); }
    .overlap mat-icon { width: 1rem; height: 1rem; font-size: 1rem; }
  `,
})
export class Timetable {
  private readonly api = inject(ClassesService);
  private readonly admin = inject(AdminService);
  protected readonly catalog = inject(CatalogStore);

  protected readonly entries = new Loadable<TimetableEntry[]>();
  protected readonly institute = new FormControl('', { nonNullable: true });
  private readonly filter = toSignal(this.institute.valueChanges, { initialValue: '' });
  protected readonly instituteOptions = signal<SelectOption[]>([]);
  protected readonly time = formatTime;
  /** Today in Asia/Colombo, 1 (Monday) to 7 (Sunday), to highlight the day. */
  protected readonly today = ((): number => {
    const name = new Intl.DateTimeFormat('en-US', { weekday: 'short', timeZone: 'Asia/Colombo' }).format(new Date());
    return ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'].indexOf(name) + 1;
  })();

  // The server flags clashes across every institute, so titles are looked up in the full list.
  private readonly titleById = computed(() => new Map((this.entries.data() ?? []).map((e) => [e.classId, e.title])));

  private readonly visible = computed(() => {
    const filter = this.filter();
    return (this.entries.data() ?? []).filter((e) => !filter || e.instituteId === filter);
  });
  protected readonly overlapCount = computed(() => this.visible().filter((e) => e.overlapsWith.length > 0).length);
  protected readonly groups = computed<DayGroup[]>(() => {
    const byDay = new Map<number, TimetableEntry[]>();
    for (const entry of this.visible()) byDay.set(entry.day, [...(byDay.get(entry.day) ?? []), entry]);
    return [...byDay.entries()].sort(([a], [b]) => a - b).map(([day, entries]) => ({ day, entries }));
  });

  constructor() {
    this.catalog.ensureLoaded();
    this.load();
    this.admin.myInstitutes().subscribe((list) => this.instituteOptions.set(list.map((i) => ({ value: i.id, label: i.name }))));
  }

  protected load(): void {
    this.entries.load(this.api.timetable());
  }

  protected titles(ids: string[]): string {
    const names = this.titleById();
    return ids.map((id) => names.get(id) ?? '').filter(Boolean).join(', ');
  }
}
