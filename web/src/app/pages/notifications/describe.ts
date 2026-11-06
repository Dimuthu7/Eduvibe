import { fill } from '../../core/i18n/fill';
import { AppNotification } from '../../core/api/notifications.service';
import { formatDayMonth, formatLkr, formatMonth, formatPeriod, formatTime } from '../../shared/format';

/** What a notification looks like on the list: an icon, a heading and a few lines, already in the person's language. */
export interface NotificationView {
  icon: string;
  title: string;
  lines: string[];
}

type Translate = (key: string) => string;

const text = (value: unknown): string => (typeof value === 'string' ? value : '');
const number = (value: unknown): number => (typeof value === 'number' ? value : Number(value) || 0);

/** The words for one fee's months: "December 2030" for one month, "Oct 2026 – Dec 2026" for a longer fee. */
const period = (p: Record<string, unknown>): string => {
  const month = text(p['month']);
  const last = text(p['lastMonth']);
  return last > month ? formatPeriod(month, last) : formatMonth(month);
};

const daysLate = (p: Record<string, unknown>, t: Translate): string => {
  const days = number(p['daysLate']);
  return fill(t(days === 1 ? 'fees.days_late_one' : 'fees.days_late'), { count: days, date: formatDayMonth(text(p['dueDate'])) });
};

/**
 * Turns a notification into words. Each kind the server sends has its own text in the translation files, so a new kind is a new case
 * here and new keys there. A kind this version of the app does not know shows a plain "new update" instead of nothing.
 */
export function describe(n: AppNotification, t: Translate): NotificationView {
  const p = n.params;
  const classTitle = text(p['classTitle']);
  switch (n.kind) {
    case 'join_request':
      return {
        icon: 'person_add',
        title:
          n.count > 1
            ? fill(t('notifications.join_request.many'), { count: n.count, class: classTitle })
            : fill(t('notifications.join_request.one'), { student: text(p['student']), class: classTitle }),
        lines: [t('notifications.join_request.hint')],
      };
    case 'results_published':
      return {
        icon: 'quiz',
        title: fill(t('notifications.results_published.title'), { exam: text(p['exam']) }),
        lines: [classTitle, t('notifications.results_published.hint')].filter(Boolean),
      };
    case 'fee_due':
      return {
        icon: 'payments',
        title: fill(t('notifications.fee_due.title'), { month: formatMonth(text(p['month'])) }),
        lines: [fill(t('notifications.fee_due.body'), { class: classTitle, amount: formatLkr(number(p['amount'])) })],
      };
    case 'fee_before':
      return {
        icon: 'event',
        title: fill(t('notifications.fee_before.title'), { period: period(p) }),
        lines: [fill(t('notifications.fee_before.body'), { class: classTitle, amount: formatLkr(number(p['amount'])), date: formatDayMonth(text(p['dueDate'])) })],
      };
    case 'fee_due_today':
      return {
        icon: 'payments',
        title: fill(t('notifications.fee_due_today.title'), { amount: formatLkr(number(p['amount'])) }),
        lines: [fill(t('notifications.fee_due_today.body'), { class: classTitle, period: period(p) })],
      };
    case 'fee_overdue':
      return {
        icon: 'schedule',
        title: fill(t('notifications.fee_overdue.title'), { amount: formatLkr(number(p['amount'])) }),
        lines: [fill(t('notifications.fee_overdue.body'), { class: classTitle, period: period(p) }), daysLate(p, t)],
      };
    case 'child_payment':
      return {
        icon: 'receipt_long',
        title: fill(t('notifications.child_payment.title'), { student: text(p['student']) }),
        lines: [fill(t('notifications.payment_received.body'), { amount: formatLkr(number(p['amount'])), receipt: text(p['receiptNo']), class: classTitle })],
      };
    case 'payment_voided':
      return {
        icon: 'money_off',
        title: t('notifications.payment_voided.title'),
        lines: [fill(t('notifications.payment_received.body'), { amount: formatLkr(number(p['amount'])), receipt: text(p['receiptNo']), class: classTitle })],
      };
    case 'child_payment_voided':
      return {
        icon: 'money_off',
        title: fill(t('notifications.child_payment_voided.title'), { student: text(p['student']) }),
        lines: [fill(t('notifications.payment_received.body'), { amount: formatLkr(number(p['amount'])), receipt: text(p['receiptNo']), class: classTitle })],
      };
    case 'child_attended': {
      const at = text(p['at']);
      return {
        icon: 'how_to_reg',
        title: fill(t(p['late'] === true ? 'notifications.child_attended.late' : 'notifications.child_attended.title'), { student: text(p['student']) }),
        lines: [
          fill(t('notifications.child_attended.body'), { class: classTitle, date: formatDayMonth(text(p['date'])) }),
          ...(at ? [fill(t('notifications.child_attended.scanned'), { time: formatTime(at) })] : []),
        ],
      };
    }
    case 'child_absent':
      return {
        icon: 'person_off',
        title: fill(t('notifications.child_absent.title'), { student: text(p['student']) }),
        lines: [fill(t('notifications.child_attended.body'), { class: classTitle, date: formatDayMonth(text(p['date'])) })],
      };
    case 'child_low_attendance':
      return {
        icon: 'running_with_errors',
        title: fill(t('notifications.child_low_attendance.title'), { student: text(p['student']) }),
        lines: [
          fill(t('notifications.child_low_attendance.body'), { percent: String(number(p['percent'])), weeks: String(number(p['weeks'])) }),
          classTitle,
        ].filter(Boolean),
      };
    case 'child_results':
      return {
        icon: 'quiz',
        title: fill(t('notifications.results_published.title'), { exam: text(p['exam']) }),
        lines: [fill(t('notifications.about_child'), { student: text(p['student']), class: classTitle })],
      };
    case 'result_changed':
      return {
        icon: 'edit_note',
        title: fill(t('notifications.result_changed.title'), { exam: text(p['exam']) }),
        lines: [classTitle, t('notifications.results_published.hint')].filter(Boolean),
      };
    case 'child_result_changed':
      return {
        icon: 'edit_note',
        title: fill(t('notifications.result_changed.title'), { exam: text(p['exam']) }),
        lines: [fill(t('notifications.about_child'), { student: text(p['student']), class: classTitle })],
      };
    case 'payment_received':
      return {
        icon: 'receipt_long',
        title: t('notifications.payment_received.title'),
        lines: [fill(t('notifications.payment_received.body'), { amount: formatLkr(number(p['amount'])), receipt: text(p['receiptNo']), class: classTitle })],
      };
    case 'extra_class': {
      const note = text(p['note']);
      return {
        icon: 'event_available',
        title: t('notifications.extra_class.title'),
        lines: [
          fill(t('notifications.extra_class.body'), { class: classTitle, start: formatTime(text(p['start'])), end: formatTime(text(p['end'])) }),
          ...(note ? [note] : []),
        ],
      };
    }
    case 'fees_unpaid': {
      const classes = Array.isArray(p['classes']) ? (p['classes'] as Record<string, unknown>[]) : [];
      return {
        icon: 'notifications_active',
        title: fill(t('notifications.fees_unpaid.title'), { month: formatMonth(text(p['month'])) }),
        lines: classes.map((c) => fill(t('notifications.fees_unpaid.line'), { class: text(c['title']), count: number(c['count']) })),
      };
    }
    default:
      return { icon: 'notifications', title: t('notifications.unknown'), lines: [] };
  }
}
