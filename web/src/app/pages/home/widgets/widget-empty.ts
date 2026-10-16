import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/** What a widget says when it has nothing to show: a soft icon and one short line, so an empty widget reads as calm and not as broken. */
@Component({
  selector: 'app-widget-empty',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule],
  template: `
    <span class="badge" [class.good]="good()"><mat-icon aria-hidden="true">{{ icon() }}</mat-icon></span>
    <p><ng-content /></p>
  `,
  styles: `
    :host { display: flex; align-items: center; gap: 0.875rem; padding: 1rem; color: var(--mat-sys-on-surface-variant); }
    .badge { display: grid; flex: none; place-items: center; width: 2.5rem; height: 2.5rem; border-radius: 50%; background: var(--mat-sys-surface-container-highest); }
    .badge.good { background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    mat-icon { font-size: 1.5rem; }
    p { margin: 0; font: var(--mat-sys-body-medium); }
  `,
})
export class WidgetEmpty {
  readonly icon = input('inbox');
  /** True when empty is good news (nobody is waiting, everyone has paid): the icon is tinted. */
  readonly good = input(false);
}
