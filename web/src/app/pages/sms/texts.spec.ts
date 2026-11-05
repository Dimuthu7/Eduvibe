import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SmsList, SmsMessage } from '../../core/api/sms.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { ToastService } from '../../core/ui/toast.service';
import { SmsTextsPage } from './texts';

const API = 'http://api.test';

const text = (id: string, over: Partial<SmsMessage> = {}): SmsMessage => ({
  id,
  studentName: `Student ${id}`,
  kind: 'fee_reminder',
  status: 'Sent',
  error: null,
  phone: '•••• 4567',
  text: "Kamal's Maths fee for March 2027 is LKR 2,000, still to be paid. - Mr Perera, GuruBook",
  parts: 1,
  fallback: false,
  stopped: false,
  createdAt: '2027-03-15T04:00:00Z',
  ...over,
});

const list = (items: SmsMessage[], over: Partial<SmsList['status']> = {}): SmsList => ({
  status: {
    enabled: true, configured: true, simulated: false, limit: 300, used: items.length, remaining: 300 - items.length,
    channel: 'whatsapp_then_sms', ...over,
  },
  items,
});

describe('SmsTextsPage', () => {
  let http: HttpTestingController;
  const toast = { success: vi.fn(), error: vi.fn() };

  const setup = (reply: SmsList | 'locked') => {
    toast.success.mockReset();
    TestBed.configureTestingModule({
      imports: [SmsTextsPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ToastService, useValue: toast }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    vi.spyOn(TestBed.inject(TranslateService), 'format').mockImplementation((key, values) => `${key} ${JSON.stringify(values)}`);
    const fixture = TestBed.createComponent(SmsTextsPage);
    fixture.detectChanges();
    const request = http.expectOne(`${API}/api/sms/messages`);
    if (reply === 'locked') {
      request.flush({ code: 'plan_feature_locked' }, { status: 403, statusText: 'Forbidden' });
      http.expectOne(`${API}/api/plans/mine`).flush({
        planName: 'Free', endsOn: null, limits: [], features: [{ key: 'sms', enabled: false, availableOn: 'Pro' }],
      });
    } else {
      request.flush(reply);
    }
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  };

  afterEach(() => http.verify());

  it('shows each text with its words, what it cost and why it failed', () => {
    const { el } = setup(list([
      text('1'),
      text('2', { kind: 'absence_alert', parts: 2, fallback: true }),
      text('3', { kind: 'results', status: 'Failed', error: 'no_credit' }),
    ]));

    const rows = el.querySelectorAll('.panel-row');
    expect(rows.length).toBe(3);
    expect(rows[0].textContent).toContain('whatsapp.kind_fee_reminder');
    expect(rows[0].textContent).toContain('•••• 4567');
    expect(rows[0].textContent).toContain('still to be paid');
    expect(rows[1].textContent).toContain('sms.parts {"parts":2}');
    expect(rows[1].textContent).toContain('sms.after_whatsapp');
    expect(rows[2].textContent).toContain('sms.error_no_credit');
    // The allowance counts parts, not texts.
    expect(el.textContent).toContain('"used":3,"limit":300');
  });

  it('saves the channel the teacher picks', () => {
    const { el, fixture } = setup(list([text('1')]));

    const choices = Array.from(el.querySelectorAll<HTMLButtonElement>('mat-button-toggle button'));
    expect(choices).toHaveLength(2);
    choices[1].click();
    const saved = http.expectOne(`${API}/api/sms/channel`);
    expect(saved.request.method).toBe('PUT');
    expect(saved.request.body).toEqual({ channel: 'sms_only' });
    saved.flush({ channel: 'sms_only' });
    fixture.detectChanges();

    expect(toast.success).toHaveBeenCalledWith('sms.channel_saved');
    expect(choices[1].getAttribute('aria-checked')).toBe('true');
  });

  it('stops texts to a number for every text that number got', () => {
    const { el, fixture } = setup(list([text('1'), text('2'), text('3', { phone: '•••• 9999' })]));

    el.querySelectorAll<HTMLButtonElement>('.panel-row .stop')[0].click();
    const saved = http.expectOne(`${API}/api/sms/messages/1/stop`);
    expect(saved.request.body).toEqual({ on: false });
    saved.flush({ on: false });
    fixture.detectChanges();

    const rows = el.querySelectorAll('.panel-row');
    expect(rows[0].textContent).toContain('sms.stopped');
    expect(rows[1].textContent).toContain('sms.stopped');
    expect(rows[2].textContent).not.toContain('sms.stopped');
    expect(toast.success).toHaveBeenCalledWith('sms.stopped_saved');
  });

  it('tells a free teacher which plan has it, and asks for nothing else', () => {
    const { el } = setup('locked');
    expect(el.textContent).toContain('sms.locked_title');
    expect(el.textContent).toContain('Pro');
    expect(el.querySelectorAll('.panel-row')).toHaveLength(0);
  });

  it('says so when the server cannot send texts at all', () => {
    const { el } = setup(list([], { configured: false }));
    expect(el.textContent).toContain('sms.unconfigured_title');
  });
});
