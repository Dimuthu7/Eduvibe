import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { EnrolmentFees } from '../../core/api/fees.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { ToastService } from '../../core/ui/toast.service';
import { StudentFees } from './student-fees';

const API = 'http://api.test';

const fees = (over: Partial<EnrolmentFees> = {}): EnrolmentFees => ({
  enrolmentId: 'e1', studentId: 's1', fullName: 'Amara Silva', indexNumber: 3, classId: 'c1', classTitle: 'Grade 6 ICT', feePlan: 'Full',
  discountAmount: 0, isActive: true, outstanding: 1500, dues: [], payments: [],
  reminders: [
    { step: 'late', number: 2, month: '2026-10', at: '2026-11-03T04:30:00Z', channel: 'App' },
    { step: 'before', number: 0, month: '2026-10', at: '2026-09-30T04:30:00Z', channel: 'App' },
  ],
  remindersSkipped: false,
  ...over,
});

describe('StudentFees reminders', () => {
  let http: HttpTestingController;
  const toast = { success: vi.fn(), error: vi.fn() };

  const setup = (record: EnrolmentFees) => {
    toast.success.mockReset();
    TestBed.configureTestingModule({
      imports: [StudentFees],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ToastService, useValue: toast }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    vi.spyOn(TestBed.inject(TranslateService), 'format').mockImplementation((key, values) => `${key} ${JSON.stringify(values)}`);
    const fixture = TestBed.createComponent(StudentFees);
    fixture.componentRef.setInput('enrolmentId', 'e1');
    fixture.detectChanges();
    return fixture;
  };

  const open = async (record: EnrolmentFees) => {
    const fixture = setup(record);
    await Promise.resolve();
    http.expectOne(`${API}/api/fees/enrolments/e1`).flush(record);
    fixture.detectChanges();
    return fixture;
  };

  afterEach(() => {
    // The avatar asks for the photo manifest.
    http.match(`${API}/api/students/photos`).forEach((r) => r.flush([]));
    http.verify();
  });

  it('lists the reminders that were sent, newest first, with the app as the way they went', async () => {
    const fixture = await open(fees());
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('reminders.step_late');
    expect(text).toContain('reminders.step_before');
    expect(text.indexOf('reminders.step_late')).toBeLessThan(text.indexOf('reminders.step_before'));
    expect(text).toContain('reminders.channel.App');
  });

  it('says so when none were sent', async () => {
    const fixture = await open(fees({ reminders: [] }));
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('reminders.none');
  });

  it('stops reminders for this student from the switch', async () => {
    const fixture = await open(fees());
    const toggle = (fixture.nativeElement as HTMLElement).querySelector('mat-slide-toggle button') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-checked')).toBe('true');
    toggle.click();
    const req = http.expectOne(`${API}/api/fees/enrolments/e1/reminders`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ skip: true });
    req.flush(null);
    fixture.detectChanges();
    expect(toast.success).toHaveBeenCalledWith('reminders.turned_off');
    expect(toggle.getAttribute('aria-checked')).toBe('false');
  });

  it('shows the switch off for a student whose reminders are stopped', async () => {
    const fixture = await open(fees({ remindersSkipped: true }));
    const toggle = (fixture.nativeElement as HTMLElement).querySelector('mat-slide-toggle button') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-checked')).toBe('false');
  });
});
