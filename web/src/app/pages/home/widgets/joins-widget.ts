import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DashboardService } from '../../../core/api/dashboard.service';
import { StudentRow } from '../../../core/api/students.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslateService } from '../../../core/i18n/translate.service';
import { Loadable } from '../../../core/state/loadable';
import { formatDate } from '../../../shared/format';
import { WidgetCard } from './widget-card';
import { WidgetState } from './widget-state';

const SHOWN = 3;

/** Students who used a sign-up link and wait for the teacher to approve them. */
@Component({
  selector: 'app-joins-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, WidgetCard, WidgetState],
  template: `
    <app-widget-card icon="person_add" title="dashboard.joins.title" [count]="state.data()?.length ?? 0" link="/students" linkLabel="dashboard.joins.review">
      <app-widget-state [status]="state.status()" (retry)="load()">
        @if (state.data(); as rows) {
          @for (row of rows.slice(0, shown); track row.enrolmentId) {
            <a class="panel-row link" routerLink="/students">
              <span class="avatar">{{ row.firstName.charAt(0) }}</span>
              <div class="row-main">
                <div class="row-title">{{ row.fullName }}</div>
                <div class="row-meta">{{ row.classTitle }} · {{ date(row.joinedOn) }}</div>
              </div>
            </a>
          } @empty {
            <p class="line muted">{{ 'dashboard.joins.empty' | t }}</p>
          }
          @if (rows.length > shown) {
            <a class="line more" routerLink="/students">{{ more(rows.length - shown) }}</a>
          }
        }
      </app-widget-state>
    </app-widget-card>
  `,
  styles: `
    a.link { color: inherit; text-decoration: none; }
    a.link:hover { background: var(--mat-sys-surface-container); }
    .line { display: block; margin: 0; padding: 0.875rem 1rem; font: var(--mat-sys-body-medium); }
    .more { border-top: 1px solid var(--mat-sys-outline-variant); color: var(--mat-sys-primary); text-decoration: none; }
  `,
})
export class JoinsWidget {
  private readonly api = inject(DashboardService);
  private readonly i18n = inject(TranslateService);
  protected readonly state = new Loadable<StudentRow[]>();
  protected readonly shown = SHOWN;
  protected readonly date = formatDate;

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.load(this.api.pendingJoins());
  }

  protected more(count: number): string {
    return this.i18n.format('dashboard.more', { count });
  }
}
