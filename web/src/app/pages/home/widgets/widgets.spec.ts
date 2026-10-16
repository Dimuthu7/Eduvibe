import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppConfig } from '../../../core/config/app-config';
import { TranslateService } from '../../../core/i18n/translate.service';
import { AttendanceWidget } from './attendance-widget';
import { averageChange, ExamsWidget } from './exams-widget';
import { collectedPercent, FeesWidget, owingClasses } from './fees-widget';
import { JoinsWidget } from './joins-widget';
import { TodayWidget } from './today-widget';

const API = 'http://api.test';

describe('dashboard widgets', () => {
  let http: HttpTestingController;
  let mounted = false;

  const mount = <T>(component: Type<T>) => {
    TestBed.configureTestingModule({ imports: [component], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    mounted = true;
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    // Text comes back as its key, with any {values} still in it, so the specs read data rather than wording.
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(component);
    fixture.detectChanges();
    return fixture;
  };

  afterEach(() => {
    if (mounted) http.verify();
    mounted = false;
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  const text = (fixture: { nativeElement: HTMLElement }) => fixture.nativeElement.textContent?.replace(/\s+/g, ' ') ?? '';

  describe('fees', () => {
    const totals = (over: Partial<Record<string, number>>) => ({ paid: 0, partlyPaid: 0, unpaid: 0, freeCard: 0, due: 0, collected: 0, outstanding: 0, ...over });
    const cls = (title: string, outstanding: number, archived = false) => ({ classId: title, title, isArchived: archived, totals: totals({ outstanding, unpaid: 2, due: 5000, collected: 5000 - outstanding }) });

    it('lists only classes that still owe, the biggest first, and leaves archived ones out', () => {
      const list = owingClasses([cls('Small', 1000), cls('Paid', 0), cls('Big', 9000), cls('Old', 5000, true)]);
      expect(list.map((c) => c.title)).toEqual(['Big', 'Small']);
    });

    it('works out how much of the month is collected', () => {
      const overview = (due: number, collected: number) => ({ month: 'm', totals: totals({ due, collected }), classes: [] });
      expect(collectedPercent(overview(10000, 2500))).toBe(25);
      expect(collectedPercent(overview(0, 0))).toBe(0);
      expect(collectedPercent(overview(100, 500))).toBe(100);
    });

    it('shows what is left to collect and the classes that owe it', () => {
      const fixture = mount(FeesWidget);
      const req = http.expectOne((r) => r.url === `${API}/api/fees/overview`);
      expect(req.request.params.get('month')).toMatch(/^\d{4}-\d{2}$/);
      req.flush({ month: '2026-10', totals: totals({ due: 15000, collected: 3000, outstanding: 12000 }), classes: [cls('Maths', 9000), cls('Science', 3000), cls('Paid up', 0)] });
      fixture.detectChanges();
      const shown = text(fixture);
      expect(shown).toContain('LKR 12,000');
      expect(shown).toContain('Maths');
      expect(shown).toContain('Science');
      expect(shown).not.toContain('Paid up');
      expect(fixture.nativeElement.querySelector('[role=progressbar]').getAttribute('aria-valuenow')).toBe('20');
      expect(fixture.nativeElement.querySelectorAll('a.link')).toHaveLength(2);
    });

    it('says everyone has paid, and says so plainly when there is nothing to collect yet', () => {
      const paid = mount(FeesWidget);
      http.expectOne((r) => r.url === `${API}/api/fees/overview`).flush({ month: 'm', totals: totals({ due: 5000, collected: 5000 }), classes: [cls('Maths', 0)] });
      paid.detectChanges();
      expect(text(paid)).toContain('dashboard.fees.all_paid');
      expect(text(paid)).not.toContain('dashboard.fees.attended_unpaid');
    });

    it('has its own try again when it cannot load', () => {
      const fixture = mount(FeesWidget);
      http.expectOne((r) => r.url === `${API}/api/fees/overview`).flush('x', { status: 500, statusText: 'Server error' });
      fixture.detectChanges();
      expect(text(fixture)).toContain('error.load_failed');
      (fixture.nativeElement.querySelector('app-widget-state button') as HTMLButtonElement).click();
      http.expectOne((r) => r.url === `${API}/api/fees/overview`).flush({ month: 'm', totals: totals({}), classes: [] });
      fixture.detectChanges();
      expect(text(fixture)).toContain('dashboard.fees.none');
    });
  });

  describe('attendance watch', () => {
    const row = (name: string, missed: number, percent: number, sessions = 6, last: string | null = '2026-10-12') => ({
      enrolmentId: name, classId: 'c', classTitle: 'Maths', indexNumber: 1, fullName: name, missedInARow: missed, sessions, percent, lastAttended: last,
    });

    it('highlights a streak, shows a low rate only with enough classes behind it, and links to the student report', () => {
      const fixture = mount(AttendanceWidget);
      http.expectOne(`${API}/api/attendance/watch`).flush({ windowDays: 30, total: 4, rows: [row('Amara', 3, 50), row('Bandula', 0, 50), row('Chamari', 2, 100, 2, null)] });
      fixture.detectChanges();
      const rows = Array.from(fixture.nativeElement.querySelectorAll('a.link')) as HTMLAnchorElement[];
      expect(rows).toHaveLength(3);
      expect(rows[0].getAttribute('href')).toBe('/attendance/summary/student/Amara');
      expect(rows[0].querySelector('.chip.alert')?.textContent).toContain('dashboard.attendance.missed');
      expect(rows[1].querySelector('.chip.alert')).toBeNull();
      expect(rows[1].querySelectorAll('.chip')).toHaveLength(1);
      // Two classes in total is too few to call a rate low.
      expect(rows[2].querySelectorAll('.chip')).toHaveLength(1);
      expect(rows[2].textContent).toContain('dashboard.attendance.not_lately');
      expect(text(fixture)).toContain('dashboard.more');
    });

    it('is calm when nobody is missing classes', () => {
      const fixture = mount(AttendanceWidget);
      http.expectOne(`${API}/api/attendance/watch`).flush({ windowDays: 30, total: 0, rows: [] });
      fixture.detectChanges();
      expect(text(fixture)).toContain('dashboard.attendance.empty');
    });
  });

  describe('exam progress', () => {
    const card = (over: Record<string, unknown> = {}) => ({
      classId: 'c1', classTitle: 'Maths', examId: 'e1', examName: 'Term 2', heldOn: '2026-10-10', students: 20, marked: 20,
      averagePercent: 61, previousAverage: 65.5, published: true, ...over,
    });

    it('works out how the average moved', () => {
      expect(averageChange(card() as never)).toBe(-4.5);
      expect(averageChange(card({ previousAverage: null }) as never)).toBeNull();
      expect(averageChange(card({ averagePercent: null }) as never)).toBeNull();
    });

    it('shows each class, the move since last time, unfinished marking, and who dropped', () => {
      const fixture = mount(ExamsWidget);
      http.expectOne(`${API}/api/exams/progress`).flush({
        dropPoints: 10,
        classes: [card(), card({ classId: 'c2', classTitle: 'Science', examId: 'e2', marked: 12, averagePercent: 70, previousAverage: 60, published: false })],
        droppedTotal: 3,
        dropped: [{ enrolmentId: 'en1', classId: 'c1', classTitle: 'Maths', indexNumber: 4, fullName: 'Dilani', examName: 'Term 2', previous: 80, latest: 55 }],
      });
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;
      const shown = text(fixture);
      expect(shown).toContain('61%');
      expect(shown).toContain('4.5');
      expect(el.querySelector('.delta.down')).not.toBeNull();
      expect(el.querySelector('.delta.up')).not.toBeNull();
      expect(shown).toContain('dashboard.exams.marking');
      expect(shown).toContain('Dilani');
      expect(shown).toContain('80%');
      expect(shown).toContain('55%');
      expect(shown).toContain('dashboard.more');
      expect((el.querySelectorAll('a.link')[0] as HTMLAnchorElement).getAttribute('href')).toBe('/exams/e1');
      expect((el.querySelectorAll('a.link')[2] as HTMLAnchorElement).getAttribute('href')).toBe('/exams/student/en1');
    });

    it('points to the Exams page when there are no exams yet', () => {
      const fixture = mount(ExamsWidget);
      http.expectOne(`${API}/api/exams/progress`).flush({ dropPoints: 10, classes: [], droppedTotal: 0, dropped: [] });
      fixture.detectChanges();
      expect(text(fixture)).toContain('dashboard.exams.empty');
    });
  });

  describe('join requests', () => {
    const student = (n: number) => ({ enrolmentId: `e${n}`, firstName: `S${n}`, fullName: `Student ${n}`, classTitle: 'Maths', joinedOn: '2026-10-15' });

    it('asks only for pending students, shows the first few and counts the rest', () => {
      const fixture = mount(JoinsWidget);
      const req = http.expectOne((r) => r.url === `${API}/api/students`);
      expect(req.request.params.get('status')).toBe('pending');
      req.flush([1, 2, 3, 4, 5].map(student));
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelectorAll('a.link')).toHaveLength(3);
      expect(fixture.nativeElement.querySelector('app-widget-card .count').textContent.trim()).toBe('5');
      expect(text(fixture)).toContain('dashboard.more');
    });

    it('says no one is waiting', () => {
      const fixture = mount(JoinsWidget);
      http.expectOne((r) => r.url === `${API}/api/students`).flush([]);
      fixture.detectChanges();
      expect(text(fixture)).toContain('dashboard.joins.empty');
      expect(fixture.nativeElement.querySelector('app-widget-card .count')).toBeNull();
    });
  });

  describe("today's classes", () => {
    const session = (id: string, start: string, end: string, over: Record<string, unknown> = {}) => ({
      id, classId: 'c', classTitle: `Class ${id}`, date: '2026-10-15', start, end, isExtra: false, note: null, taken: false, present: 0, late: 0, absent: 0, editable: true, ...over,
    });

    it('marks what is on now, what is next, and offers Mark for a class that has ended unmarked', () => {
      // 13:30 in Sri Lanka on Thursday 15 October 2026.
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date('2026-10-15T08:00:00Z'));
      const fixture = mount(TodayWidget);
      http.expectOne((r) => r.url === `${API}/api/attendance/sessions`).flush([
        session('a', '08:00', '09:30', { taken: true, present: 18, absent: 2 }),
        session('b', '10:00', '12:00'),
        session('c', '13:00', '15:00'),
        session('d', '16:00', '18:00'),
      ]);
      http.expectOne(`${API}/api/classes/timetable`).flush([]);
      fixture.detectChanges();
      const rows = Array.from(fixture.nativeElement.querySelectorAll('a.link')) as HTMLElement[];
      expect(rows).toHaveLength(4);
      expect(rows[0].querySelector('.done')).not.toBeNull();
      expect(rows[1].querySelector('.mark')).not.toBeNull();
      expect(rows[2].querySelector('.chip.now')).not.toBeNull();
      expect(rows[3].querySelector('.chip:not(.now)')?.textContent).toContain('dashboard.today.next');
      expect(rows[3].querySelector('.mark')).toBeNull();
      expect(fixture.nativeElement.querySelector('.line mat-icon')).toBeNull();
    });

    it('names the next class from the timetable when today is over', () => {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date('2026-10-15T14:00:00Z'));
      const fixture = mount(TodayWidget);
      http.expectOne((r) => r.url === `${API}/api/attendance/sessions`).flush([]);
      http.expectOne(`${API}/api/classes/timetable`).flush([
        { classId: 'x', title: 'Science', subjectId: 's', day: 5, start: '09:00', end: '11:00', placeType: 'venue', placeId: 'v', placeName: 'Hall', instituteId: null, overlapsWith: [] },
      ]);
      fixture.detectChanges();
      expect(text(fixture)).toContain('home.no_classes_today');
      expect(text(fixture)).toContain('dashboard.today.next_class');
    });

    it('still shows today when the timetable cannot be read', () => {
      const fixture = mount(TodayWidget);
      http.expectOne((r) => r.url === `${API}/api/attendance/sessions`).flush([session('a', '00:01', '00:02', { taken: true })]);
      http.expectOne(`${API}/api/classes/timetable`).flush('x', { status: 500, statusText: 'Server error' });
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelectorAll('a.link')).toHaveLength(1);
    });
  });
});
