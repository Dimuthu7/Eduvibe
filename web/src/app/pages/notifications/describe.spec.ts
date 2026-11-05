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
    'notifications.child_payment.title': 'Payment received for {student}',
    'notifications.payment_voided.title': 'Payment taken back',
    'notifications.child_payment_voided.title': 'Payment taken back for {student}',
    'notifications.child_attended.title': '{student} arrived',
    'notifications.child_attended.late': '{student} arrived late',
    'notifications.child_attended.body': '{class} · {date}',
    'notifications.child_attended.scanned': 'Card scanned at {time}',
    'notifications.child_absent.title': '{student} was not in class',
    'notifications.result_changed.title': 'A mark was corrected: {exam}',
    'notifications.about_child': '{student} · {class}',
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

  it('names the child in what a parent is told, with the scan time when there is one', () => {
    const plain = (text: string) => text.replace(/\u00a0/g, ' ');
    const arrived = view(note('child_attended', { student: 'Amara Silva', classTitle: 'Maths', date: '2026-11-04', at: '15:58', late: false }), t);
    expect(arrived.title).toBe('Amara Silva arrived');
    expect(arrived.lines).toEqual(['Maths · 4 Nov', 'Card scanned at 3:58 PM']);

    const late = view(note('child_attended', { student: 'Amara Silva', classTitle: 'Maths', date: '2026-11-04', at: null, late: true }), t);
    expect(late.title).toBe('Amara Silva arrived late');
    expect(late.lines).toEqual(['Maths · 4 Nov']);

    expect(view(note('child_absent', { student: 'Amara Silva', classTitle: 'Maths', date: '2026-11-04' }), t).title).toBe('Amara Silva was not in class');
    expect(plain(view(note('child_payment', { student: 'Amara Silva', amount: 2500, receiptNo: 'R-8', classTitle: 'Maths' }), t).lines[0]))
      .toBe('LKR 2,500 · Receipt R-8 · Maths');
    expect(view(note('child_payment_voided', { student: 'Amara Silva', amount: 2500, receiptNo: 'R-8', classTitle: 'Maths' }), t).title)
      .toBe('Payment taken back for Amara Silva');
    expect(view(note('child_results', { student: 'Amara Silva', exam: 'Term 1', classTitle: 'Maths' }), t).lines).toEqual(['Amara Silva · Maths']);
    expect(view(note('child_result_changed', { student: 'Amara Silva', exam: 'Term 1', classTitle: 'Maths' }), t).title).toBe('A mark was corrected: Term 1');
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
