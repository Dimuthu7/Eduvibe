import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { currentLanguage } from './language';
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

  afterEach(() => {
    http.verify();
    // The language on screen is shared by the whole test run, so every test leaves it as it found it.
    currentLanguage.set('en');
    document.documentElement.lang = 'en';
  });

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

  it('remembers the chosen language for the next visit and marks the page with it', async () => {
    await loadEnglish({ a: 'Hello' });

    const switching = service.use('si');
    http.expectOne('i18n/si.json').flush({});
    await switching;

    expect(localStorage.getItem('gurubook.language')).toBe('si');
    expect(document.documentElement.lang).toBe('si');
  });

  it('does not remember a language it was only told to show, such as the Super Admin always seeing English', async () => {
    await loadEnglish({ a: 'Hello' });

    await service.use('en', false);

    expect(localStorage.getItem('gurubook.language')).toBeNull();
  });

  it('starts in the language remembered on the device, and in English when that is not one it has', async () => {
    localStorage.setItem('gurubook.language', 'si');
    const loading = service.load();
    http.expectOne('i18n/en.json').flush({ a: 'Hello' });
    await new Promise((resolve) => setTimeout(resolve)); // the remembered language is asked for once English has arrived
    http.expectOne('i18n/si.json').flush({ a: 'ආයුබෝවන්' });
    await loading;
    expect(service.translate('a')).toBe('ආයුබෝවන්');

    localStorage.setItem('gurubook.language', 'fr');
    await service.load(); // English is already fetched
    expect(service.language()).toBe('en');
  });

  it('fetches a language file once, however often the person switches', async () => {
    await loadEnglish({ a: 'Hello' });

    const first = service.use('si');
    http.expectOne('i18n/si.json').flush({ a: 'ආයුබෝවන්' });
    await first;
    await service.use('en');
    await service.use('si');

    expect(service.translate('a')).toBe('ආයුබෝවන්');
  });

  it('keeps the last language picked when two taps come close together', async () => {
    await loadEnglish({ a: 'Hello' });

    const slow = service.use('si');
    const fast = service.use('en');
    http.expectOne('i18n/si.json').flush({ a: 'ආයුබෝවන්' });
    await Promise.all([slow, fast]);

    expect(service.language()).toBe('en');
    expect(service.translate('a')).toBe('Hello');
  });

  it('hands over a language picked before signing in once, then forgets it', async () => {
    await loadEnglish({ a: 'Hello' });

    const picking = service.pickBeforeSignIn('si');
    http.expectOne('i18n/si.json').flush({});
    await picking;

    expect(service.takePickedBeforeSignIn()).toBe('si');
    expect(service.takePickedBeforeSignIn()).toBeNull();
  });
});
