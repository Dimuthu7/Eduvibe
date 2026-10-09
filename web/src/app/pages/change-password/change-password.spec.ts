import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { User } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { ChangePassword } from './change-password';

const user = (change: Partial<User>): User => ({
  id: 'u', phone: '+94771234567', username: null, mustChooseUsername: true, firstName: 'N', lastName: 'P', fullName: 'N P',
  email: null, language: 'en', roles: ['Teacher'], mustChangePassword: true, teacherId: 't', district: null, streamId: null,
  subjectIds: [], ...change,
});

describe('ChangePassword (account set-up)', () => {
  let http: HttpTestingController;

  const setup = (u: User) => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue('http://api.test');
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    TestBed.inject(AuthService).user.set(u);
    const fixture = TestBed.createComponent(ChangePassword);
    fixture.detectChanges();
    const form = fixture.componentInstance['form'];
    const submit = () => {
      (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };
    return { fixture, form, submit };
  };

  afterEach(() => http.verify());

  it('asks for a valid username on first sign-in and sends nothing until it is', () => {
    const { form, submit } = setup(user({}));
    form.patchValue({ currentPassword: 'otp', username: 'ab', newPassword: 'New-pass-1', confirm: 'New-pass-1' });
    submit();
    http.expectNone('http://api.test/api/identity/change-password');
    expect(form.controls.username.errors).toEqual({ username: true });
  });

  it('sends the username with the new password', () => {
    const { form, submit } = setup(user({}));
    form.patchValue({ currentPassword: 'otp', username: 'nimal.p', newPassword: 'New-pass-1', confirm: 'New-pass-1' });
    submit();
    const request = http.expectOne('http://api.test/api/identity/change-password');
    expect(request.request.body).toEqual({ currentPassword: 'otp', newPassword: 'New-pass-1', username: 'nimal.p' });
    request.flush({}, { status: 500, statusText: 'x' });
  });

  it('shows "username taken" under the username field', () => {
    const { form, submit } = setup(user({}));
    form.patchValue({ currentPassword: 'otp', username: 'nimal.p', newPassword: 'New-pass-1', confirm: 'New-pass-1' });
    submit();
    http.expectOne('http://api.test/api/identity/change-password').flush({ code: 'username_taken' }, { status: 409, statusText: 'Conflict' });
    expect(form.controls.username.errors).toEqual({ server: 'error.username_taken' });
  });

  it('lets an older account choose only a username and keep its password', () => {
    const { form, submit } = setup(user({ mustChangePassword: false }));
    form.patchValue({ currentPassword: 'pw', username: 'old.account' });
    submit();
    const request = http.expectOne('http://api.test/api/identity/change-password');
    expect(request.request.body).toEqual({ currentPassword: 'pw', newPassword: null, username: 'old.account' });
    request.flush({}, { status: 500, statusText: 'x' });
  });

  it('requires a new password when changing it from the profile', () => {
    const { form, submit } = setup(user({ mustChangePassword: false, mustChooseUsername: false, username: 'nimal' }));
    form.patchValue({ currentPassword: 'pw' });
    submit();
    http.expectNone('http://api.test/api/identity/change-password');
    expect(form.controls.newPassword.hasError('required')).toBe(true);
  });
});
