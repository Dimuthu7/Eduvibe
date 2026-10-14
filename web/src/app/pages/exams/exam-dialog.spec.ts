import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { ExamDetail } from '../../core/api/exams.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { ExamDialog, ExamDialogData } from './exam-dialog';

const API = 'http://api.test';

describe('ExamDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  const setup = (data: ExamDialogData = { classId: 'c1' }) => {
    close.mockReset();
    TestBed.configureTestingModule({
      imports: [ExamDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MatDialogRef, useValue: { close } }, { provide: MAT_DIALOG_DATA, useValue: data }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(ExamDialog);
    fixture.detectChanges();
    if (!data.exam) http.expectOne(`${API}/api/classes/classes?status=active`).flush([{ id: 'c1', title: 'Grade 6 ICT' }]);
    const component = fixture.componentInstance as unknown as {
      form: import('@angular/forms').FormGroup;
      parts: import('@angular/forms').FormArray;
      addPart: () => void;
      removePart: (i: number) => void;
      locked: boolean;
    };
    const submit = () => {
      (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };
    return { fixture, component, submit };
  };

  afterEach(() => http.verify());

  it('starts with one part out of 100 and today as the date', () => {
    const { component } = setup();
    expect(component.parts.length).toBe(1);
    expect(component.parts.at(0).value).toMatchObject({ name: 'exams.default_part', maxMark: 100 });
  });

  it('needs a name', () => {
    const { component, submit } = setup();
    submit();
    expect(component.form.get('name')!.errors).toMatchObject({ required: true });
    http.expectNone(`${API}/api/exams`);
  });

  it('adds and removes parts, and keeps at least one', () => {
    const { component } = setup();
    component.addPart();
    expect(component.parts.length).toBe(2);
    expect(component.parts.at(1).value.name).toBe('exams.part 2');
    component.removePart(0);
    expect(component.parts.length).toBe(1);
  });

  it('sends the exam with its parts and closes with the saved exam', () => {
    const { component, submit } = setup();
    component.form.patchValue({ name: '  Term test ', heldOn: '2026-10-10' });
    component.parts.at(0).patchValue({ name: 'MCQ', maxMark: 40 });
    component.addPart();
    component.parts.at(1).patchValue({ name: 'Essay', maxMark: '60' });
    submit();
    const req = http.expectOne(`${API}/api/exams`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      classId: 'c1', name: 'Term test', heldOn: '2026-10-10',
      parts: [{ id: undefined, name: 'MCQ', maxMark: 40 }, { id: undefined, name: 'Essay', maxMark: 60 }],
    });
    req.flush({ id: 'x1' });
    expect(close).toHaveBeenCalledWith(expect.objectContaining({ id: 'x1' }));
  });

  it('shows a part problem from the server in the banner', () => {
    const { fixture, component, submit } = setup();
    component.form.patchValue({ name: 'T' });
    submit();
    http.expectOne(`${API}/api/exams`).flush({ code: 'part_name_duplicate' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('error.part_name_duplicate');
    expect(close).not.toHaveBeenCalled();
  });

  it('puts a name problem beside the name', () => {
    const { component, submit } = setup();
    component.form.patchValue({ name: 'T' });
    submit();
    http.expectOne(`${API}/api/exams`).flush({ code: 'name_too_long' }, { status: 400, statusText: 'Bad Request' });
    expect(component.form.get('name')!.errors).toEqual({ server: 'error.name_too_long' });
  });

  describe('changing an exam', () => {
    const exam = (withMarks: boolean): ExamDetail => ({
      id: 'x1', classId: 'c1', classTitle: 'Grade 6 ICT', name: 'Term test', heldOn: '2026-10-10', maxTotal: 100,
      parts: [{ id: 'p1', name: 'MCQ', maxMark: 40 }, { id: 'p2', name: 'Essay', maxMark: 60 }], bands: [],
      summary: { students: 1, marked: withMarks ? 1 : 0, averagePercent: null },
      students: [{
        enrolmentId: 'e1', indexNumber: 1, fullName: 'Amara', isActive: true, absent: false, marks: withMarks ? { p1: 20 } : {},
        total: null, percent: null, grade: null, rank: null,
      }],
    });

    it('keeps each part id so marks stay with their part, and locks the structure once marks exist', () => {
      const { component, submit } = setup({ exam: exam(true) });
      expect(component.locked).toBe(true);
      component.parts.at(1).patchValue({ maxMark: 70 });
      submit();
      const req = http.expectOne(`${API}/api/exams/x1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body.parts).toEqual([{ id: 'p1', name: 'MCQ', maxMark: 40 }, { id: 'p2', name: 'Essay', maxMark: 70 }]);
      req.flush(exam(true));
    });

    it('lets parts be added or removed while there are no marks', () => {
      const { component } = setup({ exam: exam(false) });
      expect(component.locked).toBe(false);
    });
  });
});
