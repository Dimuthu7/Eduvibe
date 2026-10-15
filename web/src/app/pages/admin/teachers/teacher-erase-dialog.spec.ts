import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TeacherSummary } from '../../../core/api/admin.service';
import { AppConfig } from '../../../core/config/app-config';
import { TeacherEraseDialog } from './teacher-erase-dialog';

const teacher = { id: 't1', fullName: 'Nimal Perera' } as TeacherSummary;

describe('TeacherEraseDialog', () => {
  let http: HttpTestingController;
  const ref = { close: vi.fn() };

  beforeEach(() => {
    ref.close.mockReset();
    TestBed.configureTestingModule({
      imports: [TeacherEraseDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MAT_DIALOG_DATA, useValue: teacher }, { provide: MatDialogRef, useValue: ref }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue('http://api.test');
  });

  afterEach(() => http.verify());

  const typeAndSubmit = (text: string) => {
    const fixture = TestBed.createComponent(TeacherEraseDialog);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('input') as HTMLInputElement;
    input.value = text;
    input.dispatchEvent(new Event('input'));
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    return fixture;
  };

  it('does not call the server until a name is typed', () => {
    typeAndSubmit('');
    http.expectNone('http://api.test/api/privacy/teachers/t1/erase');
    expect(ref.close).not.toHaveBeenCalled();
  });

  it('sends the typed name and closes when the teacher is erased', () => {
    typeAndSubmit('nimal perera');
    const request = http.expectOne('http://api.test/api/privacy/teachers/t1/erase');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ confirm: 'nimal perera' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(ref.close).toHaveBeenCalledWith(true);
  });

  it('shows a wrong name beside the field and stays open', () => {
    const fixture = typeAndSubmit('Someone Else');
    http.expectOne('http://api.test/api/privacy/teachers/t1/erase').flush({ code: 'confirm_mismatch' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(ref.close).not.toHaveBeenCalled();
    expect(fixture.componentInstance['form'].controls.confirm.errors).toEqual({ server: 'error.confirm_mismatch' });
  });
});
