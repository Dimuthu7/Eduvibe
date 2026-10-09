import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TranslateService } from './translate.service';

describe('TranslateService', () => {
  let service: TranslateService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(TranslateService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function loadEnglish(messages: Record<string, string>) {
    const loading = service.load();
    http.expectOne('i18n/en.json').flush(messages);
    await loading;
  }

  it('returns the English text for a known key', async () => {
    await loadEnglish({ 'login.title': 'Sign in' });

    expect(service.translate('login.title')).toBe('Sign in');
  });

  it('returns the key itself when no language has the text', async () => {
    await loadEnglish({});

    expect(service.translate('missing.key')).toBe('missing.key');
  });

  it('uses the chosen language and falls back to English for missing keys', async () => {
    await loadEnglish({ a: 'Hello', b: 'Bye' });

    const switching = service.use('si');
    http.expectOne('i18n/si.json').flush({ a: 'ආයුබෝවන්' });
    await switching;

    expect(service.translate('a')).toBe('ආයුබෝවන්');
    expect(service.translate('b')).toBe('Bye');
    expect(service.language()).toBe('si');
  });

  it('remembers the chosen language for the next visit', async () => {
    await loadEnglish({ a: 'Hello' });

    const switching = service.use('ta');
    http.expectOne('i18n/ta.json').flush({});
    await switching;

    expect(localStorage.getItem('eduvibe.language')).toBe('ta');
  });
});
