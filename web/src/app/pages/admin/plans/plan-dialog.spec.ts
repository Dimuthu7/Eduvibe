import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Plan } from '../../../core/api/plans.service';
import { AppConfig } from '../../../core/config/app-config';
import { TranslateService } from '../../../core/i18n/translate.service';
import { PlanDialog, toPlanInput } from './plan-dialog';

const pro: Plan = {
  id: 'p1',
  name: 'Pro',
  description: null,
  isDefault: false,
  isActive: true,
  monthlyPrice: 1500,
  features: { whatsapp: true, drive_materials: false, parent_portal: true, advanced_analytics: false },
  limits: { max_classes: null, max_students_per_class: 40, whatsapp_per_month: 500 },
};

describe('toPlanInput', () => {
  it('trims the name, turns a blank description into null and a blank limit into no cap', () => {
    const input = toPlanInput({
      name: '  Gold ',
      description: '   ',
      monthlyPrice: null,
      isActive: true,
      features: { whatsapp: true },
      limits: { max_classes: null, max_students_per_class: '' as unknown as number, whatsapp_per_month: 120 },
    });

    expect(input).toEqual({
      name: 'Gold',
      description: null,
      monthlyPrice: 0,
      isActive: true,
      features: { whatsapp: true },
      limits: { max_classes: null, max_students_per_class: null, whatsapp_per_month: 120 },
    });
  });
});

describe('PlanDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  const setup = (plan: Plan | null) => {
    TestBed.configureTestingModule({
      imports: [PlanDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close } },
        { provide: MAT_DIALOG_DATA, useValue: plan },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue('http://api.test');
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(PlanDialog);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const submit = () => {
      root.querySelector('form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };
    return { fixture, root, submit, form: fixture.componentInstance['form'] };
  };

  beforeEach(() => close.mockReset());
  afterEach(() => http.verify());

  it('needs a name', () => {
    const { root, submit } = setup(null);
    submit();

    http.expectNone('http://api.test/api/plans/plans');
    expect(Array.from(root.querySelectorAll('mat-error')).map((e) => e.textContent!.trim())).toContain('validation.required');
  });

  it('creates a plan with the features switched on and the caps typed in', () => {
    const { form, submit } = setup(null);
    form.patchValue({ name: 'Gold', monthlyPrice: 2000, features: { whatsapp: true }, limits: { max_classes: 10 } });
    submit();

    const request = http.expectOne('http://api.test/api/plans/plans');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.features.whatsapp).toBe(true);
    expect(request.request.body.features.parent_portal).toBe(false);
    expect(request.request.body.limits).toEqual({ max_classes: 10, max_students_per_class: null, whatsapp_per_month: null });
    request.flush({ ...pro, id: 'p2', name: 'Gold' });
    expect(close).toHaveBeenCalledWith(expect.objectContaining({ id: 'p2' }));
  });

  it('edits the plan it was opened with', () => {
    const { form, root, submit } = setup(pro);
    expect(form.controls.name.value).toBe('Pro');
    expect(form.controls.limits.controls['max_students_per_class'].value).toBe(40);
    expect(root.textContent).toContain('plans.edit');

    form.controls.limits.controls['max_classes'].setValue(25);
    submit();

    const request = http.expectOne('http://api.test/api/plans/plans/p1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.limits.max_classes).toBe(25);
    request.flush(pro);
  });

  it('shows a name that is already taken beside the name field', () => {
    const { form, root, submit } = setup(null);
    form.patchValue({ name: 'Pro' });
    submit();
    http.expectOne('http://api.test/api/plans/plans').flush({ code: 'name_taken' }, { status: 409, statusText: 'Conflict' });

    expect(form.controls.name.errors).toEqual({ server: 'error.name_taken' });
    expect(close).not.toHaveBeenCalled();
    expect(root.querySelector('.banner')).toBeNull();
  });

  it('does not offer to switch off the default plan', () => {
    const { root } = setup({ ...pro, isDefault: true });

    expect(root.textContent).not.toContain('plans.active');
  });
});
