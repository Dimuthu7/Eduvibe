import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { StudentHistory } from '../../core/api/exams.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { StudentTrend } from './student-trend';

const API = 'http://api.test';

const history = (): StudentHistory => ({
  enrolmentId: 'e1', studentId: 's1', indexNumber: 4, fullName: 'Amara Silva', classId: 'c1', classTitle: '2027 A/L Maths',
  exams: [
    { examId: 'x1', name: 'Unit 1', heldOn: '2026-09-01', maxTotal: 100, absent: false, total: 60, percent: 60, grade: 'C', rank: 2, ranked: 2, classAverage: 70, publishedAt: null },
    { examId: 'x2', name: 'Unit 2', heldOn: '2026-10-01', maxTotal: 100, absent: true, total: null, percent: null, grade: null, rank: null, ranked: 2, classAverage: 65, publishedAt: null },
    { examId: 'x3', name: 'Unit 3', heldOn: '2026-10-20', maxTotal: 50, absent: false, total: 45, percent: 90, grade: 'A', rank: 1, ranked: 3, classAverage: 72, publishedAt: null },
  ],
  classes: [{ enrolmentId: 'e1', classId: 'c1', title: '2027 A/L Maths' }, { enrolmentId: 'e9', classId: 'c2', title: '2027 A/L Physics' }],
});

describe('StudentTrend', () => {
  let http: HttpTestingController;

  const setup = async (examId = '') => {
    TestBed.configureTestingModule({ imports: [StudentTrend], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(StudentTrend);
    fixture.componentRef.setInput('enrolmentId', 'e1');
    fixture.componentRef.setInput('examId', examId);
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne(`${API}/api/exams/enrolments/e1/history`).flush(history());
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  };

  afterEach(() => http.verify());

  it('shows the average and best percentage, the chart and each exam newest first with rank of how many', async () => {
    const { el } = await setup();
    expect(el.querySelector('h1')!.textContent).toContain('Amara Silva');
    const totals = Array.from(el.querySelectorAll('.total strong')).map((t) => t.textContent);
    expect(totals).toEqual(['75%', '90%']);
    expect(el.querySelector('app-trend-chart')).not.toBeNull();
    const rows = Array.from(el.querySelectorAll('a.panel-row')).map((r) => r.querySelector('.row-title')!.textContent);
    expect(rows).toEqual(['Unit 3', 'Unit 2', 'Unit 1']);
    expect(el.textContent).toContain('45 / 50 · 90% · exams.rank 1 exams.of 3');
    expect(el.textContent).toContain('exams.absent');
  });

  it('goes back to the exam it came from, or to the class exams', async () => {
    const { el } = await setup('x3');
    expect(el.querySelector('a.back')!.getAttribute('href')).toBe('/exams/x3');
  });

  it('switches to the student\'s other class', async () => {
    const { fixture } = await setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    (fixture.componentInstance as unknown as { classControl: { setValue: (v: string) => void } }).classControl.setValue('e9');
    expect(navigate).toHaveBeenCalledWith(['/exams/student', 'e9'], { replaceUrl: true });
  });
});
