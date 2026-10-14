import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

/** One exam on the chart. A null percent (absent, or marks not finished) leaves a gap in that line. */
export interface TrendPoint {
  label: string;
  /** Shown under the label in the tooltip, such as the date. */
  detail: string;
  value: number | null;
  average: number | null;
}

const W = 340;
const H = 190;
const PAD = { top: 12, right: 16, bottom: 14, left: 34 };
const GRID = [0, 25, 50, 75, 100];

/** The points of one line as SVG path commands, starting a new piece after each gap. */
export function linePath(xs: number[], ys: (number | null)[]): string {
  let path = '';
  let drawing = false;
  ys.forEach((y, i) => {
    if (y === null) {
      drawing = false;
      return;
    }

    path += `${drawing ? 'L' : 'M'}${xs[i].toFixed(1)} ${y.toFixed(1)} `;
    drawing = true;
  });
  return path.trim();
}

/**
 * A student's percentage in each exam, oldest on the left, against the class average. Percentages run 0 to 100 on one
 * axis. Hovering or tapping an exam shows its values; the list beside the chart holds the same numbers as text.
 */
@Component({
  selector: 'app-trend-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="legend">
      <span class="key"><span class="swatch own" aria-hidden="true"></span>{{ ownLabel() }}</span>
      <span class="key"><span class="swatch avg" aria-hidden="true"></span>{{ averageLabel() }}</span>
    </div>
    <div class="frame" (pointerleave)="$event.pointerType === 'mouse' && active.set(null)">
      <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" role="img" [attr.aria-label]="ariaLabel()">
        @for (g of grid; track g) {
          <line class="grid" [attr.x1]="left" [attr.x2]="width - right" [attr.y1]="y(g)" [attr.y2]="y(g)" />
          <text class="tick" [attr.x]="left - 6" [attr.y]="y(g) + 4" text-anchor="end">{{ g }}%</text>
        }
        @if (active() !== null) {
          <line class="cross" [attr.x1]="xs()[active()!]" [attr.x2]="xs()[active()!]" [attr.y1]="y(100)" [attr.y2]="y(0)" />
        }
        <path class="line avg" [attr.d]="avgPath()" />
        <path class="line own" [attr.d]="ownPath()" />
        @for (p of points(); track $index; let i = $index) {
          @if (p.average !== null) {
            <circle class="dot avg" [attr.cx]="xs()[i]" [attr.cy]="y(p.average)" r="3" />
          }
          @if (p.value !== null) {
            <circle class="dot own" [class.on]="active() === i" [attr.cx]="xs()[i]" [attr.cy]="y(p.value)" r="4" />
          }
        }
        @if (lastIndex() !== null) {
          <text class="end" [attr.x]="xs()[lastIndex()!]" [attr.y]="y(points()[lastIndex()!].value!) - 9" text-anchor="middle">{{ points()[lastIndex()!].value }}%</text>
        }
        @for (p of points(); track $index; let i = $index) {
          <rect
            class="hit"
            [attr.x]="xs()[i] - step() / 2"
            [attr.y]="0"
            [attr.width]="step()"
            [attr.height]="height"
            (pointerenter)="hover($event, i)"
            (pointerdown)="active.set(i)"
          />
        }
      </svg>
      @if (active() !== null) {
        @let p = points()[active()!];
        <div class="tip" role="status" [style.left.%]="(100 * xs()[active()!]) / width" [class.flip]="xs()[active()!] > width * 0.6">
          <strong>{{ p.label }}</strong>
          <span class="muted">{{ p.detail }}</span>
          <span><span class="swatch own" aria-hidden="true"></span>{{ ownLabel() }}: {{ p.value === null ? '–' : p.value + '%' }}</span>
          <span><span class="swatch avg" aria-hidden="true"></span>{{ averageLabel() }}: {{ p.average === null ? '–' : p.average + '%' }}</span>
        </div>
      }
    </div>
  `,
  styles: `
    /* The drawing scales with its width, so it stops growing at a comfortable size on wide screens. */
    :host { display: block; max-width: 36rem; margin-inline: auto; }
    .legend { display: flex; flex-wrap: wrap; gap: 0.25rem 1rem; margin-bottom: 0.25rem; font: var(--mat-sys-label-medium); color: var(--mat-sys-on-surface-variant); }
    .key { display: inline-flex; gap: 0.375rem; align-items: center; }
    .swatch { display: inline-block; width: 1rem; height: 0; border-top: 2px solid var(--mat-sys-primary); vertical-align: middle; margin-right: 0.25rem; }
    .swatch.avg { border-top-style: dashed; border-top-color: var(--mat-sys-outline); }
    .frame { position: relative; }
    svg { display: block; width: 100%; height: auto; overflow: visible; touch-action: manipulation; }
    .grid { stroke: var(--mat-sys-outline-variant); stroke-width: 1; }
    .tick { fill: var(--mat-sys-on-surface-variant); font: 10px var(--mat-sys-body-small-font, sans-serif); }
    .cross { stroke: var(--mat-sys-outline); stroke-width: 1; }
    .line { fill: none; stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; }
    .line.own { stroke: var(--mat-sys-primary); }
    .line.avg { stroke: var(--mat-sys-outline); stroke-dasharray: 4 4; }
    .dot { stroke: var(--mat-sys-surface); stroke-width: 2; }
    .dot.own { fill: var(--mat-sys-primary); }
    .dot.own.on { r: 6; }
    .dot.avg { fill: var(--mat-sys-outline); }
    .end { fill: var(--mat-sys-on-surface); font: 600 11px var(--mat-sys-body-small-font, sans-serif); }
    .hit { fill: transparent; cursor: pointer; }
    .tip { position: absolute; top: 0; transform: translateX(-10%); display: grid; gap: 0.125rem; min-width: 9rem; max-width: 14rem; padding: 0.5rem 0.75rem; border-radius: var(--mat-sys-corner-small); background: var(--mat-sys-inverse-surface); color: var(--mat-sys-inverse-on-surface); font: var(--mat-sys-body-small); pointer-events: none; box-shadow: var(--mat-sys-level2); }
    .tip.flip { transform: translateX(-90%); }
    .tip .muted { color: inherit; opacity: 0.8; }
    .tip .swatch.own { border-top-color: var(--mat-sys-inverse-primary); }
  `,
})
export class TrendChart {
  readonly points = input.required<TrendPoint[]>();
  readonly ownLabel = input.required<string>();
  readonly averageLabel = input.required<string>();

  protected readonly width = W;
  protected readonly height = H;
  protected readonly left = PAD.left;
  protected readonly right = PAD.right;
  protected readonly grid = GRID;
  protected readonly active = signal<number | null>(null);

  protected readonly step = computed(() => {
    const n = this.points().length;
    return n <= 1 ? W - PAD.left - PAD.right : (W - PAD.left - PAD.right) / (n - 1);
  });

  /** One point sits in the middle; more spread from edge to edge. */
  protected readonly xs = computed(() => {
    const n = this.points().length;
    return this.points().map((_, i) => (n <= 1 ? (PAD.left + W - PAD.right) / 2 : PAD.left + i * this.step()));
  });

  protected readonly ownPath = computed(() => linePath(this.xs(), this.points().map((p) => (p.value === null ? null : this.y(p.value)))));
  protected readonly avgPath = computed(() => linePath(this.xs(), this.points().map((p) => (p.average === null ? null : this.y(p.average)))));

  /** The latest exam with a percentage, labelled with its value. */
  protected readonly lastIndex = computed(() => {
    const i = this.points().map((p) => p.value !== null).lastIndexOf(true);
    return i < 0 ? null : i;
  });

  protected readonly ariaLabel = computed(() =>
    this.points().map((p) => `${p.label}: ${p.value === null ? '–' : p.value + '%'}`).join(', '),
  );

  /** A mouse shows an exam's values on hover; a finger shows them on tap (pointerdown), so a stray hover after a tap cannot move them. */
  protected hover(event: PointerEvent, index: number): void {
    if (event.pointerType === 'mouse') this.active.set(index);
  }

  protected y(percent: number): number {
    return PAD.top + ((100 - Math.max(0, Math.min(100, percent))) / 100) * (H - PAD.top - PAD.bottom);
  }
}
