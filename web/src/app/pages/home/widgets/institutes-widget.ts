import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DashboardService } from '../../../core/api/dashboard.service';
import { Institute } from '../../../core/api/admin.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Loadable } from '../../../core/state/loadable';
import { WidgetCard } from './widget-card';
import { WidgetState } from './widget-state';

/** The institutes the teacher is linked to. Off by default: most teachers see it as clutter. */
@Component({
  selector: 'app-institutes-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, WidgetCard, WidgetState],
  template: `
    <app-widget-card icon="apartment" title="dashboard.institutes.title">
      <app-widget-state [status]="state.status()" (retry)="load()">
        @for (institute of state.data(); track institute.id) {
          <div class="panel-row">
            <div class="row-main">
              <div class="row-title">{{ institute.name }}</div>
              @if (institute.district) {
                <div class="row-meta">{{ institute.district }}</div>
              }
            </div>
          </div>
        } @empty {
          <p class="line muted">{{ 'home.no_institutes' | t }}</p>
        }
      </app-widget-state>
    </app-widget-card>
  `,
  styles: `
    .line { margin: 0; padding: 0.875rem 1rem; font: var(--mat-sys-body-medium); }
  `,
})
export class InstitutesWidget {
  private readonly api = inject(DashboardService);
  protected readonly state = new Loadable<Institute[]>();

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.load(this.api.institutes());
  }
}
