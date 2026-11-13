import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { provideRouter, Router } from '@angular/router';
import { AppConfig } from '../core/config/app-config';
import { TranslateService } from '../core/i18n/translate.service';
import { StudentScanDialog } from './student-scan-dialog';

const API = 'http://api.test';

const student = {
  id: 's1',
  firstName: 'Amara',
  lastName: 'Silva',
  fullName: 'Amara Silva',
  phone: '+94771234567',
  school: 'Kandy College',
  grade: '11',
  guardianName: 'Nimal Silva',
  guardianPhone: '+94777654321',
  hasAccount: false,
  hasParent: false,
  enrolments: [
    { id: 'e1', classId: 'c1', classTitle: 'Maths 2027', indexNumber: 1, status: 'Active', feePlan: 'Full', discountAmount: 0, joinedOn: '2026-01-10' },
  ],
};

describe('StudentScanDialog', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<StudentScanDialog>;
  const close = vi.fn();

  /** Lets every pending promise run and redraws: the camera and the lookups are plain promises, not just requests. */
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((resume) => setTimeout(resume, 0));
    fixture.detectChanges();
  };

  const open = () => {
    close.mockReset();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MatDialogRef, useValue: { close } },
        { provide: MatDialog, useValue: { open: vi.fn() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    fixture = TestBed.createComponent(StudentScanDialog);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  afterEach(() => {
    http.verify();
    vi.unstubAllGlobals();
  });

  it('says so when the device has no camera, and still closes', async () => {
    vi.stubGlobal('navigator', { ...navigator, mediaDevices: undefined });
    const root = open();
    await settle();

    expect(root.textContent).toContain('scan.no_camera');
    Array.from(root.querySelectorAll('button'))
      .find((b) => b.textContent!.includes('common.close'))!
      .click();
    expect(close).toHaveBeenCalled();
  });

  it('explains a blocked camera', async () => {
    const denied = new DOMException('no', 'NotAllowedError');
    vi.stubGlobal('navigator', { ...navigator, mediaDevices: { getUserMedia: () => Promise.reject(denied) } });
    const root = open();
    await settle();

    expect(root.textContent).toContain('scan.camera_blocked');
  });

  it('shows the student, what they owe today and the parent to call', async () => {
    vi.stubGlobal('navigator', { ...navigator, mediaDevices: undefined });
    const root = open();
    await settle();

    // The card is handed to the dialog as if the camera had read it.
    (fixture.componentInstance as unknown as { found(value: string): Promise<void> }).found('EV1:abc');
    const found = http.expectOne(`${API}/api/students/cards/find`);
    expect(found.request.body).toEqual({ code: 'EV1:abc' });
    found.flush(student);
    await settle();

    http.expectOne(`${API}/api/fees/enrolments/e1`).flush({
      enrolmentId: 'e1', studentId: 's1', fullName: 'Amara Silva', indexNumber: 1, classId: 'c1', classTitle: 'Maths 2027',
      feePlan: 'Full', discountAmount: 0, isActive: true, outstanding: 4000, dues: [], payments: [], reminders: [],
    });
    http
      .expectOne((r) => r.url === `${API}/api/attendance/enrolments/e1/report`)
      .flush({ entries: [{ date: '2026-11-13', start: '16:00', end: '18:00', isExtra: false, status: 'Late' }], classes: [] });
    await settle();

    expect(root.textContent).toContain('Amara Silva');
    expect(root.textContent).toContain('Maths 2027');
    expect(root.textContent).toContain('attendance.late');
    expect(root.textContent).toContain('scan.owes');
    expect(root.querySelector<HTMLAnchorElement>('a[href^="tel:"]')!.href).toBe('tel:+94777654321');
    expect(Array.from(root.querySelectorAll('button')).some((b) => b.textContent!.includes('fees.record_payment'))).toBe(true);
  });

  it('opens the student page, and says so when a card is not one of the teachers', async () => {
    vi.stubGlobal('navigator', { ...navigator, mediaDevices: undefined });
    const root = open();
    await settle();
    const dialog = fixture.componentInstance as unknown as { found(value: string): Promise<void> };

    // Text that is not a GuruBook card never reaches the server.
    await dialog.found('https://example.com');
    await settle();
    expect(root.textContent).toContain('error.card_unknown');

    dialog.found('EV1:nobody');
    http.expectOne(`${API}/api/students/cards/find`).flush({ code: 'card_unknown' }, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(root.textContent).toContain('error.card_unknown');

    dialog.found('EV1:abc');
    http.expectOne(`${API}/api/students/cards/find`).flush(student);
    await settle();
    http.expectOne(`${API}/api/fees/enrolments/e1`).flush({ outstanding: 0, dues: [], payments: [] });
    http.expectOne((r) => r.url === `${API}/api/attendance/enrolments/e1/report`).flush({ entries: [], classes: [] });
    await settle();

    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    Array.from(root.querySelectorAll('button'))
      .find((b) => b.textContent!.includes('scan.open_student'))!
      .click();
    expect(close).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/students', 's1']);
  });
});
