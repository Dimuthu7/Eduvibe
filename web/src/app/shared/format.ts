const lkr = new Intl.NumberFormat('en-LK', { style: 'currency', currency: 'LKR', maximumFractionDigits: 0 });

/** Money in rupees, for example "LKR 2,500". */
export const formatLkr = (amount: number): string => lkr.format(amount);

/** "16:00:00" or "16:00" as "4:00 PM". */
export function formatTime(time: string): string {
  const [h, m] = time.split(':').map(Number);
  return `${h % 12 || 12}:${String(m).padStart(2, '0')} ${h < 12 ? 'AM' : 'PM'}`;
}

/** The "HH:mm" an <input type="time"> uses. */
export const toInputTime = (time: string): string => time.slice(0, 5);

/** Whole minutes since midnight, for comparing two times. */
export const minutes = (time: string): number => {
  const [h, m] = time.split(':').map(Number);
  return h * 60 + m;
};

/** Today's date in Sri Lanka as "yyyy-MM-dd", whatever the phone's own time zone is. */
export const todayColombo = (now: Date = new Date()): string =>
  new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Colombo', year: 'numeric', month: '2-digit', day: '2-digit' }).format(now);

/** A "yyyy-MM-dd" date moved by whole days. */
export function addDays(date: string, days: number): string {
  const moved = new Date(`${date}T00:00:00Z`);
  moved.setUTCDate(moved.getUTCDate() + days);
  return moved.toISOString().slice(0, 10);
}

/** The first day of the month a "yyyy-MM-dd" date falls in. */
export const monthStart = (date: string): string => `${date.slice(0, 8)}01`;

/** "yyyy-MM-dd" as "Mon, 12 Oct 2026". */
export const formatDate = (date: string): string =>
  new Date(`${date}T00:00:00Z`).toLocaleDateString('en-GB', { timeZone: 'UTC', weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });

/** An instant as Sri Lanka time, for example "12 Oct 2026, 4:30 pm", whatever the phone's own time zone is. */
export const formatDateTime = (instant: string): string =>
  new Date(instant).toLocaleString('en-GB', { timeZone: 'Asia/Colombo', day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit', hour12: true });

/** The "yyyy-MM" fee month a "yyyy-MM-dd" date falls in. */
export const monthOf = (date: string): string => date.slice(0, 7);

/** A "yyyy-MM" fee month moved by whole months. */
export function addMonths(month: string, months: number): string {
  const [year, number] = month.split('-').map(Number);
  const index = year * 12 + (number - 1) + months;
  return `${Math.floor(index / 12)}-${String((index % 12) + 1).padStart(2, '0')}`;
}

/** "2026-10" as "October 2026". */
export const formatMonth = (month: string): string =>
  new Date(`${month}-01T00:00:00Z`).toLocaleDateString('en-GB', { timeZone: 'UTC', month: 'long', year: 'numeric' });

/** "2026-10" as "Oct 2026", for tight places. */
export const formatMonthShort = (month: string): string =>
  new Date(`${month}-01T00:00:00Z`).toLocaleDateString('en-GB', { timeZone: 'UTC', month: 'short', year: 'numeric' });

/** The last day of a "yyyy-MM" month as "yyyy-MM-dd". */
export function monthEnd(month: string): string {
  const [year, number] = month.split('-').map(Number);
  return new Date(Date.UTC(year, number, 0)).toISOString().slice(0, 10);
}

/** The dates a month covers: its first day to its last, or to today when it is the current month. */
export function monthRange(month: string, today: string): { from: string; to: string } {
  const end = monthEnd(month);
  return { from: `${month}-01`, to: end > today ? today : end };
}

/** True when the dates are exactly one month's range, so the screen can show it as a month rather than as two dates. */
export function isMonthRange(from: string, to: string, today: string): boolean {
  const range = monthRange(monthOf(from), today);
  return range.from === from && range.to === to;
}
