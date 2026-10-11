import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { Register } from './register';

const API = 'http://api.test';
const URL = `${API}/api/students/register/tok`;

describe('Register', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [Register], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
  });

  afterEach(() => http.verify());

  const open = async () => {
    const fixture = TestBed.createComponent(Register);
    fixture.componentRef.setInput('token', 'tok');
    fixture.detectChanges();
    await Promise.resolve();
    return fixture;
  };
  const text = (fixture: { nativeElement: unknown }) => (fixture.nativeElement as HTMLElement).textContent ?? '';

  it('shows the class and teacher from the link, then the form', async () => {
    const fixture = await open();
    http.expectOne(URL).flush({ classTitle: '2027 A/L Maths', teacherName: 'Sunil Perera', expiresAt: '2026-10-18T00:00:00Z' });
    fixture.detectChanges();
    expect(text(fixture)).toContain('2027 A/L Maths');
    expect(text(fixture)).toContain('Sunil Perera');
    expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
  });

  it('says so when the link has expired', async () => {
    const fixture = await open();
    http.expectOne(URL).flush({ code: 'invite_expired' }, { status: 410, statusText: 'Gone' });
    fixture.detectChanges();
    expect(text(fixture)).toContain('error.invite_expired');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });

  it('needs every required field and a password of at least 8 characters before sending', async () => {
    const fixture = await open();
    http.expectOne(URL).flush({ classTitle: 'Maths', teacherName: null, expiresAt: '2026-10-18T00:00:00Z' });
    fixture.detectChanges();
    const form = fixture.componentInstance['form'];
    form.patchValue({ firstName: 'Amara', lastName: 'Silva', phone: '077 123 4567', password: 'short' });
    fixture.componentInstance['submit']();
    http.expectNone(URL);
    expect(form.controls.password.hasError('minlength')).toBe(true);
  });

  it('confirms the request once sent', async () => {
    const fixture = await open();
    http.expectOne(URL).flush({ classTitle: 'Maths', teacherName: null, expiresAt: '2026-10-18T00:00:00Z' });
    fixture.detectChanges();
    fixture.componentInstance['form'].patchValue({ firstName: 'Amara', lastName: 'Silva', phone: '077 123 4567', password: 'long-enough-1' });
    fixture.componentInstance['submit']();
    const request = http.expectOne({ method: 'POST', url: URL });
    expect(request.request.body.password).toBe('long-enough-1');
    request.flush({ outcome: 'created', classTitle: 'Maths' });
    fixture.detectChanges();
    expect(text(fixture)).toContain('register.done_created');
  });

  it('closes the page when the link stops working while the form is open', async () => {
    const fixture = await open();
    http.expectOne(URL).flush({ classTitle: 'Maths', teacherName: null, expiresAt: '2026-10-18T00:00:00Z' });
    fixture.detectChanges();
    fixture.componentInstance['form'].patchValue({ firstName: 'Amara', lastName: 'Silva', phone: '077 123 4567', password: 'long-enough-1' });
    fixture.componentInstance['submit']();
    http.expectOne({ method: 'POST', url: URL }).flush({ code: 'invite_full' }, { status: 410, statusText: 'Gone' });
    fixture.detectChanges();
    expect(text(fixture)).toContain('error.invite_full');
  });
});
