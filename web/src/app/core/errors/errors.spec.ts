import { HttpErrorResponse, provideHttpClient, withInterceptors, HttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { errorKey, inlineError } from '../api/problem';
import { ToastService } from '../ui/toast.service';
import { GlobalErrorHandler } from './global-error-handler';
import { httpErrorInterceptor } from './http-error.interceptor';

describe('HTTP error interceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  const toast = { error: vi.fn(), success: vi.fn() };

  beforeEach(() => {
    toast.error.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([httpErrorInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toast },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  const fail = (status: number, body: object | null) => {
    http.get('/x').subscribe({ error: () => undefined });
    backend.expectOne('/x').flush(body, { status, statusText: 'x' });
  };

  it('toasts a lost connection', () => {
    http.get('/x').subscribe({ error: () => undefined });
    backend.expectOne('/x').error(new ProgressEvent('error'));

    expect(toast.error).toHaveBeenCalledWith('error.network');
  });

  it('toasts server errors and missing permission', () => {
    fail(500, null);
    fail(403, null);

    expect(toast.error).toHaveBeenNthCalledWith(1, 'error.server');
    expect(toast.error).toHaveBeenNthCalledWith(2, 'error.forbidden');
  });

  it('leaves errors with a code for the screen to show beside the form', () => {
    fail(409, { code: 'phone_taken' });
    fail(401, null);

    expect(toast.error).not.toHaveBeenCalled();
  });
});

describe('error keys', () => {
  const response = (status: number, body: unknown) => new HttpErrorResponse({ status, error: body });

  it('uses the API code for the inline message', () => {
    expect(inlineError(response(409, { code: 'phone_taken' }))).toBe('error.phone_taken');
  });

  it('shows nothing inline for failures the interceptor already announced', () => {
    expect(inlineError(response(500, null))).toBe('');
    expect(inlineError(new Error('x'))).toBe('');
  });

  it('falls back to a general message', () => {
    expect(errorKey(response(429, null))).toBe('error.too_many_requests');
    expect(errorKey(new Error('x'))).toBe('error.unknown');
  });
});

describe('GlobalErrorHandler', () => {
  const toast = { error: vi.fn() };

  beforeEach(() => {
    toast.error.mockReset();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      providers: [GlobalErrorHandler, { provide: ToastService, useValue: toast }],
    });
  });

  it('logs and tells the person once, even when errors repeat', () => {
    const handler = TestBed.inject(GlobalErrorHandler);

    handler.handleError(new Error('one'));
    handler.handleError(new Error('two'));

    expect(console.error).toHaveBeenCalledTimes(2);
    expect(toast.error).toHaveBeenCalledTimes(1);
    expect(toast.error).toHaveBeenCalledWith('error.unexpected');
  });

  it('does not repeat HTTP failures the interceptor already announced', () => {
    TestBed.inject(GlobalErrorHandler).handleError(new HttpErrorResponse({ status: 500 }));

    expect(toast.error).not.toHaveBeenCalled();
  });
});
