import { FeeTiming } from '../core/api/fees.service';
import { TranslateService } from '../core/i18n/translate.service';
import { formatDayMonth } from './format';

/** "Due 10 Nov", or "3 days late (due 10 Nov)" once the date has passed. */
export function feeDueText(i18n: TranslateService, fee: { dueDate: string; timing: FeeTiming; daysLate: number }): string {
  const date = formatDayMonth(fee.dueDate);
  if (fee.timing === 'Overdue') return i18n.format(fee.daysLate === 1 ? 'fees.days_late_one' : 'fees.days_late', { count: fee.daysLate, date });
  return i18n.format('fees.due_on', { date });
}
