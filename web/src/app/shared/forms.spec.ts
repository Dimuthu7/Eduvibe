import { FormControl, FormGroup } from '@angular/forms';
import { applyServerError, fieldErrorKey, filled, nonEmptyArray, phoneValidator } from './forms';

describe('form helpers', () => {
  it.each(['0771234567', '077 123 4567', '+94771234567', '94771234567', '771234567', '011-2345678'])(
    'accepts the phone number %s',
    (phone) => expect(phoneValidator(new FormControl(phone))).toBeNull(),
  );

  it.each(['abc', '12345', '+0771234567', '07712345678901'])('rejects the phone number %s', (phone) =>
    expect(phoneValidator(new FormControl(phone))).toEqual({ phone: true }),
  );

  it('treats an empty phone as valid so required can decide', () => {
    expect(phoneValidator(new FormControl(''))).toBeNull();
  });

  it('does not count spaces as filled in', () => {
    expect(filled(new FormControl('   '))).toEqual({ required: true });
    expect(filled(new FormControl(' Nimal '))).toBeNull();
  });

  it('requires at least one selection', () => {
    expect(nonEmptyArray(new FormControl([]))).toEqual({ required: true });
    expect(nonEmptyArray(new FormControl(['a']))).toBeNull();
  });

  it('turns control errors into message keys', () => {
    const control = new FormControl('', filled);
    expect(fieldErrorKey(control)).toBe('validation.required');
    control.setErrors({ email: true });
    expect(fieldErrorKey(control)).toBe('validation.email');
    control.setErrors({ server: 'error.phone_taken' });
    expect(fieldErrorKey(control)).toBe('error.phone_taken');
  });

  it('puts a server error on its field and clears it when the person edits', () => {
    const form = new FormGroup({ phone: new FormControl('0771234567') });
    expect(applyServerError(form, 'phone_taken')).toBe(true);
    expect(form.controls.phone.errors).toEqual({ server: 'error.phone_taken' });
    form.controls.phone.setValue('0771234568');
    expect(form.controls.phone.errors).toBeNull();
  });

  it('leaves errors it cannot place to the caller', () => {
    const form = new FormGroup({ phone: new FormControl('') });
    expect(applyServerError(form, 'something_else')).toBe(false);
    expect(applyServerError(form, null)).toBe(false);
  });
});
