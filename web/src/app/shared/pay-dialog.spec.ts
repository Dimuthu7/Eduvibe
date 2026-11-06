import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { PaymentClaim, PayWay } from '../core/api/payments.service';
import { AppConfig } from '../core/config/app-config';
import { TranslateService } from '../core/i18n/translate.service';
import { PayDialog, PayTarget } from './pay-dialog';

const API = 'http://api.test';
const TARGET: PayTarget = { enrolmentId: 'e1', month: '2026-11', classTitle: 'Grade 10 Maths' };

const way = (over: Partial<PayWay> = {}): PayWay => ({
  canPay: true, reference: 'GB7K2M9Q', outstanding: 2000, month: '2026-11', dueDate: '2026-11-10', classTitle: 'Grade 10 Maths',
  bankName: 'Commercial Bank', accountName: 'S Perera', accountNumber: '8001234567', branch: 'Kandy', note: '',
  hasQr: false, qrVersion: 0, claim: null, ...over,
});

const claim = (over: Partial<PaymentClaim> = {}): PaymentClaim => ({
  id: 'c1', status: 'Open', amount: 2000, month: '2026-11', reference: 'GB7K2M9Q', note: '', hasSlip: false,
  createdAt: '2026-11-05T05:00:00Z', decidedAt: null, rejectReason: null, receiptNo: null, ...over,
});

describe('PayDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  const setup = (answer: PayWay, target: PayTarget = TARGET) => {
    TestBed.configureTestingModule({
      imports: [PayDialog],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: target }, { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    URL.createObjectURL = vi.fn(() => 'blob:qr');
    URL.revokeObjectURL = vi.fn();
    const fixture = TestBed.createComponent(PayDialog);
    fixture.detectChanges();
    const base = target.studentId ? `children/${target.studentId}` : 'mine';
    http.expectOne(`${API}/api/payments/${base}/${target.enrolmentId}?month=${target.month}`).flush(answer);
    fixture.detectChanges();
    if (answer.canPay && answer.hasQr) {
      // The image carries the sign-in header, so it is a request of its own rather than an address in src.
      http.expectOne(`${API}/api/payments/${base}/${target.enrolmentId}/qr?v=${answer.qrVersion}`).flush(new Blob(['x'], { type: 'image/png' }));
      fixture.detectChanges();
    }

    return { fixture, el: fixture.nativeElement as HTMLElement };
  };

  const submit = (el: HTMLElement) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent!.includes('pay.submit'))!;

  afterEach(() => {
    http.verify();
    close.mockReset();
  });

  it('shows the amount, the account and the code to write on the transfer', () => {
    const { el } = setup(way({ hasQr: true, qrVersion: 7 }));
    const text = el.textContent ?? '';
    expect(text).toContain('2,000');
    expect(text).toContain('GB7K2M9Q');
    expect(text).toContain('8001234567');
    expect(text).toContain('Commercial Bank');
    expect(el.querySelector('img')?.getAttribute('src')).toBe('blob:qr');
  });

  it('says the teacher takes the fee in person when they are not set up', () => {
    const { el } = setup(way({ canPay: false }));
    expect(el.textContent).toContain('pay.in_person');
    expect(submit(el)).toBeUndefined();
  });

  it('tells the teacher the fee has been paid, with the amount already filled in', () => {
    const { fixture, el } = setup(way());
    submit(el).click();

    const request = http.expectOne(`${API}/api/payments/mine/e1/claims`);
    expect(request.request.body).toEqual({ month: '2026-11', amount: 2000, note: null });
    request.flush(claim());
    fixture.detectChanges();
    expect(close).toHaveBeenCalledWith(claim());
  });

  it('shows a claim that is already waiting, and does not ask again', () => {
    const { el } = setup(way({ claim: claim() }));
    expect(el.textContent).toContain('pay.status.open');
    expect(submit(el)).toBeUndefined();
  });

  it('shows why a claim was turned down, and lets them try again', () => {
    const { el } = setup(way({ claim: claim({ status: 'Rejected', rejectReason: 'Nothing arrived', decidedAt: '2026-11-05T06:00:00Z' }) }));
    expect(el.textContent).toContain('pay.status.rejected');
    expect(el.textContent).toContain('Nothing arrived');
    expect(submit(el)).toBeDefined();
  });

  it('asks for a child fee through the parent route', () => {
    const { el } = setup(way(), { ...TARGET, studentId: 's1' });
    expect(el.textContent).toContain('GB7K2M9Q');
  });
});
