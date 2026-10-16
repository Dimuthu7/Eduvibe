import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { finalize } from 'rxjs';
import { DashboardLayout, DashboardService, WidgetSetting } from '../../core/api/dashboard.service';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { formatDate, todayColombo } from '../../shared/format';
import { Customise } from './customise';
import { isDefaultLayout, resolveLayout } from './layout';
import { AttendanceWidget } from './widgets/attendance-widget';
import { ExamsWidget } from './widgets/exams-widget';
import { FeesWidget } from './widgets/fees-widget';
import { InstitutesWidget } from './widgets/institutes-widget';
import { JoinsWidget } from './widgets/joins-widget';
import { TodayWidget } from './widgets/today-widget';

/**
 * A teacher's start screen: widgets the teacher turns on and orders, each loading its own data so a slow one never holds up the rest.
 * The layout is saved on the server, so it follows the teacher to another phone.
 */
@Component({
  selector: 'app-home',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatButtonModule, MatIconModule, TranslatePipe, Customise,
    TodayWidget, JoinsWidget, FeesWidget, AttendanceWidget, ExamsWidget, InstitutesWidget,
  ],
  template: `
    @if (editing()) {
      <app-customise [initial]="widgets()" [busy]="saving()" (save)="save($event)" (cancel)="editing.set(false)" />
    } @else {
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ 'home.welcome' | t }}, {{ auth.user()?.firstName }}</h1>
          <p class="page-subtitle">{{ today() }}</p>
        </div>
        <button mat-stroked-button type="button" (click)="editing.set(true)">
          <mat-icon>tune</mat-icon>
          {{ 'dashboard.customise.button' | t }}
        </button>
      </div>

      @if (layout.status() === 'idle' || layout.status() === 'loading') {
        <div class="skeleton"></div>
        <div class="skeleton"></div>
      } @else {
        @for (w of active(); track w.key) {
          @switch (w.key) {
            @case ('today') { <app-today-widget /> }
            @case ('joins') { <app-joins-widget /> }
            @case ('fees') { <app-fees-widget /> }
            @case ('attendance') { <app-attendance-widget /> }
            @case ('exams') { <app-exams-widget /> }
            @case ('institutes') { <app-institutes-widget /> }
          }
        } @empty {
          <div class="empty">
            <mat-icon aria-hidden="true">widgets</mat-icon>
            <p>{{ 'dashboard.none_on' | t }}</p>
            <button mat-flat-button type="button" (click)="editing.set(true)">{{ 'dashboard.customise.button' | t }}</button>
          </div>
        }
      }
    }
  `,
})
export class Home {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(DashboardService);
  private readonly toast = inject(ToastService);

  protected readonly layout = new Loadable<DashboardLayout>();
  protected readonly editing = signal(false);
  protected readonly saving = signal(false);
  protected readonly today = computed(() => formatDate(todayColombo()));

  /** The saved layout made whole. If it could not be read, the default one shows and the screen still works. */
  protected readonly widgets = computed(() => resolveLayout(this.layout.data()?.widgets ?? []));
  protected readonly active = computed(() => this.widgets().filter((w) => w.enabled));

  constructor() {
    this.layout.load(this.api.layout());
  }

  protected save(list: WidgetSetting[]): void {
    this.saving.set(true);
    // A layout that is just the default is cleared rather than saved, so the teacher keeps getting the app's own order as it grows.
    const request = isDefaultLayout(list) ? this.api.resetLayout() : this.api.saveLayout(list);
    request.pipe(finalize(() => this.saving.set(false))).subscribe((saved) => {
      this.layout.data.set(saved);
      this.layout.status.set('ready');
      this.editing.set(false);
      this.toast.success('dashboard.saved');
    });
  }
}
