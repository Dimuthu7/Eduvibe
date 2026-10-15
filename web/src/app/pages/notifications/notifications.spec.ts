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
});
