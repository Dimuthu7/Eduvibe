import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { ExamDetail } from '../../core/api/exams.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { ExamScreen } from './exam';

const API = 'http://api.test';
const bands = [
  { grade: 'A', minPercent: 75 }, { grade: 'B', minPercent: 65 }, { grade: 'C', minPercent: 50 }, { grade: 'S', minPercent: 35 }, { grade: 'W', minPercent: 0 },
];

const student = (n: number, name: string, over: Partial<ExamDetail['students'][number]> = {}) => ({
  enrolmentId: `e${n}`, indexNumber: n, fullName: name, isActive: true, absent: false, marks: {}, total: null, percent: null, grade: null, rank: null, ...over,
});

const detail = (): ExamDetail => ({
  id: 'x1', classId: 'c1', classTitle: 'Grade 6 ICT', name: 'Term test', heldOn: '2026-10-10', maxTotal: 100,
  parts: [{ id: 'mcq', name: 'MCQ', maxMark: 40 }, { id: 'essay', name: 'Essay', maxMark: 60 }],
  publishedAt: null,
  bands, summary: { students: 3, marked: 1, averagePercent: 90 },
  analysis: {
    sat: 1, absent: 0, highest: 90, lowest: 90, average: 90, median: 90, passMark: 35, passed: 1, passRate: 100,
    grades: [{ grade: 'A', count: 1 }, { grade: 'B', count: 0 }, { grade: 'C', count: 0 }, { grade: 'S', count: 0 }, { grade: 'W', count: 0 }],
    parts: [{ partId: 'mcq', name: 'MCQ', maxMark: 40, average: 40 }, { partId: 'essay', name: 'Essay', maxMark: 60, average: 50 }],
  },
  students: [
    student(1, 'Amara Silva', { marks: { mcq: 40, essay: 50 }, total: 90, percent: 90, grade: 'A', rank: 1 }),
    student(2, 'Bandula Perera'),
    student(3, 'Chamari Fernando'),
  ],
});

