import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { AdminService, Institute } from '../../core/api/admin.service';
import { AttendanceService, ClassSession } from '../../core/api/attendance.service';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { formatTime, todayColombo } from '../../shared/format';

/** A teacher's start screen: today's classes first, then the rest as later sprints add it. */
@Component({
  selector: 'app-home',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatCardModule, MatIconModule, MatListModule, TranslatePipe],
  template: `
    <h1 class="page-title">{{ 'home.welcome' | t }}, {{ auth.user()?.firstName }}</h1>
    <h2 class="section-title first">{{ 'home.today' | t }}</h2>
    @switch (today.status()) {
      @case ('ready') {
        <div class="panel">
          @for (s of today.data(); track s.id) {
            <a class="panel-row link" [routerLink]="['/attendance', s.id]">
              <div class="time">
                <strong>{{ time(s.start) }}</strong>
                <span class="muted">{{ time(s.end) }}</span>
              </div>
              <div class="row-main">
                <div class="row-title">{{ s.classTitle }}</div>
                <div class="row-meta">
                  @if (s.taken) {
                    {{ 'attendance.present' | t }} {{ s.present + s.late }} · {{ 'attendance.absent' | t }} {{ s.absent }}
                  } @else {
                    {{ 'attendance.not_marked' | t }}
                  }
                </div>
              </div>
              @if (s.taken) {
                <mat-icon class="done" aria-hidden="true">check_circle</mat-icon>
              } @else {
                <span class="mark">{{ 'attendance.mark' | t }}</span>
              }
            </a>
          } @empty {
            <div class="panel-row muted">{{ 'home.no_classes_today' | t }}</div>
          }
        </div>
      }
      @case ('error') {
        <p class="muted">{{ 'error.load_failed' | t }}</p>
      }
      @default {
        <div class="skeleton"></div>
      }
    }

    <mat-card appearance="outlined" class="institutes">
      <mat-card-header>
        <mat-card-title>{{ 'home.institutes' | t }}</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        @switch (institutes.status()) {
          @case ('ready') {
            <mat-list>
              @for (institute of institutes.data(); track institute.id) {
                <mat-list-item>
                  <mat-icon matListItemIcon>apartment</mat-icon>
                  <span matListItemTitle>{{ institute.name }}</span>
                  @if (institute.district) {
                    <span matListItemLine>{{ institute.district }}</span>
                  }
                </mat-list-item>
              } @empty {
                <p class="muted">{{ 'home.no_institutes' | t }}</p>
              }
            </mat-list>
          }
          @case ('error') {
            <p class="muted">{{ 'error.load_failed' | t }}</p>
          }
          @default {
            <div class="skeleton"></div>
          }
        }
      </mat-card-content>
    </mat-card>
    <p class="muted">{{ 'home.more_soon' | t }}</p>
  `,
  styles: `
    .section-title.first { margin-top: 1rem; }
    .institutes { margin-top: 1.5rem; }
    .time { display: flex; flex-direction: column; min-width: 4.5rem; font: var(--mat-sys-body-small); }
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .done { color: var(--mat-sys-primary); }
    .mark { color: var(--mat-sys-primary); font: var(--mat-sys-label-large); }
  `,
})
export class Home {
  protected readonly auth = inject(AuthService);
  protected readonly institutes = new Loadable<Institute[]>();
  protected readonly today = new Loadable<ClassSession[]>();
  protected readonly time = formatTime;

  constructor() {
    this.institutes.load(inject(AdminService).myInstitutes());
    this.today.load(inject(AttendanceService).sessions(todayColombo()));
  }
}
