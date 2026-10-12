import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { EnrolmentFees } from '../../core/api/fees.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { monthChoices, PaymentDialog } from './payment-dialog';

const API = 'http://api.test';

const fees = (over: Partial<EnrolmentFees> = {}): EnrolmentFees => ({
  enrolmentId: 'e1',
  studentId: 's1',
  fullName: 'Amara Silva',
  indexNumber: 3,
  classId: 'c1',
  classTitle: 'Grade 6 ICT',
  feePlan: 'Full',
  discountAmount: 0,
  isActive: true,
  outstanding: 3000,
  dues: [
    { month: '2026-10', due: 1500, paid: 0, status: 'Unpaid' },
    { month: '2026-09', due: 1500, paid: 0, status: 'Unpaid' },
    { month: '2026-08', due: 1500, paid: 1500, status: 'Paid' },
  ],
  payments: [],
  ...over,
});

describe('monthChoices', () => {
  it('offers months that still owe, oldest first, then the next three months', () => {
    expect(monthChoices(fees(), '2026-10')).toEqual(['2026-09', '2026-10', '2026-11', '2026-12', '2027-01']);
  });

  it('does not offer a month twice when it is already on the record', () => {
    const withNext = fees({ dues: [...fees().dues, { month: '2026-11', due: 1500, paid: 500, status: 'PartlyPaid' }] });
    expect(monthChoices(withNext, '2026-10')).toEqual(['2026-09', '2026-10', '2026-11', '2026-12', '2027-01']);
  });
});

describe('PaymentDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  const setup = (data: EnrolmentFees = fees()) => {
    close.mockReset();
    TestBed.configureTestingModule({
      imports: [PaymentDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MatDialogRef, useValue: { close } }, { provide: MAT_DIALOG_DATA, useValue: data }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(PaymentDialog);
    fixture.detectChanges();
    const form = fixture.componentInstance['form'];
    const submit = () => {
      (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };
    return { fixture, form, submit };
  };

  afterEach(() => http.verify());

  it('starts with what the student owes, paid in cash today', () => {
    const { form } = setup();
    expect(form.getRawValue()).toMatchObject({ amount: 3000, method: 'Cash', months: [] });
  });

  it('sends nothing without an amount', () => {
    const { form, submit } = setup(fees({ outstanding: 0 }));
    submit();
    expect(form.controls.amount.errors).toMatchObject({ required: true });
    http.expectNone(`${API}/api/fees/payments`);
  });

  it('sends the payment and closes with the saved record', () => {
    const { form, submit } = setup();
    form.patchValue({ amount: 1500, method: 'BankTransfer', months: ['2026-09'] });
    submit();
    const req = http.expectOne(`${API}/api/fees/payments`);
    expect(req.request.body).toMatchObject({ enrolmentId: 'e1', amount: 1500, method: 'BankTransfer', months: ['2026-09'] });
    req.flush({ id: 'p1', receiptNo: 'R-000001' });
    expect(close).toHaveBeenCalledWith(expect.objectContaining({ receiptNo: 'R-000001' }));
  });

  it('shows an amount the server refuses beside the amount field', () => {
    const { form, submit } = setup();
    submit();
    http.expectOne(`${API}/api/fees/payments`).flush({ code: 'amount_exceeds_outstanding' }, { status: 400, statusText: 'Bad Request' });
    expect(form.controls.amount.errors).toEqual({ server: 'error.amount_exceeds_outstanding' });
    expect(close).not.toHaveBeenCalled();
  });
});
