import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';

/** The frame every dashboard widget sits in: an icon and title, an optional count, and one link to the full screen. */
@Component({
  selector: 'app-widget-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatIconModule, TranslatePipe],
  template: `
    <section class="card">
      <header>
        <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
        <h2>{{ title() | t }}</h2>
        @if (count()) {
          <span class="count">{{ count() }}</span>
        }
        @if (link(); as target) {
          <a class="more" [routerLink]="target" [queryParams]="linkParams()">{{ linkLabel() | t }}</a>
        }
      </header>
      <div class="body"><ng-content /></div>
    </section>
  `,
  styles: `
    :host { display: block; margin-bottom: 1rem; }
    .card { overflow: hidden; border: 1px solid var(--mat-sys-outline-variant); border-radius: var(--mat-sys-corner-large); background: var(--mat-sys-surface-container-low); }
    header { display: flex; align-items: center; gap: 0.625rem; padding: 0.75rem 1rem; min-height: 1.5rem; }
    header mat-icon { flex: none; font-size: 1.5rem; color: var(--mat-sys-primary); }
    h2 { flex: 1; min-width: 0; margin: 0; font: var(--mat-sys-title-medium); font-weight: 600; }
    .count { padding: 0 0.5rem; border-radius: 999px; background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); font: var(--mat-sys-label-medium); line-height: 1.375rem; }
    .more { flex: none; color: var(--mat-sys-primary); font: var(--mat-sys-label-large); text-decoration: none; padding: 0.5rem 0.25rem; }
    .body { border-top: 1px solid var(--mat-sys-outline-variant); }
  `,
})
export class WidgetCard {
  readonly icon = input.required<string>();
  /** Translation key of the title. */
  readonly title = input.required<string>();
  /** A number shown beside the title, such as how many students need a look. Hidden when 0. */
  readonly count = input(0);
  /** Where the link on the right goes, and its translation key. Left out, there is no link. */
  readonly link = input<string | null>(null);
  readonly linkLabel = input('dashboard.open');
  readonly linkParams = input<Record<string, string> | null>(null);
}
