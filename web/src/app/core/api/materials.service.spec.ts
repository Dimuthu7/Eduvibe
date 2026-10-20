import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AppConfig } from '../config/app-config';
import { errorCode } from './problem';
import { MaterialsService } from './materials.service';

const API = 'http://api.test';

describe('MaterialsService', () => {
  let http: HttpTestingController;
  let service: MaterialsService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    service = TestBed.inject(MaterialsService);
  });

  afterEach(() => http.verify());

  it('asks for a download only when told to', () => {
    service.file('m1', true).subscribe();
    const download = http.expectOne((r) => r.url === `${API}/api/materials/m1/file`);
    expect(download.request.params.get('download')).toBe('true');
    expect(download.request.responseType).toBe('blob');
    download.flush(new Blob(['x']));

    service.myFile('m2', false).subscribe();
    const view = http.expectOne((r) => r.url === `${API}/api/materials/mine/m2/file`);
    expect(view.request.params.has('download')).toBe(false);
    view.flush(new Blob(['x']));
  });

  it('keeps the error code readable when a file request fails, though the failure body arrives as a blob', async () => {
    let failure: unknown;
    service.file('m1', false).subscribe({ error: (e) => (failure = e) });
    http.expectOne((r) => r.url === `${API}/api/materials/m1/file`).flush(new Blob([JSON.stringify({ code: 'drive_reconnect' })]), { status: 409, statusText: 'Conflict' });
    await vi.waitFor(() => expect(failure).toBeInstanceOf(HttpErrorResponse));
    expect(errorCode(failure)).toBe('drive_reconnect');
    expect((failure as HttpErrorResponse).status).toBe(409);
  });

  it('passes on a failure that has no readable body', async () => {
    let failure: unknown;
    service.file('m1', false).subscribe({ error: (e) => (failure = e) });
    http.expectOne((r) => r.url === `${API}/api/materials/m1/file`).flush(new Blob(['<html>bad gateway</html>']), { status: 502, statusText: 'Bad Gateway' });
    await vi.waitFor(() => expect(failure).toBeInstanceOf(HttpErrorResponse));
    expect(errorCode(failure)).toBeNull();
    expect((failure as HttpErrorResponse).status).toBe(502);
  });
});
