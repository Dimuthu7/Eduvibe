import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, signal, untracked } from '@angular/core';
import { StudentPhotos } from './student-photos';

export type AvatarSize = 'sm' | 'md' | 'lg';

/** The first letter of the first and last word of a name, as whole characters (a Sinhala or Tamil letter is more than one code unit). */
export function initialsOf(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '';
  const first = (word: string) => {
    if (typeof Intl !== 'undefined' && 'Segmenter' in Intl) {
      const segment = new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(word)[Symbol.iterator]().next();
      return segment.done ? '' : segment.value.segment;
    }
    return Array.from(word)[0] ?? '';
  };
  return words.length === 1 ? first(words[0]) : first(words[0]) + first(words[words.length - 1]);
}

/** One of three colour pairs, always the same for the same name, so a student keeps their colour from screen to screen. */
export function toneOf(name: string): 0 | 1 | 2 {
  let sum = 0;
  for (const ch of name) sum = (sum * 31 + ch.codePointAt(0)!) % 9973;
  return (sum % 3) as 0 | 1 | 2;
}

/**
 * A student's round picture: their photo when they have one, their initials in a soft colour when they do not, so a list never
 * has a gap. Give it the student id or an enrolment id; the photo is fetched only once the avatar scrolls into view. An optional
 * badge, such as the index number, sits on the corner. It is decoration next to the name, so screen readers skip it.
 */
@Component({
  selector: 'app-student-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': '"size-" + size() + " tone-" + tone()', 'aria-hidden': 'true' },
  template: `
    <span class="face">
      @if (shown(); as src) {
        <img [src]="src" alt="" draggable="false" decoding="async" (error)="broken.set(src)" />
      } @else {
        <span class="initials">{{ initials() }}</span>
      }
    </span>
    @if (badge() !== null && badge() !== '' && size() !== 'sm') {
      <span class="badge">{{ badge() }}</span>
    }
  `,
  styles: `
    :host { --size: 3rem; position: relative; display: inline-block; flex: none; width: var(--size); height: var(--size); line-height: 1; }
    :host(.size-sm) { --size: 2rem; }
    :host(.size-lg) { --size: 5.5rem; }
    :host(.tone-0) { --bg: var(--mat-sys-primary-container); --fg: var(--mat-sys-on-primary-container); }
    :host(.tone-1) { --bg: var(--mat-sys-secondary-container); --fg: var(--mat-sys-on-secondary-container); }
    :host(.tone-2) { --bg: var(--mat-sys-tertiary-container); --fg: var(--mat-sys-on-tertiary-container); }
    .face { display: grid; place-items: center; width: 100%; height: 100%; overflow: hidden; border-radius: 50%; background: var(--bg); color: var(--fg); user-select: none; }
    .initials { font: var(--mat-sys-title-small); font-weight: 600; text-transform: uppercase; }
    :host(.size-sm) .initials { font: var(--mat-sys-label-medium); font-weight: 600; }
    :host(.size-lg) .initials { font: var(--mat-sys-headline-medium); font-weight: 600; }
    img { display: block; width: 100%; height: 100%; object-fit: cover; animation: fade-in 0.2s ease-out; }
    .badge { position: absolute; right: -0.25rem; bottom: -0.125rem; display: grid; place-items: center; box-sizing: border-box; min-width: 1.25rem; height: 1.25rem; padding: 0 0.25rem; border-radius: 999px; background: var(--mat-sys-surface-container-highest); color: var(--mat-sys-on-surface); box-shadow: 0 0 0 2px var(--mat-sys-surface-container-low); font: var(--mat-sys-label-small); font-weight: 600; }
    @keyframes fade-in { from { opacity: 0; } }
    @media (prefers-reduced-motion: reduce) { img { animation: none; } }
  `,
})
export class StudentAvatar {
  /** The student's full name, for the initials and the colour. */
  readonly name = input.required<string>();
  /** The student id, or an enrolment id: whichever the row has. */
  readonly of = input<string | null | undefined>(null);
  /** A picture to show instead of looking one up, for the signed-in student's own screen. */
  readonly url = input<string | null>(null);
  readonly size = input<AvatarSize>('md');
  readonly badge = input<string | number | null>(null);

  private readonly photos = inject(StudentPhotos);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly visible = signal(typeof IntersectionObserver === 'undefined');
  protected readonly broken = signal<string | null>(null);

  protected readonly initials = computed(() => initialsOf(this.name()));
  protected readonly tone = computed(() => toneOf(this.name()));
  protected readonly shown = computed(() => {
    const src = this.url() ?? (this.visible() ? this.photos.urlOf(this.of()) : null);
    return src && src !== this.broken() ? src : null;
  });

  constructor() {
    let watcher: IntersectionObserver | undefined;
    inject(DestroyRef).onDestroy(() => watcher?.disconnect());
    afterNextRender(() => {
      if (this.visible() || typeof IntersectionObserver === 'undefined') return;
      watcher = new IntersectionObserver(
        (entries) => {
          if (entries.some((e) => e.isIntersecting)) {
            this.visible.set(true);
            watcher?.disconnect();
          }
        },
        { rootMargin: '200px' },
      );
      watcher.observe(this.host.nativeElement);
    });

    effect(() => {
      const key = this.of();
      if (!key || this.url() || !this.visible()) return;
      untracked(() => this.photos.ensureIndex());
      const entry = this.photos.entryOf(key);
      untracked(() => this.photos.want(entry));
    });
  }
}
