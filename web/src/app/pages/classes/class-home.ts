import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { CatalogStore } from '../../core/api/catalog.service';
import { ClassesService, TuitionClass } from '../../core/api/classes.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { formatLkr, formatTime } from '../../shared/format';
import { ClassDialog } from './class-dialog';

interface Section {
  icon: string;
  title: string;
}

/**
 * One class: the teacher's home screen for it. Details and weekly times for now; today's session,
 * attendance, unpaid fees and the latest exam fill in as those features arrive in later sprints.
 */
@Component({
  selector: 'app-class-home',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatCardModule, MatChipsModule, MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    <a mat-button routerLink="/classes" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'classes.back' | t }}
    </a>

    @switch (klass.status()) {
      @case ('ready') {
        @if (current(); as c) {
          <header class="page-header">
            <div>
              <h1 class="page-title">{{ c.title }}</h1>
              <p class="page-subtitle">
                {{ catalog.subjectName(c.subjectId) }} · {{ catalog.streamName(c.streamId) }} {{ c.examYear }} · {{ 'medium.' + c.medium | t }}
              </p>
            </div>
            <div class="row-wrap">
              @if (c.status === 'Active') {
                <button mat-stroked-button type="button" (click)="edit(c)">
                  <mat-icon>edit</mat-icon>
                  {{ 'classes.edit' | t }}
                </button>
              }
              <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="'common.actions' | t">
                <mat-icon>more_vert</mat-icon>
              </button>
              <mat-menu #menu="matMenu">
                @if (c.status === 'Active') {
                  <button mat-menu-item type="button" (click)="archive(c)">
                    <mat-icon>archive</mat-icon>
                    <span>{{ 'classes.archive' | t }}</span>
                  </button>
                } @else {
                  <button mat-menu-item type="button" (click)="restore(c)">
                    <mat-icon>unarchive</mat-icon>
                    <span>{{ 'classes.restore' | t }}</span>
                  </button>
                }
              </mat-menu>
            </div>
          </header>

          @if (c.status === 'Archived') {
            <p class="note" role="status"><mat-icon>archive</mat-icon> {{ 'classes.archived_note' | t }}</p>
          }

          <mat-card appearance="outlined">
            <mat-card-content class="facts">
              <div><span class="muted">{{ 'classes.place' | t }}</span><strong>{{ c.placeName }}</strong></div>
              <div><span class="muted">{{ 'classes.fee' | t }}</span><strong>{{ money(c.monthlyFee) }} {{ 'classes.per_month' | t }}</strong></div>
            </mat-card-content>
          </mat-card>

          <h2 class="section-title">{{ 'classes.schedule' | t }}</h2>
          <div class="panel">
            @for (slot of c.slots; track slot.id) {
              <div class="panel-row">
                <span class="avatar day" aria-hidden="true">{{ 'day.short.' + slot.day | t }}</span>
                <div class="row-main">
                  <div class="row-title">{{ 'day.' + slot.day | t }}</div>
                  <div class="row-meta">{{ time(slot.start) }} – {{ time(slot.end) }}</div>
                </div>
              </div>
            }
          </div>

          <h2 class="section-title">{{ 'classes.sections' | t }}</h2>
          <div class="sections">
            @for (section of sections; track section.title) {
              <mat-card appearance="outlined" class="section">
                <mat-card-content>
                  <mat-icon aria-hidden="true">{{ section.icon }}</mat-icon>
                  <div class="row-title">{{ section.title | t }}</div>
                  <div class="row-meta">{{ 'classes.coming_soon' | t }}</div>
                </mat-card-content>
              </mat-card>
            }
          </div>
        }
      }
      @case ('error') {
        <div class="empty">
          <p>{{ 'classes.not_found' | t }}</p>
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
    .back { margin-left: -0.75rem; margin-bottom: 0.5rem; }
    .facts { display: grid; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); gap: 1rem; }
    .facts div { display: flex; flex-direction: column; }
    .note { display: flex; gap: 0.5rem; align-items: center; margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); }
    .avatar.day { font-size: 0.8125rem; }
    .sections { display: grid; grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr)); gap: 0.75rem; }
    .section { margin: 0; }
    .section mat-icon { color: var(--mat-sys-primary); margin-bottom: 0.25rem; }
  `,
})
export class ClassHome {
  /** The class id from the route. */
  readonly id = input.required<string>();

  private readonly api = inject(ClassesService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  protected readonly catalog = inject(CatalogStore);

  protected readonly klass = new Loadable<TuitionClass>();
  protected readonly current = computed(() => this.klass.data());
  protected readonly money = formatLkr;
  protected readonly time = formatTime;
  protected readonly sections: Section[] = [
    { icon: 'today', title: 'classes.today' },
    { icon: 'fact_check', title: 'classes.attendance' },
    { icon: 'payments', title: 'classes.unpaid' },
    { icon: 'quiz', title: 'classes.latest_exam' },
  ];

  constructor() {
    this.catalog.ensureLoaded();
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.klass.load(this.api.classById(this.id()));
  }

  protected edit(c: TuitionClass): void {
    this.dialog
      .open<ClassDialog, TuitionClass, TuitionClass>(ClassDialog, { data: c, width: '44rem', maxWidth: 'calc(100vw - 1rem)' })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.klass.data.set(saved);
        this.toast.success('classes.saved');
      });
  }

  protected archive(c: TuitionClass): void {
    this.api.archiveClass(c.id).subscribe((saved) => {
      this.klass.data.set(saved);
      this.toast.success('classes.archived_toast');
    });
  }

  protected restore(c: TuitionClass): void {
    this.api.restoreClass(c.id).subscribe((saved) => {
      this.klass.data.set(saved);
      this.toast.success('classes.restored_toast');
    });
  }
}
