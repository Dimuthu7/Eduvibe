import { formatLkr, formatTime, minutes, toInputTime } from './format';
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
});
