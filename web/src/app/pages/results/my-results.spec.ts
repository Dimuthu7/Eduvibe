import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MyExam } from '../../core/api/exams.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { MyResults } from './my-results';

const API = 'http://api.test';

const exam = (id: string, classId: string, over: Partial<MyExam> = {}): MyExam => ({
  examId: id, classId, name: `Exam ${id}`, heldOn: '2026-10-10', maxTotal: 100, absent: false, total: 70, percent: 70, grade: 'B', rank: 2, ranked: 25,
  parts: [{ name: 'MCQ', maxMark: 40, mark: 30 }, { name: 'Essay', maxMark: 60, mark: 40 }], ...over,
});

describe('MyResults', () => {
  let http: HttpTestingController;

  const setup = async (classId = '') => {
    TestBed.configureTestingModule({ imports: [MyResults], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(MyResults);
    fixture.componentRef.setInput('classId', classId);
    fixture.detectChanges();
    http.expectOne(`${API}/api/students/me/classes`).flush([
      { classId: 'c1', classTitle: 'Maths', teacherName: 'Nimal Perera', slots: [] },
      { classId: 'c2', classTitle: 'Physics', teacherName: null, slots: [] },
      { classId: 'c3', classTitle: 'Chemistry', teacherName: null, slots: [] },
    ]);
    http.expectOne(`${API}/api/exams/mine`).flush([
      exam('1', 'c1'),
      exam('2', 'c2', { absent: true, total: null, percent: null, grade: null, rank: null }),
      exam('3', 'c1', { percent: null, grade: null, rank: null, total: 30, parts: [{ name: 'MCQ', maxMark: 40, mark: 30 }, { name: 'Essay', maxMark: 60, mark: null }] }),
    ]);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  };

  afterEach(() => http.verify());

  it('groups results by class, leaves out classes with none, and shows marks, grade and rank of how many', async () => {
    const el = await setup();
    expect(Array.from(el.querySelectorAll('h2')).map((h) => h.textContent!.trim())).toEqual(['Maths', 'Physics']);
    expect(el.textContent).toContain('Nimal Perera');
    expect(el.textContent).toContain('70 / 100');
    expect(el.textContent).toContain('exams.rank 2 exams.of 25');
    expect(el.querySelector('.grade-chip')!.textContent!.trim()).toBe('B');
    expect(el.textContent).toContain('MCQ 30/40');
    expect(el.textContent).toContain('results.not_complete');
    expect(el.textContent).toContain('exams.absent');
  });

  it('puts the class asked for first', async () => {
    const el = await setup('c2');
    expect(el.querySelector('h2')!.textContent!.trim()).toBe('Physics');
  });
});
