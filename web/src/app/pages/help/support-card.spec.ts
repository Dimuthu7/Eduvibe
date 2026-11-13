import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { SupportScreen, Ticket } from '../../core/api/support.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { ToastService } from '../../core/ui/toast.service';
import { SupportCard } from './support-card';

const API = 'http://api.test';

const ticket = (over: Partial<Ticket> = {}): Ticket => ({
  id: 't1',
  kind: 'problem',
  details: 'Marks do not save',
  screen: '/exams',
  status: 'Open',
  hasShot: false,
  at: '2026-11-13T04:00:00Z',
  answeredAt: null,
  replies: [],
  ...over,
});

const screen = (over: Partial<SupportScreen> = {}): SupportScreen => ({
  phone: '+94712223344',
  hours: '9 AM to 6 PM',
  tickets: [ticket()],
  ...over,
});

describe('SupportCard', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<SupportCard>;
  const toast = { success: vi.fn(), error: vi.fn() };
  const dialog = { open: vi.fn() };

  const setup = () => {
    toast.success.mockReset();
    dialog.open.mockReset();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toast },
        { provide: MatDialog, useValue: dialog },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    fixture = TestBed.createComponent(SupportCard);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const button = (root: HTMLElement, text: string) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent!.includes(text));

  afterEach(() => http.verify());

  it('shows the number to call and what has been reported', () => {
    const root = setup();
    http.expectOne(`${API}/api/support`).flush(screen());
    fixture.detectChanges();

    expect(root.textContent).toContain('+94712223344');
    expect(root.textContent).toContain('9 AM to 6 PM');
    expect(root.querySelector<HTMLAnchorElement>('a[href^="tel:"]')!.href).toBe('tel:+94712223344');
    expect(root.textContent).toContain('support.kind.problem');
    expect(root.textContent).toContain('support.status.Open');
  });

  it('tells a teacher without the feature which plan it is on, and offers no report button', () => {
    const root = setup();
    http.expectOne(`${API}/api/support`).flush({ code: 'plan_feature_locked' }, { status: 403, statusText: 'Forbidden' });
    http.expectOne(`${API}/api/plans/mine`).flush({
      planName: 'Free',
      endsOn: null,
      features: [{ key: 'priority_support', enabled: false, availableOn: 'Pro' }],
      limits: [],
    });
    fixture.detectChanges();

    expect(root.textContent).toContain('support.locked');
    expect(root.textContent).toContain('Pro');
    expect(button(root, 'support.report')).toBeUndefined();
  });

  it('writes back on a ticket and shows what support said', async () => {
    const root = setup();
    http.expectOne(`${API}/api/support`).flush(screen({ tickets: [ticket({ replies: [{ id: 'r1', body: 'Looking into it.', fromSupport: true, at: '2026-11-13T05:00:00Z' }] })] }));
    fixture.detectChanges();

    root.querySelector<HTMLElement>('mat-expansion-panel-header')!.click();
    fixture.detectChanges();
    expect(root.textContent).toContain('Looking into it.');

    const box = root.querySelector('textarea')!;
    box.value = 'It is still wrong';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    button(root, 'support.send')!.click();
    const sent = http.expectOne(`${API}/api/support/tickets/t1/replies`);
    expect(sent.request.body).toEqual({ body: 'It is still wrong' });
    sent.flush(ticket({ status: 'Open', replies: [{ id: 'r2', body: 'It is still wrong', fromSupport: false, at: '2026-11-13T06:00:00Z' }] }));
    fixture.detectChanges();

    expect(root.textContent).toContain('It is still wrong');
    // ngModel writes the cleared draft back to the box in a microtask.
    await fixture.whenStable();
    expect(root.querySelector('textarea')!.value).toBe('');
  });

  it('reloads and says so once a report has gone', () => {
    const root = setup();
    http.expectOne(`${API}/api/support`).flush(screen({ tickets: [] }));
    fixture.detectChanges();
    expect(root.textContent).toContain('support.none');

    dialog.open.mockReturnValue({ afterClosed: () => of(ticket()) });
    button(root, 'support.report')!.click();
    http.expectOne(`${API}/api/support`).flush(screen());
    fixture.detectChanges();

    expect(toast.success).toHaveBeenCalledWith('support.sent');
    expect(root.textContent).toContain('support.kind.problem');
  });

  it('fetches a screenshot only when its ticket is opened', () => {
    const root = setup();
    http.expectOne(`${API}/api/support`).flush(screen({ tickets: [ticket({ hasShot: true })] }));
    fixture.detectChanges();
    http.expectNone(`${API}/api/support/tickets/t1/shot`);

    root.querySelector<HTMLElement>('mat-expansion-panel-header')!.click();
    fixture.detectChanges();
    http.expectOne(`${API}/api/support/tickets/t1/shot`).flush(new Blob(['x'], { type: 'image/jpeg' }));
    fixture.detectChanges();

    expect(root.querySelector('img.shot')).not.toBeNull();
  });
});
