import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';

/** The frame every dashboard widget sits in: a coloured icon tile, a title with an optional line under it, an optional count, and one link to the full screen. */
@Component({
  selector: 'app-widget-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-tone]': 'tone()' },
  imports: [RouterLink, MatIconModule, TranslatePipe],
  template: `
    <section class="card">
      <header>
        <span class="tile"><mat-icon aria-hidden="true">{{ icon() }}</mat-icon></span>
        <div class="heading">
          <h2>{{ title() | t }}</h2>
          @if (subtitle()) {
            <p class="sub">{{ subtitle() }}</p>
          }
        </div>
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
    :host { display: block; margin-bottom: 1rem; --tone: var(--mat-sys-primary); --tone-bg: var(--mat-sys-primary-container); --tone-fg: var(--mat-sys-on-primary-container); }
    :host([data-tone=secondary]) { --tone: var(--mat-sys-secondary); --tone-bg: var(--mat-sys-secondary-container); --tone-fg: var(--mat-sys-on-secondary-container); }
    :host([data-tone=tertiary]) { --tone: var(--mat-sys-tertiary); --tone-bg: var(--mat-sys-tertiary-container); --tone-fg: var(--mat-sys-on-tertiary-container); }
    :host([data-tone=error]) { --tone: var(--mat-sys-error); --tone-bg: var(--mat-sys-error-container); --tone-fg: var(--mat-sys-on-error-container); }
    .card { overflow: hidden; border: 1px solid var(--mat-sys-outline-variant); border-radius: var(--mat-sys-corner-large); background: var(--mat-sys-surface-container-low); }
    header { display: flex; align-items: center; gap: 0.75rem; padding: 0.875rem 1rem 0.75rem; }
    .tile { display: grid; flex: none; place-items: center; width: 2.5rem; height: 2.5rem; border-radius: var(--mat-sys-corner-medium); background: var(--tone-bg); color: var(--tone-fg); }
    .tile mat-icon { font-size: 1.5rem; }
    .heading { flex: 1; min-width: 0; }
    h2 { margin: 0; font: var(--mat-sys-title-medium); font-weight: 600; }
    .sub { margin: 0.0625rem 0 0; font: var(--mat-sys-body-small); color: var(--mat-sys-on-surface-variant); }
    .count { flex: none; min-width: 1.5rem; padding: 0 0.5rem; border-radius: 999px; background: var(--tone-bg); color: var(--tone-fg); font: var(--mat-sys-label-large); line-height: 1.5rem; text-align: center; }
    .more { flex: none; margin-inline-end: -0.25rem; padding: 0.5rem 0.625rem; border-radius: 999px; color: var(--mat-sys-primary); font: var(--mat-sys-label-large); text-decoration: none; }
    .more:hover { background: var(--mat-sys-surface-container-high); }
    .body { border-top: 1px solid var(--mat-sys-outline-variant); }
  `,
})
export class WidgetCard {
  readonly icon = input.required<string>();
  /** Translation key of the title. */
  readonly title = input.required<string>();
  /** A number shown beside the title, such as how many students need a look. Hidden when 0. */
  readonly count = input(0);
  /** The colour of the icon tile and count: primary, secondary, tertiary, or error for what needs a look. */
  readonly tone = input<'primary' | 'secondary' | 'tertiary' | 'error'>('primary');
  /** One quiet line under the title, already in words (the month, the window the numbers cover). */
  readonly subtitle = input('');
  /** Where the link on the right goes, and its translation key. Left out, there is no link. */
  readonly link = input<string | null>(null);
  readonly linkLabel = input('dashboard.open');
  readonly linkParams = input<Record<string, string> | null>(null);
}
