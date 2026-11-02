import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../core/auth/auth.service';
import { TranslateService } from '../core/i18n/translate.service';
import { PushService, PushState } from '../core/notifications/push.service';
import { ToastService } from '../core/ui/toast.service';
import { PushPrompt, SNOOZE_DAYS } from './push-prompt';

describe('PushPrompt', () => {
  const state = signal<PushState>('off');
  const push = { state, enable: vi.fn(), disable: vi.fn() };
  const toast = { success: vi.fn(), error: vi.fn() };

  const setup = (status = false) => {
    TestBed.configureTestingModule({
      providers: [
        { provide: PushService, useValue: push },
        { provide: AuthService, useValue: { user: signal({ id: 'u1' }) } },
        { provide: ToastService, useValue: toast },
      ],
    });
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(PushPrompt);
    fixture.componentRef.setInput('status', status);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const text = () => el.textContent ?? '';
    const click = (label: string) => {
      const button = [...el.querySelectorAll('button')].find((b) => b.textContent?.includes(label));
      button!.click();
      fixture.detectChanges();
    };
    return { fixture, text, click };
  };

  beforeEach(() => {
    localStorage.clear();
    state.set('off');
    push.enable.mockReset();
    push.disable.mockReset();
    toast.success.mockReset();
  });

  it('offers to turn notifications on, and asks the browser only when Turn on is pressed', async () => {
    push.enable.mockResolvedValue(true);
    const { text, click } = setup();
    expect(text()).toContain('push.offer_title');
    expect(push.enable).not.toHaveBeenCalled();
    click('push.turn_on');
    await Promise.resolve();
    expect(push.enable).toHaveBeenCalled();
    await Promise.resolve();
    expect(toast.success).toHaveBeenCalledWith('push.turned_on');
  });

  it('stays quiet for two weeks after Not now', () => {
    const { text, click } = setup();
    click('push.not_now');
    expect(text()).toBe('');
    const until = localStorage.getItem('gurubook.push-snooze.u1')!;
    expect(until).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(SNOOZE_DAYS).toBe(14);
    TestBed.resetTestingModule();
    expect(setup().text()).toBe('');
  });

  it('explains Add to Home Screen on an iPhone that has not done it', () => {
    state.set('needs-install');
    expect(setup().text()).toContain('push.install_title');
  });

  it('shows nothing when this phone cannot do notifications or the server cannot send them', () => {
    for (const s of ['unsupported', 'unavailable', 'checking'] as const) {
      state.set(s);
      TestBed.resetTestingModule();
      expect(setup(true).text()).toBe('');
    }
  });

  it('shows the steady states only where asked', () => {
    state.set('on');
    expect(setup().text()).toBe('');
    TestBed.resetTestingModule();
    const { text, click } = setup(true);
    expect(text()).toContain('push.on_title');
    push.disable.mockResolvedValue(undefined);
    click('push.turn_off');
    expect(push.disable).toHaveBeenCalled();
    state.set('blocked');
    TestBed.resetTestingModule();
    expect(setup(true).text()).toContain('push.blocked_title');
  });
});
