import { AppNotification } from '../../core/api/notifications.service';
import { fill } from '../../core/i18n/fill';
import { describe as view } from './describe';

const t = (key: string) =>
  ({
    'notifications.join_request.one': '{student} asked to join {class}',
    'notifications.join_request.many': '{count} students asked to join {class}',
    'notifications.join_request.hint': 'hint',
    'notifications.results_published.title': 'Results published: {exam}',
    'notifications.results_published.hint': 'See marks',
    'notifications.fee_due.title': '{month} fee is due',
    'notifications.fee_due.body': '{class} · {amount} to pay',
    'notifications.fee_before.title': '{period} fee starts tomorrow',
    'notifications.fee_before.body': '{class} · {amount} to pay by {date}',
    'notifications.fee_due_today.title': '{amount} fee is due today',
    'notifications.fee_due_today.body': '{class} · {period}',
    'notifications.fee_overdue.title': '{amount} fee is overdue',
    'notifications.fee_overdue.body': '{class} · {period}',
    'fees.days_late': '{count} days late (due {date})',
    'fees.days_late_one': '1 day late (due {date})',
    'notifications.payment_received.title': 'Payment received',
    'notifications.payment_received.body': '{amount} · Receipt {receipt} · {class}',
    'notifications.extra_class.title': 'Extra class today',
    'notifications.extra_class.body': '{class} · {start} – {end}',
    'notifications.fees_unpaid.title': 'Unpaid fees for {month}',
    'notifications.fees_unpaid.line': '{class}: {count} not paid yet',
    'notifications.unknown': 'New update',
  })[key] ?? key;

const note = (kind: string, params: Record<string, unknown>, count = 1): AppNotification => ({
  id: 'n1', kind, params, link: null, count, isRead: false, at: '2026-10-15T10:00:00Z',
});

describe('fill', () => {
  it('puts values in and leaves unknown placeholders alone', () => {
    expect(fill('{a} and {b} and {c}', { a: 'x', b: 2 })).toBe('x and 2 and {c}');
  });
});

describe('describe', () => {
  it('says who asked to join, or how many when they were grouped', () => {
    expect(view(note('join_request', { student: 'Dilani Silva', classTitle: 'Physics' }), t).title).toBe('Dilani Silva asked to join Physics');
    expect(view(note('join_request', { student: 'Dilani Silva', classTitle: 'Physics' }, 3), t).title).toBe('3 students asked to join Physics');
  });

  it('names the exam and the class for published results', () => {
    const v = view(note('results_published', { exam: 'Term 1', classTitle: 'Maths' }), t);
    expect(v.title).toBe('Results published: Term 1');
    expect(v.lines).toEqual(['Maths', 'See marks']);
  });

  it('writes money in rupees and months in words', () => {
    const plain = (lines: string[]) => lines.map((l) => l.replace(/\u00a0/g, ' '));
    const due = view(note('fee_due', { month: '2026-10', classTitle: 'Maths', amount: 2500 }), t);
    expect(due.title).toBe('October 2026 fee is due');
    expect(plain(due.lines)).toEqual(['Maths · LKR 2,500 to pay']);
    expect(plain(view(note('payment_received', { amount: 1000, receiptNo: 'R-000007', classTitle: 'Maths' }), t).lines)).toEqual(['LKR 1,000 · Receipt R-000007 · Maths']);
  });

  it('says when a reminder fee starts, is due, or is late', () => {
    const plain = (text: string) => text.replace(/\u00a0/g, ' ');
    const before = view(note('fee_before', { month: '2026-12', lastMonth: '2026-12', classTitle: 'Maths', amount: 2500, dueDate: '2026-12-10' }), t);
    expect(plain(before.title)).toBe('December 2026 fee starts tomorrow');
    expect(plain(before.lines[0])).toBe('Maths · LKR 2,500 to pay by 10 Dec');
    const today = view(note('fee_due_today', { month: '2026-11', lastMonth: '2026-11', classTitle: 'Maths', amount: 2500, dueDate: '2026-11-10', daysLate: 0 }), t);
    expect(plain(today.title)).toBe('LKR 2,500 fee is due today');
    const late = view(note('fee_overdue', { month: '2026-10', lastMonth: '2026-12', classTitle: 'Maths', amount: 7500, dueDate: '2026-10-10', daysLate: 14 }), t);
    expect(late.lines.map(plain)).toEqual(['Maths · Oct 2026 – Dec 2026', '14 days late (due 10 Oct)']);
    expect(view(note('fee_overdue', { month: '2026-11', classTitle: 'Maths', amount: 1, dueDate: '2026-11-10', daysLate: 1 }), t).lines[1]).toBe('1 day late (due 10 Nov)');
  });

  it('shows the times of an extra class and its note', () => {
    expect(view(note('extra_class', { classTitle: 'Biology', start: '16:00', end: '18:30', note: 'Revision' }), t).lines).toEqual([
      'Biology · 4:00 PM – 6:30 PM',
      'Revision',
    ]);
  });

  it('lists each class with unpaid students', () => {
    const v = view(note('fees_unpaid', { month: '2026-10', classes: [{ title: 'Maths', count: 4 }, { title: 'Art', count: 1 }] }), t);
    expect(v.title).toBe('Unpaid fees for October 2026');
    expect(v.lines).toEqual(['Maths: 4 not paid yet', 'Art: 1 not paid yet']);
  });

  it('shows a plain line for a kind this version does not know', () => {
    expect(view(note('from_the_future', {}), t)).toMatchObject({ title: 'New update', lines: [] });
  });
});
