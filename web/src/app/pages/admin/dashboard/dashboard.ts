import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AdminOverviewService, Stat } from '../../../core/api/admin.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Loadable } from '../../../core/state/loadable';

/** The headline numbers, in the order they are shown. A number the server does not send is left out. */
const TILES = [
  { key: 'teachers', icon: 'school', link: '/admin/teachers' },
  { key: 'students', icon: 'groups', link: null },
  { key: 'classes', icon: 'class', link: null },
  { key: 'institutes', icon: 'apartment', link: '/admin/institutes' },
] as const;

/** Splits the server's numbers into the headline tiles and the teachers-per-plan list. */
export function splitStats(stats: Stat[]): { tiles: { key: string; icon: string; link: string | null; value: number }[]; plans: Stat[]; inactive: number } {
  const value = (key: string) => stats.find((s) => s.key === key)?.value;
  return {
    tiles: TILES.filter((t) => value(t.key) !== undefined).map((t) => ({ ...t, value: value(t.key)! })),
    plans: stats.filter((s) => s.key.startsWith('plan:')),
    inactive: value('teachers_inactive') ?? 0,
  };
}

/** Super Admin start screen: how many teachers, students, classes and institutes there are, and how many teachers are on each plan. */
@Component({
  selector: 'app-admin-dashboard',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'dashboard.title' | t }}</h1>
        <p class="page-subtitle">{{ 'dashboard.subtitle' | t }}</p>
      </div>
      <a mat-stroked-button routerLink="/admin/audit">
        <mat-icon>history</mat-icon>
        {{ 'audit.title' | t }}
      </a>
    </header>

    @switch (stats.status()) {
      @case ('ready') {
        @if (view(); as v) {
          <div class="tiles">
            @for (tile of v.tiles; track tile.key) {
              @if (tile.link) {
                <a class="tile link" [routerLink]="tile.link">
                  <mat-icon aria-hidden="true">{{ tile.icon }}</mat-icon>
                  <strong>{{ tile.value }}</strong>
                  <span>{{ 'dashboard.' + tile.key | t }}</span>
                  @if (tile.key === 'teachers' && v.inactive > 0) {
                    <span class="muted small">{{ v.inactive }} {{ 'dashboard.inactive' | t }}</span>
                  }
                </a>
              } @else {
                <div class="tile">
                  <mat-icon aria-hidden="true">{{ tile.icon }}</mat-icon>
                  <strong>{{ tile.value }}</strong>
                  <span>{{ 'dashboard.' + tile.key | t }}</span>
                </div>
              }
            }
          </div>

          <h2 class="section-title">{{ 'dashboard.by_plan' | t }}</h2>
          <div class="panel">
            @for (plan of v.plans; track plan.key) {
              <div class="panel-row">
                <div class="row-main row-title">{{ plan.label }}</div>
                <strong class="count">{{ plan.value }}</strong>
              </div>
            } @empty {
              <div class="panel-row muted">{{ 'dashboard.no_plans' | t }}</div>
            }
          </div>
          <p class="muted note">{{ 'dashboard.plan_note' | t }}</p>
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
    .tiles { display: grid; grid-template-columns: repeat(2, 1fr); gap: 0.75rem; margin-bottom: 1.5rem; }
    @media (min-width: 720px) { .tiles { grid-template-columns: repeat(4, 1fr); } }
    .tile { display: grid; gap: 0.125rem; align-content: start; padding: 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); color: inherit; text-decoration: none; }
    .tile mat-icon { color: var(--mat-sys-primary); margin-bottom: 0.25rem; }
    .tile strong { font: var(--mat-sys-headline-medium); }
    .tile span { font: var(--mat-sys-label-large); color: var(--mat-sys-on-surface-variant); }
    .tile .small { font: var(--mat-sys-body-small); }
    a.tile:hover { background: var(--mat-sys-surface-container-high); }
    .count { font: var(--mat-sys-title-large); }
    .note { margin: 0.5rem 0 0; font: var(--mat-sys-body-small); }
  `,
})
export class AdminDashboard {
  private readonly api = inject(AdminOverviewService);
  protected readonly stats = new Loadable<Stat[]>();
  protected readonly view = computed(() => splitStats(this.stats.data() ?? []));

  constructor() {
    this.load();
  }

  protected load(): void {
    this.stats.load(this.api.dashboard());
  }
}
