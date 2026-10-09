import { AbstractControl, FormGroup, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * Same rule as the server (PhoneNumber.Normalize): a Sri Lankan number such as 077 123 4567 or
 * +94 77 123 4567, or another international number starting with +. The server stays the judge.
 */
export const phoneValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const text = String(control.value ?? '').trim();
  if (!text) return null;
  if (/[^\d\s\-().+]/.test(text)) return { phone: true };
  const digits = text.replace(/\D/g, '');
  if (text.startsWith('+')) return digits.length >= 8 && digits.length <= 15 && digits[0] !== '0' ? null : { phone: true };
  if (digits.startsWith('00')) return digits.length >= 10 && digits.length <= 17 ? null : { phone: true };
  if (digits.startsWith('0') && digits.length === 10) return null;
  if (digits.startsWith('94') && digits.length === 11) return null;
  if (digits.length === 9) return null;
  return { phone: true };
};

/** Requires at least one selected value (for multi-selects). */
export const nonEmptyArray: ValidatorFn = (control) =>
  Array.isArray(control.value) && control.value.length > 0 ? null : { required: true };

/** Like Validators.required, but spaces alone do not count as filled in. */
export const filled: ValidatorFn = (control) =>
  String(control.value ?? '').trim() === '' ? { required: true } : null;

/** Message key for the first problem on a control, or '' when it is fine. */
export function fieldErrorKey(control: AbstractControl): string {
  const e = control.errors;
  if (!e) return '';
  if (typeof e['server'] === 'string') return e['server'];
  if (e['required']) return 'validation.required';
  if (e['maxlength']) return 'validation.too_long';
  if (e['email']) return 'validation.email';
  if (e['phone']) return 'validation.phone';
  return 'validation.invalid';
}

/** Server error codes that belong to one field, so the message shows under that field. */
const FIELD_FOR_CODE: Record<string, string> = {
  first_name_required: 'firstName',
  last_name_required: 'lastName',
  phone_invalid: 'phone',
  phone_taken: 'phone',
  district_invalid: 'district',
  stream_invalid: 'streamId',
  subjects_required: 'subjectIds',
  subject_invalid: 'subjectIds',
  name_required: 'name',
  name_taken: 'name',
};

/**
 * Puts a server rejection beside the field it is about. Returns true when it did; the caller shows
 * anything else (returns false) in a banner. The error clears as soon as the person edits the field.
 */
export function applyServerError(form: FormGroup, code: string | null): boolean {
  const field = code ? FIELD_FOR_CODE[code] : undefined;
  const control = field ? form.get(field) : null;
  if (!code || !control) return false;
  control.setErrors({ server: `error.${code}` });
  control.markAsTouched();
  return true;
}

/** Shows every problem at once, for example when the person presses the main button on an empty form. */
export function touchAll(form: FormGroup): void {
  form.markAllAsTouched();
}
