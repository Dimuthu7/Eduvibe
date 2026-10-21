import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Child } from '../../core/api/students.service';
import { AuthService } from '../../core/auth/auth.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { Family } from './family';

const API = 'http://api.test';

const klass = (id: string, over: Partial<Child['classes'][number]> = {}) => ({
  enrolmentId: `e-${id}`, classId: id, classTitle: `Class ${id}`, teacherName: `Teacher ${id}`, locked: false, slots: [{ day: 2, start: '16:00', end: '18:00' }], ...over,
});

const kamal: Child = { studentId: 's1', firstName: 'Kamal', fullName: 'Kamal Silva', classes: [klass('c1')] };
const nimali: Child = { studentId: 's2', firstName: 'Nimali', fullName: 'Nimali Silva', classes: [klass('c2', { locked: true })] };

describe('Family', () => {
  let http: HttpTestingController;

  const setup = async (children: Child[]) => {
    TestBed.configureTestingModule({ imports: [Family], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    vi.spyOn(TestBed.inject(AuthService), 'hasRole').mockReturnValue(false);
    const fixture = TestBed.createComponent(Family);
    fixture.detectChanges();
    http.expectOne(`${API}/api/students/family`).flush(children);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  };

  const flushChild = (id: string, over: { attendance?: unknown[]; fees?: unknown[]; results?: unknown[] } = {}) => {
    http.expectOne(`${API}/api/attendance/children/${id}`).flush(over.attendance ?? []);
    http.expectOne(`${API}/api/fees/children/${id}`).flush(over.fees ?? []);
    http.expectOne(`${API}/api/exams/children/${id}`).flush(over.results ?? []);
  };

  afterEach(() => http.verify());

  it('shows a child with attendance, fees and results of each class', async () => {
    const fixture = await setup([kamal]);
    flushChild('s1', {
      attendance: [{ classId: 'c1', sessions: 4, present: 3, late: 0, absent: 1, percent: 75, recent: [{ date: '2026-10-12', status: 'Absent' }] }],
      fees: [{ classId: 'c1', outstanding: 2000, months: [{ month: '2026-10', due: 2000, paid: 0, status: 'Unpaid' }], receipts: [{ receiptNo: 'R-1', amount: 2000, method: 'Cash', paidOn: '2026-09-05' }] }],
      results: [{ examId: 'x1', classId: 'c1', name: 'Term 1', heldOn: '2026-10-01', maxTotal: 100, absent: false, total: 80, percent: 80, grade: 'A', rank: 1, ranked: 20, parts: [] }],
    });
    fixture.detectChanges();
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('mat-button-toggle-group')).toBeNull();
    expect(el.querySelector('h2')!.textContent).toContain('Kamal Silva');
    expect(el.textContent).toContain('Class c1');
    expect(el.textContent).toContain('Teacher c1');
    expect(el.textContent).toContain('75%');
    expect(el.textContent).toContain('Term 1');
    expect(el.textContent).toContain('80 / 100');
    expect(el.textContent).toContain('exams.rank 1 exams.of 20');
    expect(el.querySelector('.grade-chip')!.textContent!.trim()).toBe('A');
    expect(el.textContent).toContain('R-1');
    expect(el.textContent).toContain('family.owing');
  });

  it('lets the parent switch between children and says why a locked class shows nothing', async () => {
    const fixture = await setup([kamal, nimali]);
    flushChild('s1');
    fixture.detectChanges();
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(Array.from(el.querySelectorAll('mat-button-toggle')).map((b) => b.textContent!.trim())).toEqual(['Kamal', 'Nimali']);

    (el.querySelectorAll('mat-button-toggle button')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    flushChild('s2');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(el.querySelector('h2')!.textContent).toContain('Nimali Silva');
    expect(el.querySelector('.locked')!.textContent).toContain('family.locked');
    expect(el.querySelector('[aria-label="family.fees"]')).toBeNull();
  });

  it('says so when no child is linked yet', async () => {
    const fixture = await setup([]);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('family.no_children');
  });
});
