import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { Status } from './status';

describe('Status page', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Status],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue('http://api.test');
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
  });

  afterEach(() => http.verify());

  it('shows the server details once the API answers', async () => {
    const fixture = TestBed.createComponent(Status);
    fixture.detectChanges();

    http.expectOne('http://api.test/api/system/info').flush({
      name: 'EduVibe',
      version: '1.2.3',
      environment: 'Test',
      modules: ['identity', 'fees'],
    });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).toContain('status.ok');
    expect(text).toContain('1.2.3');
    expect(text).toContain('identity, fees');
  });

  it('offers a retry when the API cannot be reached', () => {
    const fixture = TestBed.createComponent(Status);
    fixture.detectChanges();

    http.expectOne('http://api.test/api/system/info').error(new ProgressEvent('error'));
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('status.error');
    expect(element.querySelector('button')).not.toBeNull();
  });
});
