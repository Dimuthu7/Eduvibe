import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { TeacherSummary } from '../../../core/api/admin.service';
import { Plan, TeacherPlan } from '../../../core/api/plans.service';
import { AppConfig } from '../../../core/config/app-config';
import { TranslateService } from '../../../core/i18n/translate.service';
import { ToastService } from '../../../core/ui/toast.service';
import { TeacherPlanDialog } from './teacher-plan-dialog';

const teacher = { id: 't1', fullName: 'Nimal Perera', phone: '+94771234567' } as TeacherSummary;
const plan = (id: string, name: string, isActive = true): Plan => ({
  id, name, description: null, isDefault: id === 'free', isActive, monthlyPrice: 0, features: {}, limits: {},
});
const onFree: TeacherPlan = {
  teacherId: 't1',
  teacherName: 'Nimal Perera',
  effective: { planId: 'free', planName: 'Free', isDefault: true, startsOn: null, endsOn: null },
  subscriptions: [],
  payments: [],
};

describe('TeacherPlanDialog', () => {
  let http: HttpTestingController;
  const toast = { success: vi.fn(), error: vi.fn() };

  beforeEach(() => {
    toast.success.mockReset();
    TestBed.configureTestingModule({
      imports: [TeacherPlanDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MAT_DIALOG_DATA, useValue: teacher }, { provide: ToastService, useValue: toast }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue('http://api.test');
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
  });

  afterEach(() => http.verify());

  const setup = () => {
    const fixture = TestBed.createComponent(TeacherPlanDialog);
    fixture.detectChanges();
    http.expectOne('http://api.test/api/plans/plans').flush([plan('free', 'Free'), plan('pro', 'Pro'), plan('old', 'Old', false)]);
    http.expectOne('http://api.test/api/plans/teachers/t1').flush(onFree);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement, c: fixture.componentInstance as unknown as Record<string, any> };
  };

  it('shows the plan the teacher is on and offers only plans that can be given', () => {
    const { root, c } = setup();

    expect(root.textContent).toContain('Free');
    expect(root.textContent).toContain('teacher_plan.default_note');
    expect(c['choices']().map((p: Plan) => p.name)).toEqual(['Free', 'Pro']);
  });

  it('puts the teacher on a plan, leaving a blank end date open, and shows the result', () => {
    const { fixture, root, c } = setup();
    c['planForm'].patchValue({ planId: 'pro', startsOn: '2026-11-01', note: ' trial ' });
    root.querySelector('form')!.dispatchEvent(new Event('submit'));

    const request = http.expectOne('http://api.test/api/plans/teachers/t1/subscription');
    expect(request.request.body).toEqual({ planId: 'pro', startsOn: '2026-11-01', endsOn: null, note: 'trial' });
    request.flush({ ...onFree, effective: { planId: 'pro', planName: 'Pro', isDefault: false, startsOn: '2026-11-01', endsOn: null } });
    fixture.detectChanges();

    expect(toast.success).toHaveBeenCalledWith('teacher_plan.assigned');
    expect(root.querySelector('.now strong')!.textContent).toBe('Pro');
  });

  it('records a payment and clears the amount for the next one', () => {
    const { c } = setup();
    c['payForm'].patchValue({ amount: 1500, method: 'bank_transfer', paidOn: '2026-10-14', reference: 'Ref 7' });
    c['pay']();

    const request = http.expectOne('http://api.test/api/plans/teachers/t1/payments');
    expect(request.request.body).toEqual({ amount: 1500, method: 'bank_transfer', paidOn: '2026-10-14', reference: 'Ref 7' });
    request.flush({ ...onFree, payments: [{ id: 'x', subscriptionId: 's', planName: 'Pro', amount: 1500, paidOn: '2026-10-14', method: 'bank_transfer', reference: 'Ref 7' }] });

    expect(c['payForm'].controls.amount.value).toBeNull();
    expect(c['payForm'].controls.method.value).toBe('bank_transfer');
  });

  it('asks for the amount before sending a payment', () => {
    const { c } = setup();
    c['pay']();

    http.expectNone('http://api.test/api/plans/teachers/t1/payments');
    expect(c['payForm'].controls.amount.hasError('required')).toBe(true);
  });

  it('shows a payment problem beside the field it is about', () => {
    const { c } = setup();
    c['payForm'].patchValue({ amount: 10, paidOn: '2026-10-14' });
    c['pay']();
    http.expectOne('http://api.test/api/plans/teachers/t1/payments').flush({ code: 'paid_on_in_future' }, { status: 400, statusText: 'Bad Request' });

    expect(c['payForm'].controls.paidOn.errors).toEqual({ server: 'error.paid_on_in_future' });
    expect(c['payError']()).toBe('');
  });

  it('shows a problem with no field as a banner above the form', () => {
    const { c } = setup();
    c['payForm'].patchValue({ amount: 10 });
    c['pay']();
    http.expectOne('http://api.test/api/plans/teachers/t1/payments').flush({ code: 'subscription_required' }, { status: 409, statusText: 'Conflict' });

    expect(c['payError']()).toBe('error.subscription_required');
  });
});
