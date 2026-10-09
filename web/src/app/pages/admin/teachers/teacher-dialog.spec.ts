import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { CatalogStore } from '../../../core/api/catalog.service';
import { AppConfig } from '../../../core/config/app-config';
import { TranslateService } from '../../../core/i18n/translate.service';
import { TeacherDialog } from './teacher-dialog';

describe('TeacherDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  beforeEach(() => {
    close.mockReset();
    TestBed.configureTestingModule({
      imports: [TeacherDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MatDialogRef, useValue: { close } }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue('http://api.test');
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    // The catalog lists load when the dialog opens; answer them with a tiny catalog.
    const store = TestBed.inject(CatalogStore);
    vi.spyOn(store, 'ensureLoaded').mockImplementation(() => {
      store.districts.set(['Colombo', 'Kandy']);
      store.allStreams.set([{ id: 's1', name: 'O/L', sortOrder: 1, isActive: true }]);
      store.allSubjects.set([
        { id: 'm1', name: 'Mathematics', sortOrder: 1, isActive: true },
        { id: 'sc1', name: 'Science', sortOrder: 2, isActive: true },
      ]);
    });
  });

  afterEach(() => http.verify());

  const setup = () => {
    const fixture = TestBed.createComponent(TeacherDialog);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const submit = () => {
      root.querySelector('form')!.dispatchEvent(new Event('submit'));
      // The searchable dropdowns update their error state during the first pass and render it on the second.
      fixture.detectChanges();
      fixture.detectChanges();
    };
    return { fixture, root, submit, form: fixture.componentInstance['form'] };
  };

  it('shows what is missing and sends nothing when submitted empty', () => {
    const { root, submit } = setup();
    submit();
    http.expectNone('http://api.test/api/identity/teachers');
    const errors = Array.from(root.querySelectorAll('mat-error')).map((e) => e.textContent!.trim());
    expect(errors).toContain('validation.required');
    expect(errors.length).toBeGreaterThanOrEqual(5);
  });

  it('rejects a bad phone number and email before calling the server', () => {
    const { form, submit } = setup();
    form.patchValue({ firstName: 'Nimal', lastName: 'Perera', phone: '12', email: 'nope', district: 'Colombo', streamId: 's1', subjectIds: ['m1'] });
    submit();
    http.expectNone('http://api.test/api/identity/teachers');
    expect(form.controls.phone.errors).toEqual({ phone: true });
    expect(form.controls.email.hasError('email')).toBe(true);
  });

  it('creates the teacher with multiple subjects and closes with the result', () => {
    const { form, submit } = setup();
    form.patchValue({ firstName: 'Nimal', lastName: 'Perera', phone: '0771234567', email: '', district: 'Kandy', streamId: 's1', subjectIds: ['m1', 'sc1'] });
    submit();
    const request = http.expectOne('http://api.test/api/identity/teachers');
    expect(request.request.body).toMatchObject({
      firstName: 'Nimal',
      lastName: 'Perera',
      district: 'Kandy',
      streamId: 's1',
      subjectIds: ['m1', 'sc1'],
      email: undefined,
    });
    const created = { teacher: { id: 't' }, oneTimePassword: 'abc' };
    request.flush(created);
    expect(close).toHaveBeenCalledWith(created);
  });

  it('shows "phone already registered" under the phone field', () => {
    const { fixture, root, form, submit } = setup();
    form.patchValue({ firstName: 'Nimal', lastName: 'Perera', phone: '0771234567', district: 'Kandy', streamId: 's1', subjectIds: ['m1'] });
    submit();
    http.expectOne('http://api.test/api/identity/teachers').flush({ code: 'phone_taken' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(form.controls.phone.errors).toEqual({ server: 'error.phone_taken' });
    expect(root.textContent).toContain('error.phone_taken');
    expect(close).not.toHaveBeenCalled();
  });
});
