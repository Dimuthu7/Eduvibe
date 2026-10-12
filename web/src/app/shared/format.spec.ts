import { addDays, addMonths, formatDate, formatMonth, formatLkr, formatTime, minutes, monthOf, monthStart, toInputTime, todayColombo } from './format';
import { slotSummary } from './schedule';

describe('format helpers', () => {
  it('shows times the way people say them', () => {
    expect(formatTime('16:00:00')).toBe('4:00 PM');
    expect(formatTime('09:05')).toBe('9:05 AM');
    expect(formatTime('00:30')).toBe('12:30 AM');
    expect(formatTime('12:00')).toBe('12:00 PM');
  });

  it('trims seconds for a time input and counts minutes', () => {
    expect(toInputTime('16:00:00')).toBe('16:00');
    expect(minutes('16:30')).toBe(990);
  });

  it('formats rupees without decimals', () => {
    expect(formatLkr(2500)).toMatch(/2,500/);
  });

  it('summarises weekly slots', () => {
    const text = slotSummary(
      [{ day: 1, start: '16:00:00', end: '18:00:00' }, { day: 4, start: '09:00', end: '10:00' }],
      (key) => key.replace('day.short.', 'D'),
    );
    expect(text).toBe('D1 4:00 PM – 6:00 PM · D4 9:00 AM – 10:00 AM');
  });

  it('works out the date in Sri Lanka, not in the phone time zone', () => {
    // 20:00 UTC is already the next morning (01:30) in Colombo.
    expect(todayColombo(new Date('2026-10-11T20:00:00Z'))).toBe('2026-10-12');
    expect(todayColombo(new Date('2026-10-11T10:00:00Z'))).toBe('2026-10-11');
  });

  it('moves dates by days and finds the month start', () => {
    expect(addDays('2026-10-31', 1)).toBe('2026-11-01');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
    expect(monthStart('2026-10-17')).toBe('2026-10-01');
    expect(formatDate('2026-10-12')).toBe('Mon, 12 Oct 2026');
  });

  it('moves fee months across year ends', () => {
    expect(addMonths('2026-12', 1)).toBe('2027-01');
    expect(addMonths('2026-01', -1)).toBe('2025-12');
    expect(addMonths('2026-10', 14)).toBe('2027-12');
    expect(monthOf('2026-10-17')).toBe('2026-10');
    expect(formatMonth('2026-10')).toBe('October 2026');
  });
});
