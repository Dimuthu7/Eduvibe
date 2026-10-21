import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { WhatsAppList, WhatsAppMessage } from '../../core/api/whatsapp.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { WhatsAppMessages } from './messages';

const API = 'http://api.test';

const message = (id: string, over: Partial<WhatsAppMessage> = {}): WhatsAppMessage => ({
  id, studentName: `Student ${id}`, kind: 'fee_reminder', status: 'Sent', error: null, phone: '•••• 4567', createdAt: '2027-03-15T04:00:00Z', ...over,
});

const list = (items: WhatsAppMessage[], over: Partial<WhatsAppList['status']> = {}): WhatsAppList => ({
  status: { enabled: true, configured: true, simulated: false, limit: 500, used: items.length, remaining: 500 - items.length, ...over },
  items,
});

describe('WhatsAppMessages', () => {
  let http: HttpTestingController;

  const setup = (reply: WhatsAppList | 'locked') => {
    TestBed.configureTestingModule({ imports: [WhatsAppMessages], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    vi.spyOn(TestBed.inject(TranslateService), 'format').mockImplementation((key, values) => `${key} ${JSON.stringify(values)}`);
    const fixture = TestBed.createComponent(WhatsAppMessages);
    fixture.detectChanges();
    const request = http.expectOne(`${API}/api/whatsapp/messages`);
    if (reply === 'locked') {
      request.flush({ code: 'plan_feature_locked' }, { status: 403, statusText: 'Forbidden' });
      http.expectOne(`${API}/api/plans/mine`).flush({
        planName: 'Free', endsOn: null, limits: [], features: [{ key: 'whatsapp', enabled: false, availableOn: 'Pro' }],
      });
    } else {
      request.flush(reply);
    }
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  };

  afterEach(() => http.verify());

  it('shows each message with its kind, status and why it failed', () => {
    const { el } = setup(list([
      message('1'),
      message('2', { kind: 'absence_alert', status: 'Delivered' }),
      message('3', { kind: 'results', status: 'Failed', error: 'not_on_whatsapp' }),
    ]));
    const rows = el.querySelectorAll('.panel-row');
    expect(rows.length).toBe(3);
    expect(rows[0].textContent).toContain('whatsapp.kind_fee_reminder');
    expect(rows[0].textContent).toContain('•••• 4567');
    expect(rows[1].textContent).toContain('whatsapp.status_delivered');
    expect(rows[2].textContent).toContain('whatsapp.status_failed');
    expect(rows[2].textContent).toContain('whatsapp.error_not_on_whatsapp');
    expect(el.textContent).toContain('"used":3,"limit":500');
    expect(el.querySelector('mat-progress-bar')).not.toBeNull();
  });

  it('says there is nothing yet', () => {
    const { el } = setup(list([]));
    expect(el.querySelector('.empty')!.textContent).toContain('whatsapp.none');
  });

  it('marks test mode and a server without WhatsApp', () => {
    expect(setup(list([], { simulated: true })).el.textContent).toContain('whatsapp.test_mode');
    http.verify();
    TestBed.resetTestingModule();
    expect(setup(list([], { configured: false })).el.textContent).toContain('whatsapp.unconfigured_title');
  });

  it('shows a Free teacher the lock and the plan that has it', () => {
    const { el } = setup('locked');
    expect(el.textContent).toContain('whatsapp.locked_title');
    expect(el.textContent).toContain('Pro');
    expect(el.querySelector('.panel-row')).toBeNull();
  });
});
