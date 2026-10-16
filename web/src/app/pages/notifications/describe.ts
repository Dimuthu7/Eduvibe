import { fill } from '../../core/i18n/fill';
import { AppNotification } from '../../core/api/notifications.service';
import { formatLkr, formatMonth, formatTime } from '../../shared/format';

/** What a notification looks like on the list: an icon, a heading and a few lines, already in the person's language. */
export interface NotificationView {
  icon: string;
  title: string;
  lines: string[];
}

type Translate = (key: string) => string;

const text = (value: unknown): string => (typeof value === 'string' ? value : '');
const number = (value: unknown): number => (typeof value === 'number' ? value : Number(value) || 0);

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
