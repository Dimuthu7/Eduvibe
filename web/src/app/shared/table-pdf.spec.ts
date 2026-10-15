import { Cell, formatCell, planTable } from './table-pdf';

// Every character is 6 points wide, so widths are easy to predict.
const measure = (value: string) => value.length * 6;
const row = (...cells: Cell[]): Cell[] => cells;

describe('formatCell', () => {
  it('writes numbers with separators, dates in words and blanks as nothing', () => {
    expect(formatCell(4000)).toBe('4,000');
    expect(formatCell(56.5)).toBe('56.5');
    expect(formatCell('2026-10-12')).toBe('12 Oct 2026');
    expect(formatCell(null)).toBe('');
    expect(formatCell('Aruni Silva')).toBe('Aruni Silva');
  });
});

describe('planTable', () => {
  const feeHeaders = ['Index', 'Student', 'Status', 'Due (LKR)', 'Paid (LKR)', 'Outstanding (LKR)'];
  const feeRows = [row(1, 'Aruni Silva', 'Paid', 2000, 2000, 0), row('', 'Total', '', 2000, 2000, 0)];

  it('keeps a table that fits on a portrait page, filling the width', () => {
    const layout = planTable(measure, feeHeaders, feeRows, 2);
    expect(layout.landscape).toBe(false);
    expect(layout.bands).toHaveLength(1);
    expect(layout.columns.reduce((sum, c) => sum + c.width, 0)).toBeLessThanOrEqual(layout.width - 60);
    expect(layout.columns.reduce((sum, c) => sum + c.width, 0)).toBeGreaterThan(layout.width - 70);
    expect(layout.columns.filter((c) => c.align === 'right').map((c) => c.header)).toEqual(['Index', 'Due (LKR)', 'Paid (LKR)', 'Outstanding (LKR)']);
  });

  it('turns a wide table sideways and squeezes the text columns before splitting it', () => {
    const headers = ['Index', 'Student', 'Phone', 'Guardian', 'Guardian phone', 'School', 'Grade', 'Fee plan', 'Discount (LKR)', 'Joined'];
    const rows = [row(1, 'Aruni Silva Perera', '0771234567', 'Nimal Perera', '0771234568', 'Royal College Colombo', '11', 'Standard', 0, '2026-10-12')];
    const layout = planTable(measure, headers, rows, 1);
    expect(layout.landscape).toBe(true);
    expect(layout.bands).toHaveLength(1);
    expect(layout.columns.reduce((sum, c) => sum + c.width, 0)).toBeLessThanOrEqual(layout.width - 60);
  });

  it('turns session columns into narrow ones with the heading on its side and splits a very long register', () => {
    const dates = Array.from({ length: 40 }, (_, i) => `${i + 1} Oct`);
    const headers = ['Index', 'Student', ...dates, 'Sessions', 'Present', 'Late', 'Absent', 'Attendance %'];
    const marks = dates.map((_, i) => (i % 3 === 0 ? 'A' : 'P'));
    const rows = [row(1, 'Aruni Silva', ...marks, 40, 27, 0, 13, 67.5)];
    const layout = planTable(measure, headers, rows, 3);
    expect(layout.landscape).toBe(true);
    expect(layout.columns.filter((c) => c.narrow)).toHaveLength(40);
    expect(layout.headerHeight).toBeGreaterThan(40);
    expect(layout.bands.length).toBeGreaterThan(1);
    layout.bands.forEach((band) => {
      expect(band.slice(0, 2)).toEqual([0, 1]); // the index and the name are repeated
      expect(band.reduce((sum, c) => sum + layout.columns[c].width, 0)).toBeLessThanOrEqual(layout.width - 60);
    });
    // Every column is in exactly one group, apart from the repeated two.
    expect(new Set(layout.bands.flatMap((b) => b.slice(2))).size).toBe(headers.length - 2);
  });

  it('does not mistake a table with one or two short columns for a register', () => {
    const layout = planTable(measure, ['Index', 'Student', 'Grade', 'Rank'], [row(1, 'Aruni', 'A', 1)], 1);
    expect(layout.columns.some((c) => c.narrow)).toBe(false);
  });

  it('does not turn columns that are empty for everyone into narrow ones', () => {
    const headers = ['Index', 'Student', 'Guardian', 'Guardian phone', 'School', 'Grade'];
    const layout = planTable(measure, headers, [row(1, 'Aruni', '', '', '', ''), row(2, 'Bimal', '', '', '', '')], 1);
    expect(layout.columns.some((c) => c.narrow)).toBe(false);
  });

  it('puts the title block on the first page only and fills the pages in order', () => {
    const rows = Array.from({ length: 100 }, (_, i) => row(i + 1, `Student ${i + 1}`, 'Paid', 2000, 2000, 0));
    const layout = planTable(measure, feeHeaders, rows, 2);
    expect(layout.pages.length).toBeGreaterThan(2);
    expect(layout.pages.map((p) => p.first)).toEqual(layout.pages.map((_, i) => i === 0));
    expect(layout.pages[0].from).toBe(0);
    layout.pages.slice(1).forEach((p, i) => expect(p.from).toBe(layout.pages[i].to));
    expect(layout.pages.at(-1)?.to).toBe(100);
    expect(layout.pages[0].to - layout.pages[0].from).toBeLessThan(layout.pages[1].to - layout.pages[1].from);
  });

  it('still makes a page for a table with no rows', () => {
    const layout = planTable(measure, feeHeaders, [], 2);
    expect(layout.pages).toHaveLength(1);
    expect(layout.pages[0]).toMatchObject({ from: 0, to: 0, first: true });
  });
});
