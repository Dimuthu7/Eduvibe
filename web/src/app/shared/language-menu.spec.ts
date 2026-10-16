import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { User } from '../core/auth/auth.models';
import { AuthService } from '../core/auth/auth.service';
import { AppConfig } from '../core/config/app-config';
import { TranslateService } from '../core/i18n/translate.service';
import { LanguageMenu } from './language-menu';

const API = 'http://api.test';
const teacher = { id: 'u', roles: ['Teacher'], language: 'en', firstName: 'N' } as User;

describe('LanguageMenu', () => {
  let http: HttpTestingController;

  const setup = (user: User | null) => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    TestBed.inject(AuthService).user.set(user);
    const i18n = TestBed.inject(TranslateService);
    vi.spyOn(i18n, 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(LanguageMenu);
    fixture.detectChanges();
    return { fixture, i18n };
  };

  const open = (fixture: { nativeElement: HTMLElement; detectChanges(): void }) => {
    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    return Array.from(document.querySelectorAll('.mat-mdc-menu-item')) as HTMLButtonElement[];
  };

  afterEach(() => {
    http.verify();
    document.body.querySelectorAll('.cdk-overlay-container').forEach((o) => (o.innerHTML = ''));
  });

  it('lists each language under its own name and ticks the one in use', () => {
    const { fixture } = setup(teacher);
    const items = open(fixture);

    expect(items.map((i) => i.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['checkEnglish', 'සිංහල']);
  });

  it('saves a signed-in person\'s choice on their account and shows it at once', () => {
    const { fixture, i18n } = setup(teacher);
    const use = vi.spyOn(i18n, 'use').mockResolvedValue();
    open(fixture)[1].click();

    expect(use).toHaveBeenCalledWith('si');
    const save = http.expectOne(`${API}/api/identity/me/language`);
    expect(save.request.body).toEqual({ language: 'si' });
    save.flush({ ...teacher, language: 'si' });
  });

  it('keeps the choice of someone not signed in for their sign-in', () => {
    const { fixture, i18n } = setup(null);
    const pick = vi.spyOn(i18n, 'pickBeforeSignIn').mockResolvedValue();
    open(fixture)[1].click();

    expect(pick).toHaveBeenCalledWith('si');
    http.expectNone(`${API}/api/identity/me/language`);
  });

  it('does nothing when the language already in use is chosen again', () => {
    const { fixture, i18n } = setup(teacher);
    const use = vi.spyOn(i18n, 'use').mockResolvedValue();
    open(fixture)[0].click();

    expect(use).not.toHaveBeenCalled();
  });

  it('is not shown to the Super Admin, who always sees English', () => {
    const { fixture } = setup({ ...teacher, roles: ['SuperAdmin'] });

    expect(fixture.nativeElement.querySelector('button')).toBeNull();
  });
});
