import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { RemindersDialog, WEEKLY_CHOICES } from './reminders-dialog';

const API = 'http://api.test';
const URL = `${API}/api/fees/classes/c1/reminders`;

describe('RemindersDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  const setup = (loaded: { enabled: boolean; weeklyLimit: number | null; skippedEnrolments: string[] }) => {
    close.mockReset();
    TestBed.configureTestingModule({
      imports: [RemindersDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close } },
        { provide: MAT_DIALOG_DATA, useValue: { classId: 'c1', classTitle: 'Grade 6 ICT' } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(RemindersDialog);
    fixture.detectChanges();
    http.expectOne(URL).flush(loaded);
    fixture.detectChanges();
    const submit = () => {
      (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };
    return { fixture, form: fixture.componentInstance['form'], submit };
  };

  afterEach(() => http.verify());

  it('shows what the class has now, with "until paid" when there is no weekly limit', () => {
    const { form } = setup({ enabled: false, weeklyLimit: null, skippedEnrolments: [] });
    expect(form.getRawValue()).toEqual({ enabled: false, weeklyLimit: 0 });
  });

  it('offers until paid and one to eight weekly reminders', () => {
    expect(WEEKLY_CHOICES).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8]);
  });

  it('mentions students who have reminders turned off', () => {
    const { fixture } = setup({ enabled: true, weeklyLimit: 3, skippedEnrolments: ['e1'] });
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('reminders.skipped');
  });

  it('saves the choice, sending no limit for until paid, and closes', () => {
    const { form, submit } = setup({ enabled: true, weeklyLimit: 3, skippedEnrolments: [] });
    form.patchValue({ enabled: true, weeklyLimit: 0 });
    submit();
    const req = http.expectOne(URL);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ enabled: true, weeklyLimit: null });
    req.flush({ enabled: true, weeklyLimit: null, skippedEnrolments: [] });
    expect(close).toHaveBeenCalledWith(true);
  });

  it('sends a weekly limit as a number', () => {
    const { form, submit } = setup({ enabled: true, weeklyLimit: null, skippedEnrolments: [] });
    form.patchValue({ weeklyLimit: 4 });
    submit();
    const req = http.expectOne(URL);
    expect(req.request.body).toEqual({ enabled: true, weeklyLimit: 4 });
    req.flush({ enabled: true, weeklyLimit: 4, skippedEnrolments: [] });
  });

  it('shows a refused limit beside the field', () => {
    const { form, submit } = setup({ enabled: true, weeklyLimit: null, skippedEnrolments: [] });
    submit();
    http.expectOne(URL).flush({ code: 'weekly_limit_invalid' }, { status: 400, statusText: 'Bad Request' });
    expect(form.controls.weeklyLimit.errors).toEqual({ server: 'error.weekly_limit_invalid' });
    expect(close).not.toHaveBeenCalled();
  });
});
