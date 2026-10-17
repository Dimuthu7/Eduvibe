import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { Home } from './home';

const API = 'http://api.test';

describe('Home dashboard', () => {
  let http: HttpTestingController;

  const setup = (layout: { widgets: { key: string; enabled: boolean }[]; customised: boolean }) => {
    TestBed.configureTestingModule({ imports: [Home], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    vi.spyOn(TestBed.inject(AuthService), 'user').mockReturnValue({ firstName: 'Sunil' } as never);
    const fixture = TestBed.createComponent(Home);
    fixture.detectChanges();
    http.expectOne(`${API}/api/dashboard/layout`).flush(layout);
    fixture.detectChanges();
    return fixture;
  };

  // Answers every widget request with an empty result, and says which endpoints were asked.
  const answerWidgets = (): string[] => {
    const asked: string[] = [];
    for (const req of http.match((r) => r.url !== `${API}/api/dashboard/layout`)) {
      asked.push(req.request.url.replace(API, ''));
      respond(req);
    }
    return asked;
  };

  const respond = (req: TestRequest) => {
    const url = req.request.url;
    if (url.endsWith('/api/exams/progress')) req.flush({ dropPoints: 10, classes: [], droppedTotal: 0, dropped: [] });
    else if (url.endsWith('/api/attendance/watch')) req.flush({ windowDays: 30, total: 0, rows: [] });
    else if (url.endsWith('/api/fees/overview')) req.flush({ month: '2026-10', totals: { paid: 0, partlyPaid: 0, unpaid: 0, freeCard: 0, due: 0, collected: 0, outstanding: 0 }, classes: [] });
    else req.flush([]);
  };

  // Avatars ask once which students have a photo; none do here.
  const settlePhotos = () => http.match((r) => r.url.endsWith('/api/students/photos')).forEach((r) => r.flush([]));

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  const titles = (fixture: { nativeElement: HTMLElement }) =>
    Array.from(fixture.nativeElement.querySelectorAll('app-widget-card h2')).map((h) => h.textContent?.trim());

  it('shows the default widgets in the default order, with institutes off', () => {
    const fixture = setup({ widgets: [], customised: false });
    const asked = answerWidgets();
    fixture.detectChanges();
    expect(titles(fixture)).toEqual([
      'dashboard.today.title', 'dashboard.joins.title', 'dashboard.fees.title', 'dashboard.attendance.title', 'dashboard.exams.title',
    ]);
    expect(asked.some((u) => u.includes('my-institutes'))).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('Sunil');
  });

  it('follows the saved order and asks for nothing from a widget that is off', () => {
    const fixture = setup({
      widgets: [
        { key: 'fees', enabled: true },
        { key: 'today', enabled: true },
        { key: 'exams', enabled: false },
        { key: 'institutes', enabled: true },
      ],
      customised: true,
    });
    const asked = answerWidgets();
    fixture.detectChanges();
    // Saved keys come first; widgets never saved (joins, attendance) join at the end with their defaults.
    expect(titles(fixture)).toEqual([
      'dashboard.fees.title', 'dashboard.today.title', 'dashboard.institutes.title', 'dashboard.joins.title', 'dashboard.attendance.title',
    ]);
    expect(asked.some((u) => u.includes('/api/exams/progress'))).toBe(false);
    expect(asked.some((u) => u.includes('my-institutes'))).toBe(true);
  });

  it('shows what to do when every widget is off', () => {
    const fixture = setup({
      widgets: ['today', 'joins', 'fees', 'attendance', 'exams', 'institutes'].map((key) => ({ key, enabled: false })),
      customised: true,
    });
    expect(http.match(() => true)).toHaveLength(0);
    expect(titles(fixture)).toEqual([]);
    expect(fixture.nativeElement.querySelector('.empty')).not.toBeNull();
  });

  it('saves a changed layout from the customise screen and shows it straight away', () => {
    const fixture = setup({ widgets: [], customised: false });
    answerWidgets();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    (Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('dashboard.customise.button')) as HTMLButtonElement).click();
    fixture.detectChanges();

    const rows = el.querySelectorAll('app-customise .panel-row');
    expect(rows).toHaveLength(6);
    // Move "Unpaid fees" to the top and switch exams off.
    (rows[2].querySelectorAll('.move button')[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    (rows[2].querySelectorAll('.move button')[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    (el.querySelectorAll('app-customise .panel-row')[4].querySelector('mat-slide-toggle button') as HTMLButtonElement).click();
    fixture.detectChanges();
    (el.querySelector('app-customise form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const save = http.expectOne(`${API}/api/dashboard/layout`);
    expect(save.request.method).toBe('PUT');
    expect(save.request.body.widgets.map((w: { key: string; enabled: boolean }) => `${w.key}:${w.enabled}`)).toEqual([
      'fees:true', 'today:true', 'joins:true', 'attendance:true', 'exams:false', 'institutes:false',
    ]);
    save.flush({ widgets: save.request.body.widgets, customised: true });
    fixture.detectChanges();
    answerWidgets();
    fixture.detectChanges();
    expect(el.querySelector('app-customise')).toBeNull();
    expect(titles(fixture)).toEqual(['dashboard.fees.title', 'dashboard.today.title', 'dashboard.joins.title', 'dashboard.attendance.title']);
  });

  it('clears the saved layout instead of saving one that is just the default', () => {
    const fixture = setup({ widgets: [{ key: 'fees', enabled: true }], customised: true });
    answerWidgets();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    (Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('dashboard.customise.button')) as HTMLButtonElement).click();
    fixture.detectChanges();
    (Array.from(el.querySelectorAll('app-customise button')).find((b) => b.textContent?.includes('dashboard.customise.reset')) as HTMLButtonElement).click();
    fixture.detectChanges();
    (el.querySelector('app-customise form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const reset = http.expectOne(`${API}/api/dashboard/layout`);
    expect(reset.request.method).toBe('DELETE');
    reset.flush({ widgets: [], customised: false });
    fixture.detectChanges();
    answerWidgets();
    fixture.detectChanges();
    expect(titles(fixture)[0]).toBe('dashboard.today.title');
  });

  it('cancel drops the changes', () => {
    const fixture = setup({ widgets: [], customised: false });
    answerWidgets();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    (Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('dashboard.customise.button')) as HTMLButtonElement).click();
    fixture.detectChanges();
    (Array.from(el.querySelectorAll('app-customise button')).find((b) => b.textContent?.includes('common.cancel')) as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('app-customise')).toBeNull();
    http.expectNone(`${API}/api/dashboard/layout`);
    answerWidgets();
  });
});
