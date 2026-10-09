import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AppConfig } from '../config/app-config';
import { Session, User } from './auth.models';
import { AuthService } from './auth.service';
import { authInterceptor } from './auth.interceptor';

const API = 'http://api.test';

const user = (overrides: Partial<User> = {}): User => ({
  id: 'u1',
  phone: '+94771234567',
  fullName: 'Nimal',
  email: null,
  language: 'en',
  roles: ['Teacher'],
  mustChangePassword: false,
  teacherId: 't1',
  town: null,
  subjects: null,
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
    expect(request.request.body).toEqual({ phone: '0771234567', password: 'secret' });
    request.flush(session());

    expect(auth.user()?.fullName).toBe('Nimal');
    expect(auth.accessToken).toBe('access-1');
    expect(localStorage.getItem('eduvibe.session')).toContain('refresh-1');
  });

  it('sends a one-time password user to change it first', () => {
    const auth = setup();
    auth.login('0771234567', 'otp').subscribe();
    http.expectOne(`${API}/api/identity/login`).flush(session({ user: user({ mustChangePassword: true }) }));

    expect(auth.homeRoute()).toBe('/change-password');
  });

  it('starts a Super Admin on the teachers screen', () => {
    const auth = setup();
    auth.login('0771234567', 'pw').subscribe();
    http.expectOne(`${API}/api/identity/login`).flush(session({ user: user({ roles: ['SuperAdmin'], teacherId: null }) }));

    expect(auth.homeRoute()).toBe('/admin/teachers');
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
});
