import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { LoadStatus } from '../../../core/state/loadable';

/**
 * What a widget shows while its data loads or fails: a placeholder, or a short message with Try again.
 * The widget's own content goes inside and appears once the data is ready.
 */
@Component({
  selector: 'app-widget-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, TranslatePipe],
  template: `
    @switch (status()) {
      @case ('ready') {
        <ng-content />
      }
      @case ('error') {
        <div class="message">
          <span class="muted">{{ 'error.load_failed' | t }}</span>
          <button mat-stroked-button type="button" (click)="retry.emit()">{{ 'status.retry' | t }}</button>
        </div>
      }
      @default {
        <div class="skeleton"></div>
      }
    }
  `,
  styles: `
    .message { display: flex; flex-direction: column; align-items: flex-start; gap: 0.5rem; padding: 1rem; }
    .skeleton { height: 3.5rem; margin: 0.75rem 1rem; }
  `,
})
export class WidgetState {
  readonly status = input.required<LoadStatus>();
  readonly retry = output();
}
