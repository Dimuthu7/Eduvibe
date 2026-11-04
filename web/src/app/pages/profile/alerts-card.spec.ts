import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AppConfig } from '../../core/config/app-config';
import { AuthService } from '../../core/auth/auth.service';
import { TranslateService } from '../../core/i18n/translate.service';
import { ToastService } from '../../core/ui/toast.service';
import { AlertsCard } from './alerts-card';

const API = 'http://api.test';

const child = (studentId: string, fullName: string) => ({ studentId, firstName: fullName.split(' ')[0], fullName, classes: [] });

const plan = (parentPortal: boolean) => ({
  name: 'Pro',
  features: [{ key: 'parent_portal', enabled: parentPortal, availableOn: null }],
  limits: [],
});

describe('AlertsCard', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<AlertsCard>;
  const toast = { success: vi.fn(), error: vi.fn() };

  const setup = (user: { roles: string[]; teacherId?: string }) => {
    toast.success.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toast },
        {
          provide: AuthService,
          useValue: { user: signal({ id: 'u1', ...user }), hasRole: (role: string) => user.roles.includes(role) },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    fixture = TestBed.createComponent(AlertsCard);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const toggles = (root: HTMLElement) => Array.from(root.querySelectorAll<HTMLButtonElement>('mat-slide-toggle button'));

  afterEach(() => http.verify());

  it('shows a parent each child with payments, attendance and results, and what they muted', () => {
    const root = setup({ roles: ['Parent'] });
    http.expectOne(`${API}/api/students/family`).flush([child('s1', 'Amara Silva'), child('s2', 'Kavindu Silva')]);
    http.expectOne(`${API}/api/notifications/alerts`).flush({ muted: [{ studentId: 's1', topic: 'attendance' }] });
    fixture.detectChanges();

    expect(root.textContent).toContain('Amara Silva');
    expect(root.textContent).toContain('Kavindu Silva');
    const switches = toggles(root);
    expect(switches).toHaveLength(6);
    expect(switches.map((s) => s.getAttribute('aria-checked'))).toEqual(['true', 'false', 'true', 'true', 'true', 'true']);
  });

  it('switches one topic off for one child and says so', () => {
    const root = setup({ roles: ['Parent'] });
    http.expectOne(`${API}/api/students/family`).flush([child('s1', 'Amara Silva')]);
    http.expectOne(`${API}/api/notifications/alerts`).flush({ muted: [] });
    fixture.detectChanges();

    toggles(root)[0].click();
    const saved = http.expectOne(`${API}/api/notifications/alerts`);
    expect(saved.request.method).toBe('PUT');
    expect(saved.request.body).toEqual({ studentId: 's1', topic: 'payments', on: false });
    saved.flush({ muted: [{ studentId: 's1', topic: 'payments' }] });
    fixture.detectChanges();

    expect(toast.success).toHaveBeenCalledWith('alerts.turned_off');
    expect(toggles(root)[0].getAttribute('aria-checked')).toBe('false');
  });

  it('lets a teacher on the parent portal choose how much of attendance goes out', () => {
    const root = setup({ roles: ['Teacher'], teacherId: 't1' });
    http.expectOne(`${API}/api/plans/mine`).flush(plan(true));
    http.expectOne(`${API}/api/notifications/parent-alerts`).flush({ attendanceAlerts: 'all' });
    fixture.detectChanges();

    expect(root.textContent).toContain('alerts.parents_title');
    const choices = Array.from(root.querySelectorAll<HTMLButtonElement>('mat-button-toggle button'));
    expect(choices).toHaveLength(3);
    choices[1].click();
    const saved = http.expectOne(`${API}/api/notifications/parent-alerts`);
    expect(saved.request.method).toBe('PUT');
    expect(saved.request.body).toEqual({ attendanceAlerts: 'absences' });
    saved.flush({ attendanceAlerts: 'absences' });
    fixture.detectChanges();
    expect(toast.success).toHaveBeenCalledWith('alerts.saved');
  });

  it('says nothing to a teacher whose plan has no parent portal, and asks for no settings', () => {
    const root = setup({ roles: ['Teacher'], teacherId: 't1' });
    http.expectOne(`${API}/api/plans/mine`).flush(plan(false));
    fixture.detectChanges();
    expect(root.textContent?.trim()).toBe('');
  });

  it('shows nothing to a student', () => {
    const root = setup({ roles: ['Student'] });
    fixture.detectChanges();
    expect(root.textContent?.trim()).toBe('');
  });
});
