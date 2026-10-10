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
