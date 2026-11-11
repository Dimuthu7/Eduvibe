import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { StudentDialog } from './student-dialog';

const API = 'http://api.test';

describe('StudentDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  beforeEach(() => {
    close.mockReset();
    TestBed.configureTestingModule({
      imports: [StudentDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close } },
        { provide: MAT_DIALOG_DATA, useValue: { classId: 'c1' } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
  });

  afterEach(() => http.verify());

  const setup = () => {
    const fixture = TestBed.createComponent(StudentDialog);
    fixture.detectChanges();
    http.expectOne(`${API}/api/classes/classes?status=active`).flush([{ id: 'c1', title: '2027 A/L Maths' }]);
    fixture.detectChanges();
    const form = fixture.componentInstance['form'];
    const submit = () => {
      (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };
    const fill = () => form.patchValue({ firstName: 'Kasun', lastName: 'Perera', phone: '077 123 4567' });
    return { fixture, form, submit, fill };
  };

  it('sends nothing and shows what is missing when submitted empty', () => {
    const { fixture, submit } = setup();
    submit();
    http.expectNone(`${API}/api/students`);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('mat-error').length).toBeGreaterThanOrEqual(3);
  });

  it('asks for a discount amount only when the fee plan is a discount', () => {
    const { form, submit, fill } = setup();
    fill();
    form.patchValue({ feePlan: 'Discount' });
    submit();
    http.expectNone(`${API}/api/students`);
    expect(form.controls.discountAmount.hasError('required')).toBe(true);

    form.patchValue({ discountAmount: 500 });
    submit();
    const request = http.expectOne(`${API}/api/students`);
    expect(request.request.body).toMatchObject({ classId: 'c1', phone: '077 123 4567', feePlan: 'Discount', discountAmount: 500 });
  });

  it('closes with the new student and the one-time password', () => {
    const { submit, fill } = setup();
    fill();
    submit();
    const created = { row: { fullName: 'Kasun Perera' }, oneTimePassword: 'abc123XYZ9' };
    const request = http.expectOne(`${API}/api/students`);
    expect(request.request.body.discountAmount).toBeNull();
    request.flush(created);
    expect(close).toHaveBeenCalledWith(created);
  });

  it('offers to add an existing student instead of making a duplicate, then enrols them', () => {
    const { fixture, submit, fill } = setup();
    fill();
    submit();
    http
      .expectOne(`${API}/api/students`)
      .flush({ code: 'student_exists', existing: { enrolledHere: false } }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('students.exists');
    expect(close).not.toHaveBeenCalled();

    fixture.componentInstance['enrolExisting']();
    const again = http.expectOne(`${API}/api/students`);
    expect(again.request.body.enrolExisting).toBe(true);
    again.flush({ row: {}, oneTimePassword: null });
    expect(close).toHaveBeenCalled();
  });

  it('says so beside the phone when the number belongs to a student with another first name', () => {
    const { form, submit, fill } = setup();
    fill();
    submit();
    http.expectOne(`${API}/api/students`).flush({ code: 'student_name_mismatch' }, { status: 409, statusText: 'Conflict' });
    expect(form.controls.phone.errors).toEqual({ server: 'error.student_name_mismatch' });
  });

  it('puts a server rejection next to its field', () => {
    const { form, submit, fill } = setup();
    fill();
    submit();
    http.expectOne(`${API}/api/students`).flush({ code: 'phone_invalid' }, { status: 400, statusText: 'Bad Request' });
    expect(form.controls.phone.errors).toEqual({ server: 'error.phone_invalid' });
  });
});

describe('StudentDialog editing a student another teacher added first', () => {
  const student = {
    id: 's1', firstName: 'Kamal', lastName: 'Silva', fullName: 'Kamal Silva', phone: '+94771111111', school: null, grade: null,
    guardianName: 'Mrs Fernando', guardianPhone: '+94772222222', hasAccount: true, hasParent: false, enrolments: [], managesAccount: false,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [StudentDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: { student } },
      ],
    });
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
  });

  it('shows the guardian phone but does not let it be changed, and says why', () => {
    const fixture = TestBed.createComponent(StudentDialog);
    fixture.detectChanges();
    const form = fixture.componentInstance['form'];
    expect(form.controls.guardianPhone.disabled).toBe(true);
    expect(form.getRawValue().guardianPhone).toBe('+94772222222');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('students.guardian_phone_locked');
  });
});
