import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { CatalogStore } from '../../core/api/catalog.service';
import { ClassesService, Venue } from '../../core/api/classes.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { VenueDialog } from './venue-dialog';

/** A teacher's private venues: home class, hired hall and the like. */
@Component({
  selector: 'app-venues',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'venues.title' | t }}</h1>
        <p class="page-subtitle">{{ 'venues.subtitle' | t }}</p>
      </div>
      <button mat-flat-button type="button" (click)="open()">
        <mat-icon>add</mat-icon>
        {{ 'venues.add' | t }}
      </button>
    </header>

    @switch (venues.status()) {
      @case ('ready') {
        @if (list().length > 0) {
          <div class="panel">
            @for (venue of list(); track venue.id) {
              <div class="panel-row" [class.hidden-row]="!venue.isActive">
                <span class="avatar" aria-hidden="true"><mat-icon>place</mat-icon></span>
                <div class="row-main">
                  <div class="row-title">
                    {{ venue.name }}
                    @if (!venue.isActive) {
                      <span class="status off">{{ 'venues.hidden' | t }}</span>
                    }
                  </div>
                  <div class="row-meta">
                    {{ venue.district }}@if (venue.town) { · {{ venue.town }} }@if (venue.address) { · {{ venue.address }} }
                  </div>
                </div>
                <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="'common.actions' | t">
                  <mat-icon>more_vert</mat-icon>
                </button>
                <mat-menu #menu="matMenu">
                  <button mat-menu-item type="button" (click)="open(venue)">
                    <mat-icon>edit</mat-icon>
                    <span>{{ 'venues.edit' | t }}</span>
                  </button>
                  <button mat-menu-item type="button" (click)="setActive(venue, !venue.isActive)">
                    <mat-icon>{{ venue.isActive ? 'visibility_off' : 'visibility' }}</mat-icon>
                    <span>{{ (venue.isActive ? 'venues.hide' : 'venues.show') | t }}</span>
                  </button>
                </mat-menu>
              </div>
            }
          </div>
        } @else {
          <div class="empty">
            <mat-icon>place</mat-icon>
            <p>{{ 'venues.none' | t }}</p>
            <button mat-stroked-button type="button" (click)="open()">{{ 'venues.add' | t }}</button>
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
    .hidden-row .avatar, .hidden-row .row-title { opacity: 0.55; }
    .status { margin-left: 0.375rem; padding: 0.125rem 0.625rem; border-radius: 999px; background: var(--mat-sys-surface-container-highest); color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-label-small); }
  `,
})
export class Venues {
  private readonly api = inject(ClassesService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly catalog = inject(CatalogStore);

  protected readonly venues = new Loadable<Venue[]>();
  protected readonly list = computed(() => this.venues.data() ?? []);

  constructor() {
    this.catalog.ensureLoaded();
    this.load();
  }

  protected load(): void {
    this.venues.load(this.api.venues());
  }

  protected open(venue?: Venue): void {
    this.dialog
      .open<VenueDialog, Venue | null, Venue>(VenueDialog, { data: venue ?? null, width: '40rem', maxWidth: 'calc(100vw - 1rem)' })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.replace(saved, !venue);
        this.toast.success('venues.saved');
      });
  }

  protected setActive(venue: Venue, isActive: boolean): void {
    this.api.updateVenue(venue.id, { ...venue, isActive }).subscribe((saved) => this.replace(saved, false));
  }

  private replace(saved: Venue, added: boolean): void {
    this.venues.update((all) =>
      (added ? [...all, saved] : all.map((v) => (v.id === saved.id ? saved : v))).sort((a, b) => a.name.localeCompare(b.name)),
    );
  }
}
