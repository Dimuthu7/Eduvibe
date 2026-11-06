import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { BankDetails, TeacherClaim } from '../../core/api/payments.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { PaymentsPage } from './payments';

const API = 'http://api.test';

const details = (over: Partial<BankDetails> = {}): BankDetails => ({
  bankName: 'Commercial Bank', accountName: 'S Perera', accountNumber: '8001234567', branch: 'Kandy', note: '',
  hasQr: false, qrVersion: 0, ...over,
});

const claim = (over: Partial<TeacherClaim> = {}): TeacherClaim => ({
  id: 'c1', enrolmentId: 'e1', studentId: 's1', indexNumber: 4, fullName: 'Dilani Silva', classTitle: 'Grade 10 Maths',
  status: 'Open', amount: 1500, outstanding: 1500, month: '2026-11', reference: 'GB7K2M9Q', note: '', hasSlip: true,
  createdAt: '2026-11-05T05:00:00Z', decidedAt: null, rejectReason: null, receiptNo: null, ...over,
});

describe('PaymentsPage', () => {
  let http: HttpTestingController;
  const open = vi.fn(() => ({ afterClosed: () => of('Nothing arrived') }));

  const setup = (bank: BankDetails, claims: TeacherClaim[]) => {
    TestBed.configureTestingModule({
      imports: [PaymentsPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MatDialog, useValue: { open } }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    URL.createObjectURL = vi.fn(() => 'blob:image');
    URL.revokeObjectURL = vi.fn();
    const fixture = TestBed.createComponent(PaymentsPage);
    fixture.detectChanges();
    http.expectOne(`${API}/api/payments/details`).flush(bank);
    http.expectOne(`${API}/api/payments/claims`).flush(claims);
    fixture.detectChanges();
    if (bank.hasQr) {
      http.expectOne(`${API}/api/payments/qr?v=${bank.qrVersion}`).flush(new Blob(['x'], { type: 'image/png' }));
      fixture.detectChanges();
    }

    return { fixture, el: fixture.nativeElement as HTMLElement };
  };

  const button = (el: HTMLElement, label: string) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent!.includes(label))!;

  afterEach(() => {
    http.verify();
    open.mockClear();
  });

  it('fills the form from the saved details and lists what is waiting', () => {
    const { fixture, el } = setup(details(), [claim()]);
    expect((el.querySelector('input[formControlName=accountNumber]') as HTMLInputElement).value).toBe('8001234567');
    const text = el.textContent ?? '';
    expect(text).toContain('Dilani Silva');
    expect(text).toContain('GB7K2M9Q');
    expect(text).toContain('1,500');
    // The slip is fetched only when the teacher asks for it.
    expect(el.querySelector('img.slip-image')).toBeNull();
    (Array.from(el.querySelectorAll('button')).find((b) => b.textContent!.includes('payments.view_slip')) as HTMLButtonElement).click();
    http.expectOne(`${API}/api/payments/claims/c1/slip`).flush(new Blob(['x'], { type: 'image/jpeg' }));
    fixture.detectChanges();
    expect(el.querySelector('img.slip-image')?.getAttribute('src')).toBe('blob:image');
  });

  it('says so when nothing is waiting', () => {
    const { el } = setup(details(), []);
    expect(el.textContent).toContain('payments.none_waiting');
  });

  it('confirms a payment and reloads', () => {
    const { fixture, el } = setup(details(), [claim()]);
    button(el, 'payments.confirm').click();
    fixture.detectChanges();

    const request = http.expectOne(`${API}/api/payments/claims/c1/confirm`);
    expect(request.request.body).toEqual({ amount: null });
    request.flush({ ...claim(), status: 'Confirmed', receiptNo: 'R-000004' });
    fixture.detectChanges();
    http.expectOne(`${API}/api/payments/details`).flush(details());
    http.expectOne(`${API}/api/payments/claims`).flush([{ ...claim(), status: 'Confirmed', receiptNo: 'R-000004' }]);
    fixture.detectChanges();
    expect(el.textContent).toContain('R-000004');
  });

  it('asks for a reason before rejecting one', () => {
    const { fixture, el } = setup(details(), [claim()]);
    button(el, 'payments.reject').click();
    fixture.detectChanges();

    expect(open).toHaveBeenCalledOnce();
    const request = http.expectOne(`${API}/api/payments/claims/c1/reject`);
    expect(request.request.body).toEqual({ reason: 'Nothing arrived' });
    request.flush({ ...claim(), status: 'Rejected', rejectReason: 'Nothing arrived' });
    fixture.detectChanges();
    http.expectOne(`${API}/api/payments/details`).flush(details());
    http.expectOne(`${API}/api/payments/claims`).flush([]);
    fixture.detectChanges();
  });

  it('shows the QR once there is one', () => {
    const { el } = setup(details({ hasQr: true, qrVersion: 42 }), []);
    expect(el.querySelector('img.qr')?.getAttribute('src')).toBe('blob:image');
  });
});
