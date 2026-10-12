import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { todayColombo } from '../../shared/format';
import { ExtraSessionDialog } from './extra-session-dialog';

const API = 'http://api.test';

describe('ExtraSessionDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  const setup = (data: { classId?: string } | null) => {
    close.mockReset();
    TestBed.configureTestingModule({
      imports: [ExtraSessionDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MatDialogRef, useValue: { close } }, { provide: MAT_DIALOG_DATA, useValue: data }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(ExtraSessionDialog);
    fixture.detectChanges();
    http.expectOne(`${API}/api/classes/classes?status=active`).flush([{ id: 'c1', title: 'Grade 6 ICT' }]);
    const form = fixture.componentInstance['form'];
    const submit = () => {
      (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };
    return { form, submit };
  };

  afterEach(() => http.verify());

  it('starts on today with no class chosen when opened from the Classes list', () => {
    const { form } = setup({});
    expect(form.getRawValue()).toMatchObject({ classId: '', date: todayColombo() });
  });

  it('has the class chosen when opened from one class', () => {
    const { form } = setup({ classId: 'c1' });
    expect(form.controls.classId.value).toBe('c1');
  });

  it('asks for the missing details and sends nothing', () => {
    const { form, submit } = setup({});
    submit();
    expect(form.controls.classId.errors).toMatchObject({ required: true });
    http.expectNone(`${API}/api/attendance/sessions`);
  });

  it('adds the class and closes with the new session', () => {
    const { form, submit } = setup({ classId: 'c1' });
    form.patchValue({ start: '16:00', end: '18:00', note: ' Revision ' });
    submit();
    const req = http.expectOne(`${API}/api/attendance/sessions`);
    expect(req.request.body).toMatchObject({ classId: 'c1', start: '16:00', end: '18:00', note: 'Revision' });
    req.flush({ id: 's1', date: todayColombo() });
    expect(close).toHaveBeenCalledWith(expect.objectContaining({ id: 's1' }));
  });
});
