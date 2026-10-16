import en from '../../../../public/i18n/en.json';
import si from '../../../../public/i18n/si.json';
import { LANGUAGES } from './language';

const english = en as Record<string, string>;
const sinhala = si as Record<string, string>;
const placeholders = (text: string): string[] => (text.match(/\{\w+\}/g) ?? []).sort();

/**
 * Screens only the Super Admin sees. The Super Admin always uses English and has no language menu, so these have no Sinhala; if one is
 * ever shown in Sinhala it falls back to the English text.
 */
const ADMIN_ONLY =
  /^(teachers\.|institutes\.|catalog\.|plans\.|teacher_plan\.|audit\.|privacy\.|nav\.(teachers|institutes|plans|catalog|audit|dashboard)$|dashboard\.(title|subtitle|teachers|students|classes|institutes|inactive|by_plan|no_plans|plan_note)$|error\.(unknown_teacher|name_taken|price_invalid|description_too_long|plan_invalid|dates_invalid|reference_too_long|default_plan_active|subscription_required|feature_invalid|limit_invalid|confirm_mismatch|teacher_still_active|teacher_not_found|student_not_found)$)/;

describe('Sinhala text', () => {
  it('has every teacher and student screen translated', () => {
    const missing = Object.keys(english).filter((key) => !(key in sinhala) && !ADMIN_ONLY.test(key));
    expect(missing).toEqual([]);
  });

  it('has no text the English file does not have', () => {
    expect(Object.keys(sinhala).filter((key) => !(key in english))).toEqual([]);
  });

  it('keeps every {value} the app fills in, and nothing else', () => {
    const wrong = Object.keys(sinhala).filter((key) => JSON.stringify(placeholders(sinhala[key])) !== JSON.stringify(placeholders(english[key] ?? '')));
    expect(wrong).toEqual([]);
  });

  it('has no empty text', () => {
    expect(Object.keys(sinhala).filter((key) => sinhala[key].trim() === '')).toEqual([]);
  });

  it('lists Sinhala in the language menu under its own name', () => {
    expect(LANGUAGES.find((l) => l.code === 'si')?.name).toBe('සිංහල');
  });
});
