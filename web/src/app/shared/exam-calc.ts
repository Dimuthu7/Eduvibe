import { ExamPart, GradeBand } from '../core/api/exams.service';

/** The O/L default bands, offered by "Reset" on the grading screen. The server holds the same defaults. */
export const OL_DEFAULT_BANDS: GradeBand[] = [
  { grade: 'A', minPercent: 75 },
  { grade: 'B', minPercent: 65 },
  { grade: 'C', minPercent: 50 },
  { grade: 'S', minPercent: 35 },
  { grade: 'W', minPercent: 0 },
];

const round2 = (value: number): number => Math.round((value + Number.EPSILON) * 100) / 100;

/**
 * Reads what a teacher typed in a mark box. Empty means not entered (null); a comma works as the decimal point.
 * Anything else that is not a number from 0 to the part's maximum with at most two decimals is 'invalid'.
 */
export function parseMark(text: string, max: number): number | null | 'invalid' {
  const clean = text.trim().replace(',', '.');
  if (clean === '') return null;
  if (!/^\d+(\.\d{1,2})?$/.test(clean)) return 'invalid';
  const value = Number(clean);
  return value <= max ? value : 'invalid';
}

/** The grade of the highest band the percentage reaches. */
export function gradeFor(percent: number, bands: GradeBand[]): string | null {
  const sorted = [...bands].sort((a, b) => b.minPercent - a.minPercent);
  return sorted.find((b) => percent >= b.minPercent)?.grade ?? null;
}

export interface RowPreview {
  /** The marks typed so far added together. */
  total: number;
  /** Set only when every part has a mark. */
  percent: number | null;
  grade: string | null;
  /** True when a box holds something that is not a valid mark. */
  invalid: boolean;
  /** Every part has a mark. */
  complete: boolean;
}

/** The same sums the server does, so the teacher sees a total and grade as they type. Ranks are only known after saving. */
export function previewRow(parts: ExamPart[], typed: Record<string, string>, bands: GradeBand[]): RowPreview {
  let total = 0;
  let entered = 0;
  let invalid = false;
  for (const part of parts) {
    const mark = parseMark(typed[part.id] ?? '', part.maxMark);
    if (mark === 'invalid') {
      invalid = true;
    } else if (mark !== null) {
      total += mark;
      entered++;
    }
  }

  total = round2(total);
  const complete = !invalid && entered === parts.length;
  const max = parts.reduce((sum, p) => sum + p.maxMark, 0);
  const percent = complete && max > 0 ? round2((100 * total) / max) : null;
  return { total, percent, grade: percent === null ? null : gradeFor(percent, bands), invalid, complete };
}
