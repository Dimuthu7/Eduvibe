import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The GuruBook mark: a teacher's tick inside a ring of ink, drawn with the theme colours so it follows light and dark.
 * Decorative: the name next to it carries the meaning.
 */
@Component({
  selector: 'app-brand-mark',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 32 32" aria-hidden="true" focusable="false">
      <circle cx="16" cy="16" r="15" fill="var(--mat-sys-primary)" />
      <path d="M8.5 16.8 13.6 22 23.8 10.2" fill="none" stroke="var(--mat-sys-on-primary)" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round" />
    </svg>
  `,
  styles: `
    :host { display: inline-flex; flex: none; }
    svg { display: block; }
  `,
})
export class BrandMark {
  readonly size = input(28);
}
