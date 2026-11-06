import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MessageEvent, MessageSettings } from '../../core/api/messaging.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { ToastService } from '../../core/ui/toast.service';
import { MessageSettingsPage } from './message-settings';

const API = 'http://api.test';

const event = (over: Partial<MessageEvent> = {}): MessageEvent => ({
  event: 'fee_overdue',
  on: true,
  way: 'app',
  note: null,
  percent: null,
  ways: ['app', 'whatsapp', 'sms', 'whatsapp_then_sms'],
  fields: ['student', 'class', 'month', 'amount', 'teacher'],
  texts: [
    { language: 'en', default: '{student} owes {amount}.', custom: null },
    { language: 'si', default: '{student} {amount}.', custom: null },
    { language: 'ta', default: '{student} {amount}.', custom: null },
  ],
  ...over,
});

const settings = (...events: MessageEvent[]): MessageSettings => ({
  events,
  lowAttendanceWeeks: 4,
  lowAttendanceQuietDays: 14,
  noteMaxLength: 80,
});

describe('MessageSettingsPage', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<MessageSettingsPage>;
  const toast = { success: vi.fn(), error: vi.fn() };

  const setup = () => {
    toast.success.mockReset();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ToastService, useValue: toast }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    vi.spyOn(TestBed.inject(TranslateService), 'format').mockImplementation((key) => key);
    fixture = TestBed.createComponent(MessageSettingsPage);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  /** Opens an event's panel, which is where everything but its name lives. */
  const open = (root: HTMLElement, index = 0) => {
    root.querySelectorAll<HTMLElement>('mat-expansion-panel-header')[index].click();
    fixture.detectChanges();
  };

  const toggleButtons = (root: HTMLElement) => Array.from(root.querySelectorAll<HTMLButtonElement>('mat-button-toggle button'));
  const buttons = (root: HTMLElement) => Array.from(root.querySelectorAll<HTMLButtonElement>('.actions button'));

  afterEach(() => http.verify());

  it('lists every alert with how it is sent', () => {
    const root = setup();
    http
      .expectOne(`${API}/api/messaging`)
      .flush(settings(event(), event({ event: 'absent', way: 'whatsapp' }), event({ event: 'arrived', on: false })));
    fixture.detectChanges();

    expect(root.textContent).toContain('messages.event.fee_overdue');
    expect(root.textContent).toContain('messages.event.absent');
    expect(root.textContent).toContain('messages.way.whatsapp');
    expect(root.textContent).toContain('messages.off');
  });

  it('saves the way an alert is sent', () => {
    const root = setup();
    http.expectOne(`${API}/api/messaging`).flush(settings(event()));
    fixture.detectChanges();
    open(root);

    // The four ways, after the app-only choice the panel starts on.
    toggleButtons(root)[2].click();
    const saved = http.expectOne(`${API}/api/messaging/fee_overdue`);
    expect(saved.request.method).toBe('PUT');
    expect(saved.request.body).toEqual({ on: true, way: 'sms', note: null, percent: null });
    saved.flush(settings(event({ way: 'sms' })));
    fixture.detectChanges();

    expect(toast.success).toHaveBeenCalledWith('messages.saved');
  });

  it('switches an alert off without changing how it would be sent', () => {
    const root = setup();
    http.expectOne(`${API}/api/messaging`).flush(settings(event({ way: 'sms' })));
    fixture.detectChanges();
    open(root);

    root.querySelector<HTMLButtonElement>('mat-slide-toggle button')!.click();
    const saved = http.expectOne(`${API}/api/messaging/fee_overdue`);
    expect(saved.request.body).toEqual({ on: false, way: 'sms', note: null, percent: null });
    saved.flush(settings(event({ on: false, way: 'sms' })));
    fixture.detectChanges();

    expect(root.querySelectorAll('mat-button-toggle').length).toBe(0);
  });

  it('writes a text in the teacher own words, with a field put where they were typing', () => {
    const root = setup();
    http.expectOne(`${API}/api/messaging`).flush(settings(event({ way: 'sms' })));
    fixture.detectChanges();
    open(root);

    const box = root.querySelector<HTMLTextAreaElement>('textarea')!;
    expect(box.value).toBe('{student} owes {amount}.');
    box.value = 'Please pay for ';
    box.dispatchEvent(new Event('input'));
    box.setSelectionRange(box.value.length, box.value.length);
    fixture.detectChanges();

    const field = Array.from(root.querySelectorAll<HTMLButtonElement>('.fields button')).find((b) =>
      b.textContent?.includes('messages.field.student'),
    )!;
    field.click();
    fixture.detectChanges();

    const save = buttons(root)[0];
    save.click();
    const saved = http.expectOne(`${API}/api/messaging/fee_overdue/texts/en`);
    expect(saved.request.method).toBe('PUT');
    expect(saved.request.body).toEqual({ text: 'Please pay for {student}' });
    saved.flush(settings(event({ way: 'sms', texts: [{ language: 'en', default: '{student} owes {amount}.', custom: 'Please pay for {student}' }] })));
    fixture.detectChanges();

    expect(toast.success).toHaveBeenCalledWith('messages.saved');
    expect(root.querySelector<HTMLTextAreaElement>('textarea')!.value).toBe('Please pay for {student}');
  });

  it('puts GuruBook words back', () => {
    const root = setup();
    http
      .expectOne(`${API}/api/messaging`)
      .flush(settings(event({ way: 'sms', texts: [{ language: 'en', default: 'Fee for {student}.', custom: 'Mine.' }] })));
    fixture.detectChanges();
    open(root);

    buttons(root)[1].click();
    const saved = http.expectOne(`${API}/api/messaging/fee_overdue/texts/en`);
    expect(saved.request.body).toEqual({ text: '' });
    saved.flush(settings(event({ way: 'sms', texts: [{ language: 'en', default: 'Fee for {student}.', custom: null }] })));
    fixture.detectChanges();

    expect(root.querySelector<HTMLTextAreaElement>('textarea')!.value).toBe('Fee for {student}.');
  });

  it('asks for the teacher own line on WhatsApp, and not for the words of the text', () => {
    const root = setup();
    http.expectOne(`${API}/api/messaging`).flush(settings(event({ way: 'whatsapp' })));
    fixture.detectChanges();
    open(root);

    expect(root.querySelector('textarea')).toBeNull();
    const line = root.querySelector<HTMLInputElement>('input[matInput]')!;
    line.value = 'Call me if this is wrong.';
    line.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    buttons(root)[0].click();
    const saved = http.expectOne(`${API}/api/messaging/fee_overdue`);
    expect(saved.request.body).toEqual({ on: true, way: 'whatsapp', note: 'Call me if this is wrong.', percent: null });
    saved.flush(settings(event({ way: 'whatsapp', note: 'Call me if this is wrong.' })));
    fixture.detectChanges();
  });

  it('lets the teacher say how little attendance is too little', () => {
    const root = setup();
    http.expectOne(`${API}/api/messaging`).flush(settings(event({ event: 'low_attendance', percent: 75, way: 'app' })));
    fixture.detectChanges();
    open(root);

    expect(root.textContent).toContain('messages.percent_label');
    expect(root.querySelector('mat-select')).not.toBeNull();
  });

  it('tells a teacher whose plan has no message settings, and asks when it would come', () => {
    const root = setup();
    http.expectOne(`${API}/api/messaging`).flush(
      { type: 'about:blank', title: 'Forbidden', status: 403, code: 'plan_feature_locked' },
      { status: 403, statusText: 'Forbidden' },
    );
    http.expectOne(`${API}/api/plans/mine`).flush({
      name: 'Free',
      features: [{ key: 'custom_messages', enabled: false, availableOn: 'Pro' }],
      limits: [],
    });
    fixture.detectChanges();

    expect(root.textContent).toContain('messages.locked_title');
    expect(root.textContent).toContain('Pro');
  });
});