describe('ExamScreen', () => {
  let http: HttpTestingController;

  const setup = async () => {
    TestBed.configureTestingModule({ imports: [ExamScreen], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(ExamScreen);
    fixture.componentRef.setInput('id', 'x1');
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne(`${API}/api/exams/x1`).flush(detail());
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const boxes = () => Array.from(el.querySelectorAll<HTMLInputElement>('input.mark-input'));
    const type = (box: HTMLInputElement, value: string) => {
      box.value = value;
      box.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const save = () => Array.from(el.querySelectorAll<HTMLButtonElement>('.savebar button'))[0];
    return { fixture, el, boxes, type, save };
  };

  // Avatars ask once which students have a photo; none do here.
  const settlePhotos = () => http.match((r) => r.url.endsWith('/api/students/photos')).forEach((r) => r.flush([]));

  afterEach(() => {
    settlePhotos();
    http.verify();
  });

  it('lists every student with a box for each part, starting from the saved marks', async () => {
    const { el, boxes, save } = await setup();
    expect(el.textContent).toContain('Amara Silva');
    expect(boxes()).toHaveLength(6);
    expect(boxes().map((b) => b.value)).toEqual(['40', '50', '', '', '', '']);
    expect(el.textContent).toContain('exams.rank 1');
    expect(save().disabled).toBe(true);
  });

  it('shows the total and grade as marks are typed and sends only the changed students', async () => {
    const { fixture, el, boxes, type, save } = await setup();
    type(boxes()[2], '30');
    expect(el.textContent).toContain('exams.not_complete');
    type(boxes()[3], '25.5');
    expect(el.textContent).toContain('55.5');
    expect(el.textContent).toContain('C');
    expect(save().disabled).toBe(false);

    save().click();
    const req = http.expectOne(`${API}/api/exams/x1/marks`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ rows: [{ enrolmentId: 'e2', absent: false, marks: { mcq: 30, essay: 25.5 } }] });
    const saved = detail();
    saved.students[1] = student(2, 'Bandula Perera', { marks: { mcq: 30, essay: 25.5 }, total: 55.5, percent: 55.5, grade: 'C', rank: 2 });
    req.flush(saved);
    fixture.detectChanges();
    expect(save().disabled).toBe(true);
    expect(el.textContent).toContain('exams.all_saved');
  });

  it('keeps Absent apart from 0 and sends the student as absent without marks', async () => {
    const { fixture, el, boxes, save } = await setup();
    const absent = el.querySelectorAll<HTMLButtonElement>('button.absent-toggle')[1];
    absent.click();
    fixture.detectChanges();
    expect(boxes()[2].disabled).toBe(true);
    save().click();
    const req = http.expectOne(`${API}/api/exams/x1/marks`);
    expect(req.request.body).toEqual({ rows: [{ enrolmentId: 'e2', absent: true, marks: {} }] });
    req.flush(detail());
  });

  it('refuses a mark above the part maximum and will not save it', async () => {
    const { el, boxes, type, save } = await setup();
    type(boxes()[2], '45');
    expect(el.textContent).toContain('exams.mark_problem');
    expect(save().disabled).toBe(true);
    type(boxes()[2], '40');
    expect(el.textContent).not.toContain('exams.mark_problem');
  });

  it('clears a student by emptying their boxes', async () => {
    const { fixture, boxes, type, save } = await setup();
    type(boxes()[0], '');
    type(boxes()[1], '');
    save().click();
    const req = http.expectOne(`${API}/api/exams/x1/marks`);
    expect(req.request.body).toEqual({ rows: [{ enrolmentId: 'e1', absent: false, marks: { mcq: null, essay: null } }] });
    req.flush(detail());
    fixture.detectChanges();
  });

  it('moves to the next box when Enter is pressed', async () => {
    const { boxes } = await setup();
    boxes()[2].focus();
    boxes()[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(document.activeElement).toBe(boxes()[3]);
  });

  it('ranks the saved results with ties first, then unfinished, absent and empty students', async () => {
    const { fixture, el } = await setup();
    const component = fixture.componentInstance as unknown as { ranked: () => { fullName: string }[] };
    expect(component.ranked().map((s) => s.fullName)).toEqual(['Amara Silva', 'Bandula Perera', 'Chamari Fernando']);
    (el.querySelectorAll<HTMLElement>('mat-button-toggle button')[1]).click();
    fixture.detectChanges();
    expect(el.textContent).toContain('exams.average');
  });

  it('links each result to the student\'s results across exams', async () => {
    const { fixture, el } = await setup();
    (el.querySelectorAll<HTMLElement>('mat-button-toggle button')[1]).click();
    fixture.detectChanges();
    const link = el.querySelector<HTMLAnchorElement>('a.panel-row')!;
    expect(link.getAttribute('href')).toBe('/exams/student/e1?examId=x1');
  });

  it('shows the class analysis', async () => {
    const { fixture, el } = await setup();
    (el.querySelectorAll<HTMLElement>('mat-button-toggle button')[2]).click();
    fixture.detectChanges();
    expect(el.querySelector('app-exam-analysis')).not.toBeNull();
    expect(el.textContent).toContain('100%');
    expect(el.textContent).toContain('exams.median');
  });

  it('publishes after the teacher confirms, and asks for unsaved marks to be saved first', async () => {
    const { fixture, el, boxes, type } = await setup();
    expect(el.textContent).toContain('exams.not_published');
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(true) } as never);
    const publish = () => el.querySelector<HTMLButtonElement>('.share button')!;

    type(boxes()[2], '30');
    publish().click();
    expect(open).not.toHaveBeenCalled();
    type(boxes()[2], '');

    publish().click();
    expect(open).toHaveBeenCalled();
    const req = http.expectOne(`${API}/api/exams/x1/publish`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...detail(), publishedAt: '2026-10-14T08:00:00Z' });
    fixture.detectChanges();
    expect(el.textContent).toContain('exams.published_on');
    expect(el.querySelector('.share button')).toBeNull();
  });
});
