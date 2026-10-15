import { addDays, addMonths, formatDate, formatDateTime, formatMonth, formatLkr, formatTime, isMonthRange, minutes, monthEnd, monthOf, monthRange, monthStart, toInputTime, todayColombo } from './format';
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

describe('month ranges', () => {
  it('finds the last day of a month, leap years included', () => {
    expect(monthEnd('2026-10')).toBe('2026-10-31');
    expect(monthEnd('2026-02')).toBe('2026-02-28');
    expect(monthEnd('2028-02')).toBe('2028-02-29');
  });

  it('runs a past month to its end and the current month to today', () => {
    expect(monthRange('2026-09', '2026-10-13')).toEqual({ from: '2026-09-01', to: '2026-09-30' });
    expect(monthRange('2026-10', '2026-10-13')).toEqual({ from: '2026-10-01', to: '2026-10-13' });
  });

  it('knows whether two dates are exactly a month', () => {
    expect(isMonthRange('2026-10-01', '2026-10-13', '2026-10-13')).toBe(true);
    expect(isMonthRange('2026-09-01', '2026-09-30', '2026-10-13')).toBe(true);
    expect(isMonthRange('2026-09-05', '2026-09-30', '2026-10-13')).toBe(false);
    expect(isMonthRange('2026-10-01', '2026-10-10', '2026-10-13')).toBe(false);
  });
});

describe('formatDateTime', () => {
  it('shows an instant in Sri Lanka time whatever the phone uses', () => {
    // 11:00 UTC is 4:30 pm in Colombo (UTC+5:30); 20:00 UTC on the 12th is already 1:30 am on the 13th.
    expect(formatDateTime('2026-10-12T11:00:00Z').toLowerCase()).toBe('12 oct 2026, 4:30 pm');
    expect(formatDateTime('2026-10-12T20:00:00Z').toLowerCase()).toBe('13 oct 2026, 1:30 am');
  });
});
