import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { ExamDetail } from '../../core/api/exams.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/** Rounds to at most two places and drops trailing zeros, so 65.5 stays 65.5 and 70.00 shows as 70. */
export function short(value: number): string {
  return String(Math.round(value * 100) / 100);
}

/**
 * How the class did in one exam: highest, lowest, average and median marks, the pass rate, how many earned each grade
 * and the average of each part. Worked out by the server from the saved marks.
 */
@Component({
  selector: 'app-exam-analysis',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, TranslatePipe],
  template: `
    @let a = exam().analysis;
    @if (a.sat === 0) {
      <div class="empty">
        <mat-icon>insights</mat-icon>
        <p>{{ 'exams.analysis_none' | t }}</p>
      </div>
    } @else {
      <div class="pass" role="group" [attr.aria-label]="'exams.pass_rate' | t">
        <strong>{{ a.passRate }}%</strong>
        <div>
          <div>{{ 'exams.pass_rate' | t }} · {{ a.passed }} / {{ a.sat }}</div>
          <div class="muted small">
            @if (a.passMark !== null) {
              {{ 'exams.pass_from' | t }} {{ a.passMark }}% ({{ lowest() }} {{ 'exams.fails' | t }})
            } @else {
              {{ 'exams.everyone_passes' | t }}
            }
            @if (a.absent > 0) {
              · {{ 'exams.absent' | t }} {{ a.absent }}
            }
          </div>
        </div>
      </div>

      <div class="tiles">
        @for (tile of tiles(); track tile.label) {
          <div class="tile">
            <span class="label">{{ tile.label | t }}</span>
            <strong>{{ tile.value }}<span class="of"> / {{ max() }}</span></strong>
            <span class="muted small">{{ tile.percent }}%</span>
          </div>
        }
      </div>

      <h2 class="section-title">{{ 'exams.grade_spread' | t }}</h2>
      <div class="panel bars">
        @for (g of a.grades; track g.grade) {
          <div class="bar-row" [title]="g.grade + ': ' + g.count">
            <span class="name grade">{{ g.grade }}</span>
            <span class="track" aria-hidden="true"><span class="fill" [style.width.%]="share(g.count, topGrade())"></span></span>
            <span class="value">{{ g.count }}</span>
          </div>
        }
      </div>

      <h2 class="section-title">{{ 'exams.part_averages' | t }}</h2>
      <div class="panel bars">
        @for (p of a.parts; track p.partId) {
          <div class="bar-row part" [title]="p.name + ': ' + (p.average === null ? '–' : num(p.average) + ' / ' + num(p.maxMark))">
            <span class="name">{{ p.name }}</span>
            <span class="track" aria-hidden="true"><span class="fill" [style.width.%]="p.average === null ? 0 : share(p.average, p.maxMark)"></span></span>
            <span class="value">
              @if (p.average === null) {
                –
              } @else {
                {{ num(p.average) }} / {{ num(p.maxMark) }}
              }
            </span>
          </div>
        }
      </div>
    }
  `,
  styles: `
    :host { display: block; }
    .small { font: var(--mat-sys-body-small); }
    .pass { display: flex; gap: 1rem; align-items: center; margin-bottom: 0.75rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); }
    .pass strong { font: var(--mat-sys-headline-medium); color: var(--mat-sys-primary); }
    .tiles { display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem; margin-bottom: 1rem; }
    .tile { display: grid; gap: 0.125rem; padding: 0.75rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); }
    .tile .label { font: var(--mat-sys-label-medium); color: var(--mat-sys-on-surface-variant); }
    .tile strong { font: var(--mat-sys-headline-small); }
    .tile .of { font: var(--mat-sys-body-medium); color: var(--mat-sys-on-surface-variant); }
    .bars { padding: 0.5rem 0; }
    .bar-row { display: grid; grid-template-columns: 2.5rem 1fr auto; gap: 0.75rem; align-items: center; padding: 0.5rem 1rem; }
    .bar-row.part { grid-template-columns: minmax(4rem, 30%) 1fr auto; }
    .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .name.grade { font: var(--mat-sys-title-medium); }
    .track { height: 0.75rem; border-radius: 0.25rem; background: var(--mat-sys-surface-container-highest); overflow: hidden; }
    .fill { display: block; height: 100%; min-width: 0; border-radius: 0.25rem; background: var(--mat-sys-primary); }
    .value { min-width: 1.5rem; text-align: end; font: var(--mat-sys-label-large); font-variant-numeric: tabular-nums; }
  `,
})
export class ExamAnalysis {
  readonly exam = input.required<ExamDetail>();

  protected readonly num = short;
  protected readonly max = computed(() => short(this.exam().maxTotal));
  /** The grade that fails: the one starting at the lowest percentage. */
  protected readonly lowest = computed(() => [...this.exam().bands].sort((x, y) => x.minPercent - y.minPercent)[0]?.grade ?? '');
  protected readonly topGrade = computed(() => Math.max(1, ...this.exam().analysis.grades.map((g) => g.count)));
  protected readonly tiles = computed(() => {
    const { analysis: a, maxTotal } = this.exam();
    const tile = (label: string, value: number | null) => ({
      label,
      value: value === null ? '–' : short(value),
      percent: value === null || maxTotal <= 0 ? '–' : short((100 * value) / maxTotal),
    });
    return [tile('exams.highest', a.highest), tile('exams.lowest', a.lowest), tile('exams.average', a.average), tile('exams.median', a.median)];
  });

  protected share(value: number, of: number): number {
    return of <= 0 ? 0 : Math.min(100, (100 * value) / of);
  }
}
