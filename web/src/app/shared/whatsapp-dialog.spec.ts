import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { WhatsAppPreview, WhatsAppStatus } from '../core/api/whatsapp.service';
import { AppConfig } from '../core/config/app-config';
import { TranslateService } from '../core/i18n/translate.service';
import { ToastService } from '../core/ui/toast.service';
import { WhatsAppDialog, WhatsAppSpec } from './whatsapp-dialog';

const API = 'http://api.test';
const PATH = '/api/fees/classes/c1/whatsapp?month=2027-03';

const status = (over: Partial<WhatsAppStatus> = {}): WhatsAppStatus => ({
  enabled: true, configured: true, simulated: false, limit: 500, used: 12, remaining: 488, ...over,
});

const preview = (over: Partial<WhatsAppPreview> = {}): WhatsAppPreview => ({
  configured: true, total: 6, ready: 3, noNumber: 1, optedOut: 1, alreadySent: 1, overAllowance: 0, limit: 500, used: 12, noNumberNames: ['Nimal Perera'], ...over,
});

describe('WhatsAppDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  const setup = (first: WhatsAppStatus) => {
    const data: WhatsAppSpec = { path: PATH, title: 'whatsapp.fees_title', intro: 'whatsapp.fees_intro' };
    TestBed.configureTestingModule({
      imports: [WhatsAppDialog],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    vi.spyOn(TestBed.inject(TranslateService), 'format').mockImplementation((key, values) => `${key} ${JSON.stringify(values)}`);
    const fixture = TestBed.createComponent(WhatsAppDialog);
    fixture.detectChanges();
    http.expectOne(`${API}/api/whatsapp/status`).flush(first);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  };

  const sendButton = (el: HTMLElement) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent!.includes('whatsapp.send'))!;

  afterEach(() => {
    http.verify();
    close.mockReset();
  });

  it('shows a Free teacher that WhatsApp is locked and which plan has it, and offers no send', () => {
    const { fixture, el } = setup(status({ enabled: false, limit: 0, remaining: 0 }));
    http.expectOne(`${API}/api/plans/mine`).flush({
      planName: 'Free', endsOn: null, limits: [], features: [{ key: 'whatsapp', enabled: false, availableOn: 'Pro' }],
    });
    fixture.detectChanges();
    expect(el.textContent).toContain('whatsapp.locked_title');
    expect(el.textContent).toContain('Pro');
    expect(sendButton(el)).toBeUndefined();
  });

  it('says so when WhatsApp is not set up on the server, without asking for a preview', () => {
    const { el } = setup(status({ configured: false }));
    expect(el.textContent).toContain('whatsapp.unconfigured_title');
    expect(sendButton(el)).toBeUndefined();
  });

  it('shows who would be messaged and why the others are left out, then sends and closes', () => {
    const { fixture, el } = setup(status());
    http.expectOne(`${API}/api/fees/classes/c1/whatsapp?month=2027-03`).flush(preview());
    fixture.detectChanges();

    const text = el.textContent!;
    expect(text).toContain('whatsapp.will_send');
    expect(el.querySelector('.main dd')!.textContent).toBe('3');
    expect(text).toContain('whatsapp.already_sent');
    expect(text).toContain('whatsapp.no_number');
    expect(text).toContain('Nimal Perera');
    expect(text).toContain('whatsapp.opted_out');
    expect(text).not.toContain('whatsapp.over_allowance');
    expect(text).toContain('"used":12,"limit":500');

    const toast = vi.spyOn(TestBed.inject(ToastService), 'success');
    sendButton(el).click();
    http.expectOne({ method: 'POST', url: `${API}${PATH}` }).flush({ configured: true, queued: 3, noNumber: 1, optedOut: 1, alreadySent: 1, overAllowance: 0 });
    expect(toast).toHaveBeenCalledWith('whatsapp.queued');
    expect(close).toHaveBeenCalledWith(true);
  });

  it('warns when the allowance will not cover everyone and cannot send when nobody is ready', () => {
    const { fixture, el } = setup(status());
    http.expectOne(`${API}${PATH}`).flush(preview({ ready: 0, total: 2, noNumber: 0, optedOut: 0, alreadySent: 0, overAllowance: 2, noNumberNames: [] }));
    fixture.detectChanges();
    expect(el.querySelector('.warn')!.textContent).toContain('whatsapp.over_allowance');
    expect(sendButton(el).disabled).toBe(true);
  });

  it('shows a plan with no cap without a limit', () => {
    const { fixture, el } = setup(status({ limit: null, remaining: null }));
    http.expectOne(`${API}${PATH}`).flush(preview({ limit: null }));
    fixture.detectChanges();
    expect(el.textContent).toContain('whatsapp.allowance_unlimited');
  });

  it('lets the teacher try again when the preview fails', () => {
    const { fixture, el } = setup(status());
    http.expectOne(`${API}${PATH}`).flush({ code: 'month_invalid' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(el.querySelector('[role=alert]')!.textContent).toContain('error.month_invalid');
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent!.includes('status.retry'))!.click();
    http.expectOne(`${API}/api/whatsapp/status`).flush(status());
    http.expectOne(`${API}${PATH}`).flush(preview());
    fixture.detectChanges();
    expect(el.querySelector('.main dd')!.textContent).toBe('3');
  });

  it('goes back to "not set up" when the server turns out not to be configured at send time', () => {
    const { fixture, el } = setup(status());
    http.expectOne(`${API}${PATH}`).flush(preview());
    fixture.detectChanges();
    sendButton(el).click();
    http.expectOne({ method: 'POST', url: `${API}${PATH}` }).flush({ code: 'whatsapp_not_configured' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(el.textContent).toContain('whatsapp.unconfigured_title');
    expect(close).not.toHaveBeenCalled();
  });
});
