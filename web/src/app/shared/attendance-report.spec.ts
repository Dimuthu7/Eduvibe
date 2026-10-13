import { rowsPerPage } from './attendance-report';

describe('rowsPerPage', () => {
  it('always gives a first page, even when nothing was marked', () => {
    expect(rowsPerPage(0)).toEqual([0]);
  });

  it('keeps a short list on one page', () => {
    expect(rowsPerPage(12)).toEqual([12]);
  });

  it('spreads a long list over pages without losing or repeating a row', () => {
    const pages = rowsPerPage(100);
    expect(pages.length).toBeGreaterThan(2);
    expect(pages.reduce((sum, n) => sum + n, 0)).toBe(100);
    expect(pages.every((n) => n > 0)).toBe(true);
    expect(pages[1]).toBeGreaterThan(pages[0]);
  });
});
