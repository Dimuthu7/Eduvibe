import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { WhatsAppActions } from '../../shared/whatsapp-actions';
import { AttendanceMark } from './mark';

const API = 'http://api.test';

const detail = (editable = true, saved: { taken?: boolean; absent?: number } = {}) => ({
  session: {
    id: 's1', classId: 'c1', classTitle: '2027 Maths', date: '2026-10-11', start: '16:00', end: '18:00',
    isExtra: false, note: null, taken: saved.taken ?? false, present: 0, late: 0, absent: saved.absent ?? 0, editable,
  },
  students: [
    { enrolmentId: 'e1', indexNumber: 1, fullName: 'Amara Silva', status: 'Present' },
    { enrolmentId: 'e2', indexNumber: 2, fullName: 'Bandula Perera', status: 'Present' },
    { enrolmentId: 'e3', indexNumber: 3, fullName: 'Chamari Fernando', status: 'Absent' },
  ],
});

describe('AttendanceMark', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<AttendanceMark>;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [AttendanceMark], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
  });

  // Avatars ask once which students have a photo; none do here.
  const settlePhotos = () => http.match((r) => r.url.endsWith('/api/students/photos')).forEach((r) => r.flush([]));

  afterEach(() => {
    vi.useRealTimers();
    settlePhotos();
    http.verify();
  });

  const open = async (editable = true, saved: { taken?: boolean; absent?: number } = {}) => {
    fixture = TestBed.createComponent(AttendanceMark);
    fixture.componentRef.setInput('id', 's1');
    fixture.detectChanges();
    await Promise.resolve();
    http.expectOne(`${API}/api/attendance/sessions/s1`).flush(detail(editable, saved));
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return { root, rows: () => Array.from(root.querySelectorAll<HTMLButtonElement>('button.hit')) };
  };

  it('starts from the saved marks and counts them', async () => {
    const { root } = await open();
    expect(root.textContent).toContain('Chamari Fernando');
    expect(root.querySelector('.count.absent')!.textContent).toContain('1');
    expect(root.querySelector('.count.present')!.textContent).toContain('2');
  });

  it('flips a student between present and absent with a tap', async () => {
    const { root, rows } = await open();
    rows()[0].click();
    fixture.detectChanges();
    expect(root.querySelector('.count.absent')!.textContent).toContain('2');
    rows()[0].click();
    fixture.detectChanges();
    expect(root.querySelector('.count.absent')!.textContent).toContain('1');
  });

  it('marks late on press and hold, and the click after the hold changes nothing', async () => {
    vi.useFakeTimers();
    const { root, rows } = await open();
    rows()[1].dispatchEvent(new Event('pointerdown'));
    vi.advanceTimersByTime(600);
    rows()[1].dispatchEvent(new Event('pointerup'));
    rows()[1].click();
    fixture.detectChanges();
    expect(root.querySelector('.count.late')!.textContent).toContain('1');
    expect(root.querySelector('.count.absent')!.textContent).toContain('1');
  });

  it('does not mark anyone late when the finger slides to scroll', async () => {
    vi.useFakeTimers();
    const { root, rows } = await open();
    rows()[1].dispatchEvent(new MouseEvent('pointerdown', { clientX: 100, clientY: 300 }));
    vi.advanceTimersByTime(200);
    rows()[1].dispatchEvent(new MouseEvent('pointermove', { clientX: 100, clientY: 260 }));
    vi.advanceTimersByTime(600);
    rows()[1].dispatchEvent(new Event('pointerup'));
    fixture.detectChanges();
    expect(root.querySelector('.count.late')!.textContent).toContain('0');
  });

  it('sends only the students who are not present, then returns to the day', async () => {
    const { root, rows } = await open();
    rows()[1].click();
    fixture.detectChanges();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne(`${API}/api/attendance/sessions/s1/marks`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.marks).toEqual([
      { enrolmentId: 'e2', status: 'Absent' },
      { enrolmentId: 'e3', status: 'Absent' },
    ]);
    request.flush({});
    expect(navigate).toHaveBeenCalledWith(['/attendance'], { queryParams: { date: '2026-10-11' } });
  });

  it('puts everyone back to present with one button', async () => {
    const { root } = await open();
    (Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.includes('attendance.all_present'))!).click();
    fixture.detectChanges();
    expect(root.querySelector('.count.absent')!.textContent).toContain('0');
    expect(root.querySelector('.count.present')!.textContent).toContain('3');
  });

  it('cannot be changed once the week is over', async () => {
    const { root, rows } = await open(false);
    expect(rows().every((r) => r.disabled)).toBe(true);
    expect(root.querySelector('form')).toBeNull();
    expect(root.textContent).toContain('attendance.locked');
  });

  it('offers to tell the parents of absent students only once attendance is saved with someone absent', async () => {
    const tell = (root: HTMLElement) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent!.includes('whatsapp.tell_parents'));

    expect(tell((await open()).root)).toBeUndefined();
    fixture.destroy();
    expect(tell((await open(true, { taken: true, absent: 0 })).root)).toBeUndefined();
    fixture.destroy();

    const { root } = await open(true, { taken: true, absent: 1 });
    const whatsapp = vi.spyOn(TestBed.inject(WhatsAppActions), 'open').mockResolvedValue();
    tell(root)!.click();
    expect(whatsapp).toHaveBeenCalledWith(expect.objectContaining({ path: '/api/attendance/sessions/s1/whatsapp' }));
  });
});
