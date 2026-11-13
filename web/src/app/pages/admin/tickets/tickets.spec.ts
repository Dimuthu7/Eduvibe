import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminTicket } from '../../../core/api/support.service';
import { AppConfig } from '../../../core/config/app-config';
import { TranslateService } from '../../../core/i18n/translate.service';
import { ToastService } from '../../../core/ui/toast.service';
import { AdminTickets } from './tickets';

const API = 'http://api.test';

const ticket = (over: Partial<AdminTicket> = {}): AdminTicket => ({
  id: 't1',
  teacherId: 'teacher-1',
  teacherName: 'Sunil Perera',
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

describe('AdminTickets', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<AdminTickets>;
  const toast = { success: vi.fn(), error: vi.fn() };

  const setup = async (rows: AdminTicket[] = [ticket()]) => {
    toast.success.mockReset();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ToastService, useValue: toast }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    fixture = TestBed.createComponent(AdminTickets);
    fixture.detectChanges();
    http.expectOne(`${API}/api/support/admin/tickets`).flush(rows);
    http.expectOne(`${API}/api/support/admin/settings`).flush({ phone: '+94712223344', hours: '9 AM to 6 PM' });
    // ngModel writes the loaded number into its box in a microtask.
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const button = (root: HTMLElement, text: string) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent!.includes(text));

  afterEach(() => http.verify());

  it('lists every teacher ticket with who raised it', async () => {
    const root = await setup();

    expect(root.textContent).toContain('Sunil Perera');
    expect(root.textContent).toContain('support.kind.problem');
    expect(root.textContent).toContain('support.status.Open');
    expect(root.querySelector<HTMLInputElement>('input[name="phone"]')!.value).toBe('+94712223344');
  });

  it('writes back and tells the Super Admin it went', async () => {
    const root = await setup();
    root.querySelector<HTMLElement>('mat-expansion-panel-header')!.click();
    fixture.detectChanges();
    expect(root.textContent).toContain('Marks do not save');
    expect(root.textContent).toContain('/exams');

    const box = root.querySelector('textarea')!;
    box.value = 'Fixed today';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    button(root, 'tickets.send')!.click();
    const sent = http.expectOne(`${API}/api/support/admin/tickets/t1/replies`);
    expect(sent.request.body).toEqual({ body: 'Fixed today' });
    sent.flush(ticket({ status: 'InProgress', replies: [{ id: 'r1', body: 'Fixed today', fromSupport: true, at: '2026-11-13T05:00:00Z' }] }));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(toast.success).toHaveBeenCalledWith('tickets.answered');
    expect(root.textContent).toContain('Fixed today');
    expect(root.textContent).toContain('support.status.InProgress');
  });

  it('moves a ticket along and offers only the other statuses', async () => {
    const root = await setup();
    root.querySelector<HTMLElement>('mat-expansion-panel-header')!.click();
    fixture.detectChanges();
    expect(button(root, 'tickets.mark.Open')).toBeUndefined();

    button(root, 'tickets.mark.Resolved')!.click();
    const put = http.expectOne(`${API}/api/support/admin/tickets/t1/status`);
    expect(put.request.body).toEqual({ status: 'Resolved' });
    put.flush(ticket({ status: 'Resolved' }));
    fixture.detectChanges();

    expect(root.textContent).toContain('support.status.Resolved');
    expect(button(root, 'tickets.mark.Resolved')).toBeUndefined();
  });

  it('narrows the list to one status, and drops a ticket that leaves it', async () => {
    const root = await setup();
    Array.from(root.querySelectorAll<HTMLButtonElement>('mat-button-toggle button'))
      .find((b) => b.textContent!.includes('support.status.Open'))!
      .click();
    fixture.detectChanges();
    const narrowed = http.expectOne((r) => r.url === `${API}/api/support/admin/tickets` && r.params.get('status') === 'Open');
    narrowed.flush([ticket()]);
    fixture.detectChanges();

    root.querySelector<HTMLElement>('mat-expansion-panel-header')!.click();
    fixture.detectChanges();
    button(root, 'tickets.mark.Resolved')!.click();
    http.expectOne(`${API}/api/support/admin/tickets/t1/status`).flush(ticket({ status: 'Resolved' }));
    fixture.detectChanges();

    expect(root.textContent).toContain('tickets.none');
  });

  it('saves the number teachers are shown', async () => {
    const root = await setup();
    const phone = root.querySelector<HTMLInputElement>('input[name="phone"]')!;
    phone.value = '0765554433';
    phone.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    const put = http.expectOne(`${API}/api/support/admin/settings`);
    expect(put.request.body).toEqual({ phone: '0765554433', hours: '9 AM to 6 PM' });
    put.flush({ phone: '+94765554433', hours: '9 AM to 6 PM' });
    fixture.detectChanges();

    expect(toast.success).toHaveBeenCalledWith('tickets.settings_saved');
  });
});
