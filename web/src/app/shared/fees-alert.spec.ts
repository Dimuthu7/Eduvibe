import { TestBed } from '@angular/core/testing';
import { MyFees } from '../core/api/fees.service';
import { AuthService } from '../core/auth/auth.service';
import { TranslateService } from '../core/i18n/translate.service';
import { FeeAlertItem, feeAlerts, FeesAlert } from './fees-alert';
import { todayColombo } from './format';

const month = (m: string, over: Partial<MyFees['months'][number]> = {}): MyFees['months'][number] => ({
  month: m, due: 2000, paid: 0, status: 'Unpaid', dueDate: `${m}-10`, periodMonths: 1, lastMonth: m, timing: '', daysLate: 0, ...over,
});
const fees = (classId: string, months: MyFees['months']): MyFees => ({ classId, outstanding: 0, months, receipts: [] });
const title = (id: string) => `Class ${id}`;

describe('feeAlerts', () => {
  it('lists only fees that are late, due today or due soon', () => {
    const result = feeAlerts([fees('a', [month('2026-11')]), fees('b', [month('2026-11', { timing: 'DueSoon' })])], title);
    expect(result.map((i) => i.classId)).toEqual(['b']);
  });

  it('puts late fees first, then today, then soon', () => {
    const result = feeAlerts(
      [
        fees('soon', [month('2026-11', { timing: 'DueSoon' })]),
        fees('late', [month('2026-10', { timing: 'Overdue', daysLate: 4 })]),
        fees('today', [month('2026-11', { timing: 'DueToday' })]),
      ],
      title,
    );
    expect(result.map((i) => i.classId)).toEqual(['late', 'today', 'soon']);
  });

  it('shows one line per class: the most urgent fee, with what is still owed on it', () => {
    const result = feeAlerts(
      [fees('a', [month('2026-11', { timing: 'DueSoon' }), month('2026-10', { timing: 'Overdue', daysLate: 9, due: 2000, paid: 500 })])],
      title,
    );
    expect(result).toHaveLength(1);
    expect(result[0]).toMatchObject({ timing: 'Overdue', daysLate: 9, amount: 1500, classTitle: 'Class a' });
  });

  it('ignores a fee that has been paid in full', () => {
    expect(feeAlerts([fees('a', [month('2026-10', { timing: 'Overdue', paid: 2000, status: 'Paid' })])], title)).toEqual([]);
  });
});

describe('FeesAlert', () => {
  const items: FeeAlertItem[] = [
    { classId: 'a', classTitle: 'Grade 10 Maths', amount: 2000, dueDate: '2026-11-10', timing: 'Overdue', daysLate: 3 },
  ];

  const mount = (list: FeeAlertItem[]) => {
    TestBed.configureTestingModule({ imports: [FeesAlert] });
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    TestBed.inject(AuthService).user.set({ id: 'u1' } as never);
    const fixture = TestBed.createComponent(FeesAlert);
    fixture.componentRef.setInput('items', list);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(() => localStorage.clear());

  it('shows the class, the amount and how late it is', () => {
    const text = (mount(items).nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Grade 10 Maths');
    expect(text).toContain('2,000');
    expect(text).toContain('alert.fees_late');
    expect(text).toContain('fees.days_late');
  });

  it('says "soon" when nothing is late yet', () => {
    const text = (mount([{ ...items[0], timing: 'DueSoon', daysLate: 0 }]).nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('alert.fees_soon');
  });

  it('shows nothing when no fee needs attention', () => {
    expect((mount([]).nativeElement as HTMLElement).querySelector('section')).toBeNull();
  });

  it('can be hidden for the rest of today, and comes back tomorrow', () => {
    const first = mount(items);
    (first.nativeElement as HTMLElement).querySelector('button')!.click();
    first.detectChanges();
    expect((first.nativeElement as HTMLElement).querySelector('section')).toBeNull();
    expect(localStorage.getItem('gurubook.fees-alert.u1')).toBe(todayColombo());

    // Opening the app again the same day keeps it hidden.
    TestBed.resetTestingModule();
    expect((mount(items).nativeElement as HTMLElement).querySelector('section')).toBeNull();

    // A hide from yesterday no longer counts.
    TestBed.resetTestingModule();
    localStorage.setItem('gurubook.fees-alert.u1', '2000-01-01');
    expect((mount(items).nativeElement as HTMLElement).querySelector('section')).not.toBeNull();
  });
});
