import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { Login } from './login';

describe('Login page', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue('http://api.test');
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
  });

  afterEach(() => http.verify());

  const fill = (root: HTMLElement, phone: string, password: string) => {
    const [phoneInput, passwordInput] = Array.from(root.querySelectorAll('input'));
    phoneInput.value = phone;
    phoneInput.dispatchEvent(new Event('input'));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
  };

  it('does not submit until both fields are filled', () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();

    const button = (fixture.nativeElement as HTMLElement).querySelector('button')!;
    expect(button.disabled).toBe(true);
  });

  it('shows the translated message when the server rejects the password', async () => {
    const fixture = TestBed.createComponent(Login);
    const root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    fill(root, '0771234567', 'wrong');
    fixture.detectChanges();
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectOne('http://api.test/api/identity/login').flush(
      { code: 'invalid_credentials' },
      { status: 401, statusText: 'Unauthorized' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('[role=alert]')?.textContent).toContain('error.invalid_credentials');
  });

  it('goes to the start screen after signing in', async () => {
    const fixture = TestBed.createComponent(Login);
    const root = fixture.nativeElement as HTMLElement;
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture.detectChanges();

    fill(root, '0771234567', 'right-pass');
    fixture.detectChanges();
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectOne('http://api.test/api/identity/login').flush({
      accessToken: 'a',
      refreshToken: 'r',
      accessTokenExpiresAt: '2030-01-01T00:00:00Z',
      user: { id: 'u', phone: '+94771234567', firstName: 'N', lastName: 'P', fullName: 'N P', email: null, language: 'en', roles: ['Teacher'], mustChangePassword: false, teacherId: 't', district: null, streamId: null, subjectIds: [] },
    });

    expect(navigate).toHaveBeenCalledWith('/home');
  });
});
