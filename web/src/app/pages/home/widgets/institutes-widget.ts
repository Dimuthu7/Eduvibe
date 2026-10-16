import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DashboardService } from '../../../core/api/dashboard.service';
import { Institute } from '../../../core/api/admin.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Loadable } from '../../../core/state/loadable';
import { WidgetCard } from './widget-card';
import { WidgetEmpty } from './widget-empty';
import { WidgetState } from './widget-state';

/** The institutes the teacher is linked to. Off by default: most teachers see it as clutter. */
@Component({
  selector: 'app-institutes-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, WidgetCard, WidgetEmpty, WidgetState],
  template: `
    <app-widget-card icon="apartment" title="dashboard.institutes.title" tone="secondary">
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
          <app-widget-empty icon="apartment">{{ 'home.no_institutes' | t }}</app-widget-empty>
        }
      </app-widget-state>
    </app-widget-card>
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
