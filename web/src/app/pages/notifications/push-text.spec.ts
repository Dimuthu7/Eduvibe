import en from '../../../../public/i18n/en.json';
import si from '../../../../public/i18n/si.json';
import ta from '../../../../public/i18n/ta.json';
import '../../../../public/push-text.js';
import { AppNotification } from '../../core/api/notifications.service';
import { Language, currentLanguage } from '../../core/i18n/language';
import { describe as view } from './describe';

interface PushText {
  compose(n: unknown, messages: Record<string, string>, lang: string): { title: string; body: string };
}

const pushText = (globalThis as unknown as { GuruBookPushText: PushText }).GuruBookPushText;
const files: Record<Language, Record<string, string>> = { en, si, ta };

const note = (kind: string, params: Record<string, unknown>, count = 1): AppNotification => ({
  id: 'n1', kind, params, link: null, count, isRead: false, at: '2026-11-02T10:00:00Z',
});

/** One of each kind the server sends, so a new kind added to the app but not to the phone's text file fails here. */
const SAMPLES: AppNotification[] = [
  note('join_request', { student: 'Dilani Silva', classTitle: 'Physics' }),
  note('join_request', { student: 'Dilani Silva', classTitle: 'Physics' }, 3),
  note('results_published', { exam: 'Term 1', classTitle: 'Maths' }),
  note('fee_due', { month: '2026-10', classTitle: 'Maths', amount: 2500 }),
  note('fee_before', { month: '2026-12', lastMonth: '2026-12', classTitle: 'Maths', amount: 2500, dueDate: '2026-12-10' }),
  note('fee_before', { month: '2026-10', lastMonth: '2026-12', classTitle: 'Maths', amount: 7500, dueDate: '2026-10-10' }),
  note('fee_due_today', { month: '2026-11', lastMonth: '2026-11', classTitle: 'Maths', amount: 2500, dueDate: '2026-11-10', daysLate: 0 }),
  note('fee_overdue', { month: '2026-11', lastMonth: '2026-11', classTitle: 'Maths', amount: 2500, dueDate: '2026-11-10', daysLate: 1 }),
  note('fee_overdue', { month: '2026-10', lastMonth: '2027-03', classTitle: 'Maths', amount: 9000, dueDate: '2026-10-10', daysLate: 14 }),
  note('payment_received', { amount: 1000, receiptNo: 'R-000007', classTitle: 'Maths' }),
  note('extra_class', { classTitle: 'Biology', start: '16:00', end: '18:30', note: 'Revision' }),
  note('extra_class', { classTitle: 'Biology', start: '09:05', end: '10:00', note: '' }),
  note('fees_unpaid', { month: '2026-11', classes: [{ title: 'Maths', count: 4 }, { title: 'Physics', count: 1 }] }),
  note('something_new', {}),
];

describe('phone notification text', () => {
  afterEach(() => currentLanguage.set('en'));

  for (const language of ['en', 'si', 'ta'] as const) {
    it(`reads the same as the app's list in ${language}`, () => {
      currentLanguage.set(language);
      const messages = files[language];
      const t = (key: string) => messages[key] ?? key;
      for (const sample of SAMPLES) {
        const expected = view(sample, t);
        const actual = pushText.compose(sample, messages, language);
        expect(actual.title, `${sample.kind} title`).toBe(expected.title);
        expect(actual.body, `${sample.kind} body`).toBe(expected.lines.join('\n'));
      }
    });
  }

  it('has text for the new reminder kinds in every language', () => {
    for (const language of ['en', 'si', 'ta'] as const) {
      for (const key of ['fee_before', 'fee_due_today', 'fee_overdue']) {
        expect(files[language][`notifications.${key}.title`], `${language} ${key}`).toBeTruthy();
        expect(files[language][`notifications.${key}.body`], `${language} ${key}`).toBeTruthy();
      }
    }
  });
});
