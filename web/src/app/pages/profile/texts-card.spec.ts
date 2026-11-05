import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { ToastService } from '../../core/ui/toast.service';
import { TextsCard } from './texts-card';

const API = 'http://api.test';

describe('TextsCard', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<TextsCard>;
  const toast = { success: vi.fn(), error: vi.fn() };

  const setup = (roles: string[]) => {
    toast.success.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toast },
        {
          provide: AuthService,
          useValue: { user: signal({ id: 'u1', roles }), hasRole: (role: string) => roles.includes(role) },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    fixture = TestBed.createComponent(TextsCard);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const toggle = (root: HTMLElement) => root.querySelector<HTMLButtonElement>('mat-slide-toggle button');

  afterEach(() => http.verify());

  it('lets a parent stop texts to their own phone', () => {
    const root = setup(['Parent']);
    http.expectOne(`${API}/api/sms/my-texts`).flush({ on: true });
    fixture.detectChanges();

    expect(toggle(root)?.getAttribute('aria-checked')).toBe('true');
    toggle(root)!.click();
    const saved = http.expectOne(`${API}/api/sms/my-texts`);
    expect(saved.request.method).toBe('PUT');
    expect(saved.request.body).toEqual({ on: false });
    saved.flush({ on: false });
    fixture.detectChanges();

    expect(toast.success).toHaveBeenCalledWith('sms.my_texts_off');
    expect(toggle(root)?.getAttribute('aria-checked')).toBe('false');
  });

  it('shows a student the switch too, already off when they turned it off', () => {
    const root = setup(['Student']);
    http.expectOne(`${API}/api/sms/my-texts`).flush({ on: false });
    fixture.detectChanges();

    expect(toggle(root)?.getAttribute('aria-checked')).toBe('false');
  });

  it('shows a teacher nothing, and asks for nothing', () => {
    const root = setup(['Teacher']);
    fixture.detectChanges();
    expect(root.textContent?.trim()).toBe('');
  });

  it('stays away when the server cannot say', () => {
    const root = setup(['Parent']);
    http.expectOne(`${API}/api/sms/my-texts`).flush({ code: 'no_phone' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(root.textContent?.trim()).toBe('');
  });
});
