import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { UnreadNotifications } from '../../core/state/unread-notifications';
import { Notifications } from './notifications';

const API = 'http://api.test';
const item = (id: string, isRead: boolean, link: string | null) => ({
  id, kind: 'results_published', params: { exam: `Exam ${id}`, classTitle: 'Maths' }, link, count: 1, isRead, at: '2026-10-15T10:00:00Z',
});

describe('Notifications', () => {
  let http: HttpTestingController;

  const setup = () => {
    TestBed.configureTestingModule({ imports: [Notifications], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => (key === 'notifications.results_published.title' ? 'Results: {exam}' : key));
    const fixture = TestBed.createComponent(Notifications);
    fixture.detectChanges();
    http.expectOne(`${API}/api/notifications`).flush({ unread: 2, items: [item('a', false, '/results'), item('b', false, null), item('c', true, '/results')] });
    fixture.detectChanges();
    return fixture;
  };

  afterEach(() => http.verify());

  it('lists what the person was told and puts the unread count on the profile icon', () => {
    const fixture = setup();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Results: Exam a');
    expect(text).toContain('Results: Exam c');
    expect(fixture.nativeElement.querySelectorAll('.item.unread')).toHaveLength(2);
    expect(TestBed.inject(UnreadNotifications).count()).toBe(2);
  });

  it('marks one read and opens its screen when tapped', () => {
    const fixture = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    (fixture.nativeElement.querySelectorAll('.item')[0] as HTMLButtonElement).click();
    http.expectOne(`${API}/api/notifications/a/read`).flush({ unread: 1 });
    fixture.detectChanges();
    expect(navigate).toHaveBeenCalledWith('/results');
    expect(fixture.nativeElement.querySelectorAll('.item.unread')).toHaveLength(1);
    expect(TestBed.inject(UnreadNotifications).count()).toBe(1);
  });

  it('marks everything read in one go', () => {
    const fixture = setup();
    const readAll = (fixture.nativeElement as HTMLElement).querySelector('.page-header button') as HTMLButtonElement;
    readAll.click();
    http.expectOne(`${API}/api/notifications/read-all`).flush({ unread: 0 });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.item.unread')).toHaveLength(0);
    expect(TestBed.inject(UnreadNotifications).count()).toBe(0);
    expect((fixture.nativeElement as HTMLElement).querySelector('.page-header button')).toBeNull();
  });

  it('closes one with its cross, drops it from the list and takes an unread one off the count', () => {
    const fixture = setup();
    (fixture.nativeElement.querySelectorAll('.close')[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    // Gone at once, before the server has answered.
    expect(fixture.nativeElement.querySelectorAll('.item')).toHaveLength(2);
    expect(TestBed.inject(UnreadNotifications).count()).toBe(1);
    const request = http.expectOne(`${API}/api/notifications/a`);
    expect(request.request.method).toBe('DELETE');
    request.flush({ unread: 1 });
  });

  it('leaves the count alone when a read one is closed', () => {
    const fixture = setup();
    (fixture.nativeElement.querySelectorAll('.close')[2] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.item')).toHaveLength(2);
    expect(TestBed.inject(UnreadNotifications).count()).toBe(2);
    http.expectOne(`${API}/api/notifications/c`).flush({ unread: 2 });
  });

  it('brings the list back from the server if closing failed', () => {
    const fixture = setup();
    (fixture.nativeElement.querySelectorAll('.close')[0] as HTMLButtonElement).click();
    http.expectOne(`${API}/api/notifications/a`).flush({}, { status: 500, statusText: 'Server Error' });
    http.expectOne(`${API}/api/notifications`).flush({ unread: 2, items: [item('a', false, '/results'), item('b', false, null), item('c', true, '/results')] });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.item')).toHaveLength(3);
  });

  describe('swiping', () => {
    const touch = (target: Element, type: string, x: number, y = 10) =>
      target.dispatchEvent(new PointerEvent(type, { pointerId: 1, pointerType: 'touch', clientX: x, clientY: y, bubbles: true }));
    const faces = (fixture: { nativeElement: HTMLElement }) => fixture.nativeElement.querySelectorAll('.swipe-face');

    beforeEach(() => {
      vi.useFakeTimers();
      // jsdom has no pointer capture or layout.
      HTMLElement.prototype.setPointerCapture = vi.fn();
      Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, value: 300 });
    });
    afterEach(() => vi.useRealTimers());

    it('closes a row that is swiped far enough', () => {
      const fixture = setup();
      const face = faces(fixture)[0];
      touch(face, 'pointerdown', 200);
      touch(face, 'pointermove', 150);
      touch(face, 'pointermove', 40);
      touch(face, 'pointerup', 40);
      vi.advanceTimersByTime(200);
      fixture.detectChanges();
      expect(http.expectOne(`${API}/api/notifications/a`).request.method).toBe('DELETE');
      expect(fixture.nativeElement.querySelectorAll('.item')).toHaveLength(2);
    });

    it('springs back when the swipe is short, and the tap that follows does not open anything', () => {
      const fixture = setup();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      const face = faces(fixture)[0];
      touch(face, 'pointerdown', 200);
      touch(face, 'pointermove', 160);
      touch(face, 'pointerup', 160);
      (face.querySelector('.item') as HTMLButtonElement).click();
      vi.advanceTimersByTime(200);
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelectorAll('.item')).toHaveLength(3);
      expect(navigate).not.toHaveBeenCalled();
      http.expectNone(`${API}/api/notifications/a`);
    });

    it('ignores a mostly vertical drag, which is the person scrolling', () => {
      const fixture = setup();
      const face = faces(fixture)[0];
      touch(face, 'pointerdown', 200, 10);
      touch(face, 'pointermove', 150, 80);
      touch(face, 'pointermove', 20, 120);
      touch(face, 'pointerup', 20, 120);
      vi.advanceTimersByTime(200);
      expect(fixture.nativeElement.querySelectorAll('.item')).toHaveLength(3);
    });
  });
});
