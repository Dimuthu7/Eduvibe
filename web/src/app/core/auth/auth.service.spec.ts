import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AppConfig } from '../config/app-config';
import { TranslateService } from '../i18n/translate.service';
import { Session, User } from './auth.models';
import { AuthService } from './auth.service';
import { authInterceptor } from './auth.interceptor';

const API = 'http://api.test';

const user = (overrides: Partial<User> = {}): User => ({
  id: 'u1',
  phone: '+94771234567',
  username: 'nimal',
  mustChooseUsername: false,
  firstName: 'Nimal',
  lastName: 'Perera',
  fullName: 'Nimal Perera',
  email: null,
  language: 'en',
  roles: ['Teacher'],
  mustChangePassword: false,
  teacherId: 't1',
  district: null,
  streamId: null,
  subjectIds: [],
  ...overrides,
});

const session = (overrides: Partial<Session> = {}): Session => ({
  accessToken: 'access-1',
  refreshToken: 'refresh-1',
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  user: user(),
  ...overrides,
});

describe('AuthService', () => {
  let http: HttpTestingController;

  const setup = () => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    return TestBed.inject(AuthService);
  };

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('stores the user and tokens after sign-in', () => {
    const auth = setup();

    auth.login('0771234567', 'secret').subscribe();
    const request = http.expectOne(`${API}/api/identity/login`);
    expect(request.request.body).toEqual({ username: '0771234567', password: 'secret' });
    request.flush(session());

    expect(auth.user()?.fullName).toBe('Nimal Perera');
    expect(auth.accessToken).toBe('access-1');
    expect(localStorage.getItem('eduvibe.session')).toContain('refresh-1');
  });

  it('sends a one-time password user to change it first', () => {
    const auth = setup();
    auth.login('0771234567', 'otp').subscribe();
    http.expectOne(`${API}/api/identity/login`).flush(session({ user: user({ mustChangePassword: true }) }));

    expect(auth.homeRoute()).toBe('/change-password');
  });

  it('sends an older account without a username to choose one first', () => {
    const auth = setup();
    auth.login('0771234567', 'pw').subscribe();
    http.expectOne(`${API}/api/identity/login`).flush(session({ user: user({ mustChooseUsername: true, username: null }) }));

    expect(auth.homeRoute()).toBe('/change-password');
  });

  it('sends the chosen username with the new password and leaves out an empty password', () => {
    const auth = setup();
    auth.changePassword('otp', 'New-pass-1', 'nimal.p').subscribe();
    const first = http.expectOne(`${API}/api/identity/change-password`);
    expect(first.request.body).toEqual({ currentPassword: 'otp', newPassword: 'New-pass-1', username: 'nimal.p' });
    first.flush(session());

    auth.changePassword('pw', '', 'nimal.p').subscribe();
    const second = http.expectOne(`${API}/api/identity/change-password`);
    expect(second.request.body.newPassword).toBeNull();
    second.flush(session());
  });

  it('starts a Super Admin on the teachers screen', () => {
    const auth = setup();
    auth.login('0771234567', 'pw').subscribe();
    http.expectOne(`${API}/api/identity/login`).flush(session({ user: user({ roles: ['SuperAdmin'], teacherId: null }) }));

    expect(auth.homeRoute()).toBe('/admin/dashboard');
  });

  it('sends a student to their own start screen, not the teacher home', () => {
    const auth = setup();
    auth.login('0771234567', 'pw').subscribe();
    http.expectOne(`${API}/api/identity/login`).flush(session({ user: user({ roles: ['Student'], teacherId: null }) }));

    expect(auth.homeRoute()).toBe('/welcome');
  });

  it('adds the access token to API calls only', () => {
    const auth = setup();
    auth.login('0771234567', 'pw').subscribe();
    http.expectOne(`${API}/api/identity/login`).flush(session());

    auth['http'].get(`${API}/api/identity/me`).subscribe();
    expect(http.expectOne(`${API}/api/identity/me`).request.headers.get('Authorization')).toBe('Bearer access-1');

    auth['http'].get('https://elsewhere.test/data').subscribe();
    expect(http.expectOne('https://elsewhere.test/data').request.headers.has('Authorization')).toBe(false);
  });

  it('refreshes once on a 401 and retries the call', async () => {
    const auth = setup();
    auth.login('0771234567', 'pw').subscribe();
    http.expectOne(`${API}/api/identity/login`).flush(session());

    let result: unknown;
    auth['http'].get(`${API}/api/identity/teachers`).subscribe((value) => (result = value));
    http.expectOne(`${API}/api/identity/teachers`).flush(null, { status: 401, statusText: 'Unauthorized' });

    await vi.waitFor(() => http.expectOne(`${API}/api/identity/refresh`).flush(session({ accessToken: 'access-2', refreshToken: 'refresh-2' })));
    await vi.waitFor(() => {
      const retry = http.expectOne(`${API}/api/identity/teachers`);
      expect(retry.request.headers.get('Authorization')).toBe('Bearer access-2');
      retry.flush(['ok']);
    });

    expect(result).toEqual(['ok']);
  });

  it('signs out locally and goes to the sign-in screen when refresh fails', async () => {
    const auth = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    auth.login('0771234567', 'pw').subscribe();
    http.expectOne(`${API}/api/identity/login`).flush(session());

    auth['http'].get(`${API}/api/identity/teachers`).subscribe({ error: () => undefined });
    http.expectOne(`${API}/api/identity/teachers`).flush(null, { status: 401, statusText: 'Unauthorized' });
    await vi.waitFor(() => http.expectOne(`${API}/api/identity/refresh`).flush(null, { status: 401, statusText: 'Unauthorized' }));

    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith('/login'));
    expect(auth.user()).toBeNull();
    expect(localStorage.getItem('eduvibe.session')).toBeNull();
  });

  it('clears the session on sign-out', async () => {
    const auth = setup();
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    auth.login('0771234567', 'pw').subscribe();
    http.expectOne(`${API}/api/identity/login`).flush(session());

    const done = auth.logout();
    http.expectOne(`${API}/api/identity/logout`).flush(null, { status: 204, statusText: 'No Content' });
    await done;

    expect(auth.user()).toBeNull();
    expect(auth.accessToken).toBeNull();
  });

  describe('language', () => {
    const signIn = (auth: AuthService, signedIn: Session) => {
      auth.login('0771234567', 'pw').subscribe();
      http.expectOne(`${API}/api/identity/login`).flush(signedIn);
    };

    it('shows the app in the language saved on the account', async () => {
      const auth = setup();
      const i18n = TestBed.inject(TranslateService);
      const use = vi.spyOn(i18n, 'use').mockResolvedValue();

      signIn(auth, session({ user: user({ language: 'si' }) }));

      expect(use).toHaveBeenCalledWith('si');
    });

    it('keeps a language picked on the sign-in screen and saves it on the account', () => {
      const auth = setup();
      const i18n = TestBed.inject(TranslateService);
      vi.spyOn(i18n, 'use').mockResolvedValue();
      void i18n.pickBeforeSignIn('si');

      signIn(auth, session({ user: user({ language: 'en' }) }));

      const save = http.expectOne(`${API}/api/identity/me/language`);
      expect(save.request.method).toBe('PUT');
      expect(save.request.body).toEqual({ language: 'si' });
      save.flush(user({ language: 'si' }));
      expect(auth.user()?.language).toBe('si');
      expect(auth.user()?.firstName).toBe('Nimal');
    });

    it('uses the account language when nothing was picked before signing in', () => {
      const auth = setup();
      const i18n = TestBed.inject(TranslateService);
      const use = vi.spyOn(i18n, 'use').mockResolvedValue();

      signIn(auth, session({ user: user({ language: 'en' }) }));

      expect(use).toHaveBeenCalledWith('en');
      http.expectNone(`${API}/api/identity/me/language`);
    });

    it('keeps the Super Admin in English, even when a language was picked, without remembering it for the device', () => {
      const auth = setup();
      const i18n = TestBed.inject(TranslateService);
      const use = vi.spyOn(i18n, 'use').mockResolvedValue();
      void i18n.pickBeforeSignIn('si');

      signIn(auth, session({ user: user({ roles: ['SuperAdmin'], teacherId: null, language: 'si' }) }));

      expect(use).toHaveBeenLastCalledWith('en', false);
      http.expectNone(`${API}/api/identity/me/language`);
    });
  });
});
