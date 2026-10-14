import { ExamPart } from '../core/api/exams.service';
import { gradeFor, OL_DEFAULT_BANDS, parseMark, previewRow } from './exam-calc';

const parts: ExamPart[] = [
  { id: 'mcq', name: 'MCQ', maxMark: 40 },
  { id: 'essay', name: 'Essay', maxMark: 60 },
];

describe('parseMark', () => {
  it('reads whole and decimal marks, with a comma as the decimal point', () => {
    expect(parseMark('35', 40)).toBe(35);
    expect(parseMark(' 37.5 ', 40)).toBe(37.5);
    expect(parseMark('37,25', 40)).toBe(37.25);
    expect(parseMark('0', 40)).toBe(0);
  });

  it('treats an empty box as not entered, which is not the same as 0', () => {
    expect(parseMark('', 40)).toBeNull();
    expect(parseMark('   ', 40)).toBeNull();
  });

  it('rejects marks over the maximum, negatives, letters and too many decimals', () => {
    for (const text of ['41', '-1', 'abc', '1.234', '1e2', '4 0', '.5']) {
      expect(parseMark(text, 40)).toBe('invalid');
    }

    expect(parseMark('40', 40)).toBe(40);
  });
});

describe('gradeFor', () => {
  it('uses the highest band the percentage reaches', () => {
    expect(gradeFor(75, OL_DEFAULT_BANDS)).toBe('A');
    expect(gradeFor(74.99, OL_DEFAULT_BANDS)).toBe('B');
    expect(gradeFor(35, OL_DEFAULT_BANDS)).toBe('S');
    expect(gradeFor(34.99, OL_DEFAULT_BANDS)).toBe('W');
    expect(gradeFor(0, OL_DEFAULT_BANDS)).toBe('W');
  });

  it('does not depend on the order the bands are listed in', () => {
    expect(gradeFor(80, [...OL_DEFAULT_BANDS].reverse())).toBe('A');
  });
});

describe('previewRow', () => {
  it('adds the marks and gives a percentage and grade once every part is entered', () => {
    expect(previewRow(parts, { mcq: '35', essay: '50' }, OL_DEFAULT_BANDS)).toEqual({
      total: 85, percent: 85, grade: 'A', invalid: false, complete: true,
    });
  });

  it('shows the total so far but no grade while a part is missing', () => {
    expect(previewRow(parts, { mcq: '38', essay: '' }, OL_DEFAULT_BANDS)).toEqual({
      total: 38, percent: null, grade: null, invalid: false, complete: false,
    });
  });

  it('counts a real zero as entered', () => {
    const row = previewRow(parts, { mcq: '0', essay: '0' }, OL_DEFAULT_BANDS);
    expect(row.complete).toBe(true);
    expect(row.grade).toBe('W');
  });

  it('flags a box that holds a bad mark and gives no grade', () => {
    const row = previewRow(parts, { mcq: '45', essay: '10' }, OL_DEFAULT_BANDS);
    expect(row.invalid).toBe(true);
    expect(row.grade).toBeNull();
  });

  it('rounds the percentage to two decimals like the server', () => {
    const three: ExamPart[] = [{ id: 'p', name: 'P', maxMark: 3 }];
    expect(previewRow(three, { p: '2' }, OL_DEFAULT_BANDS).percent).toBe(66.67);
  });
});
