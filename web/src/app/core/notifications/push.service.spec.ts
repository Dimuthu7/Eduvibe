import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SwPush } from '@angular/service-worker';
import { BehaviorSubject } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { AppConfig } from '../config/app-config';
import { PushService } from './push.service';

const API = 'http://api.test';
const user = { id: 'u1', roles: ['Teacher'], firstName: 'Sunil', lastName: 'P', mustChangePassword: false, needsProfile: false };

const subscription = {
  endpoint: 'https://push.example/abc',
  toJSON: () => ({ endpoint: 'https://push.example/abc', keys: { p256dh: 'P', auth: 'A' } }),
} as unknown as PushSubscription;

describe('PushService', () => {
  let http: HttpTestingController;
  const current = new BehaviorSubject<PushSubscription | null>(null);
  const swPush = {
    isEnabled: true,
    subscription: current,
    requestSubscription: vi.fn(),
    unsubscribe: vi.fn(() => Promise.resolve()),
  };
  const signedIn = signal<unknown>(user);
  let permission: NotificationPermission = 'default';

  const setup = () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SwPush, useValue: swPush },
        { provide: AuthService, useValue: { user: signedIn } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    return TestBed.inject(PushService);
  };

  const settle = async () => {
    for (let i = 0; i < 5; i++) await Promise.resolve();
  };

  const answerSettings = async (body: { enabled: boolean; publicKey: string }) => {
    await settle();
    http.expectOne(`${API}/api/notifications/push`).flush(body);
    await settle();
  };

  beforeEach(() => {
    localStorage.clear();
    signedIn.set(user);
    current.next(null);
    swPush.isEnabled = true;
    swPush.requestSubscription.mockReset();
    swPush.unsubscribe.mockClear();
    permission = 'default';
    const notification = function () {};
    Object.defineProperty(notification, 'permission', { get: () => permission });
    vi.stubGlobal('Notification', notification);
    (window as unknown as { PushManager: unknown }).PushManager = function () {};
  });

  afterEach(() => {
    http.verify();
    vi.unstubAllGlobals();
  });

  it('is unsupported without a service worker', async () => {
    swPush.isEnabled = false;
    const push = setup();
    await push.refresh();
    expect(push.state()).toBe('unsupported');
  });

  it('is unavailable when the server has no keys', async () => {
    const push = setup();
    const done = push.refresh();
    await answerSettings({ enabled: false, publicKey: '' });
    await done;
    expect(push.state()).toBe('unavailable');
  });

  it('is blocked when the browser said no', async () => {
    permission = 'denied';
    const push = setup();
    const done = push.refresh();
    await answerSettings({ enabled: true, publicKey: 'K' });
    await done;
    expect(push.state()).toBe('blocked');
  });

  it('is off until the person turns it on, then registers the phone and remembers it for that person', async () => {
    const push = setup();
    const refreshed = push.refresh();
    await answerSettings({ enabled: true, publicKey: 'K' });
    await refreshed;
    expect(push.state()).toBe('off');

    swPush.requestSubscription.mockResolvedValue(subscription);
    const enabled = push.enable();
    await settle();
    const req = http.expectOne(`${API}/api/notifications/push/device`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toMatchObject({ endpoint: subscription.endpoint, p256dh: 'P', auth: 'A' });
    req.flush(null);
    expect(await enabled).toBe(true);
    expect(push.state()).toBe('on');
    expect(localStorage.getItem('gurubook.push.u1')).toBe('on');
    expect(swPush.requestSubscription).toHaveBeenCalledWith({ serverPublicKey: 'K' });
  });

  it('stays off when the person declines the browser question', async () => {
    const push = setup();
    const refreshed = push.refresh();
    await answerSettings({ enabled: true, publicKey: 'K' });
    await refreshed;
    swPush.requestSubscription.mockRejectedValue(new Error('denied'));
    permission = 'denied';
    expect(await push.enable()).toBe(false);
    expect(push.state()).toBe('blocked');
    expect(localStorage.getItem('gurubook.push.u1')).toBeNull();
  });

  it('does not switch on for someone else on the same phone', async () => {
    current.next(subscription);
    localStorage.setItem('gurubook.push.u1', 'on');
    signedIn.set({ ...user, id: 'u2' });
    const push = setup();
    const done = push.refresh();
    await answerSettings({ enabled: true, publicKey: 'K' });
    await done;
    expect(push.state()).toBe('off');
  });

  it('refreshes the device and shows on when this person had turned it on', async () => {
    current.next(subscription);
    localStorage.setItem('gurubook.push.u1', 'on');
    const push = setup();
    const done = push.refresh();
    await answerSettings({ enabled: true, publicKey: 'K' });
    http.expectOne(`${API}/api/notifications/push/device`).flush(null);
    await done;
    expect(push.state()).toBe('on');
  });

  it('turns off: tells the server, drops the browser subscription and forgets the choice', async () => {
    current.next(subscription);
    localStorage.setItem('gurubook.push.u1', 'on');
    const push = setup();
    const done = push.refresh();
    await answerSettings({ enabled: true, publicKey: 'K' });
    http.expectOne(`${API}/api/notifications/push/device`).flush(null);
    await done;

    const off = push.disable();
    await settle();
    const req = http.expectOne(`${API}/api/notifications/push/unsubscribe`);
    expect(req.request.body).toEqual({ endpoint: subscription.endpoint });
    req.flush(null);
    await off;
    expect(swPush.unsubscribe).toHaveBeenCalled();
    expect(localStorage.getItem('gurubook.push.u1')).toBeNull();
    expect(push.state()).toBe('off');
  });
});
