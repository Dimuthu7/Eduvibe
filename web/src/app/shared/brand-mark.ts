import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The GuruBook mark: an open book with the teacher's tick across it, drawn with the theme colours so it follows light and dark.
 * The installed app icon is the same drawing on a fixed indigo tile. Decorative: the name next to it carries the meaning.
 */
@Component({
  selector: 'app-brand-mark',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 512 512" aria-hidden="true" focusable="false">
      <rect width="512" height="512" rx="112" fill="var(--mat-sys-primary)" />
      <path d="M92 168 Q176 136 248 184 L248 392 Q176 348 92 372 Z" fill="var(--mat-sys-on-primary)" />
      <path d="M264 184 Q336 136 420 168 L420 372 Q336 348 264 392 Z" fill="var(--mat-sys-on-primary)" opacity="0.85" />
      <path d="M160 272 L226 336 L364 198" fill="none" stroke="#ffb547" stroke-width="44" stroke-linecap="round" stroke-linejoin="round" />
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
