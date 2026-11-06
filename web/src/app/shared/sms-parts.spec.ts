import { fillSample, smsParts } from './sms-parts';

describe('smsParts', () => {
  it('counts one part for a short English text, and the empty box as one', () => {
    expect(smsParts('')).toBe(1);
    expect(smsParts('Fee due today.')).toBe(1);
    expect(smsParts('a'.repeat(160))).toBe(1);
  });

  it('counts two parts once an English text passes what one part holds', () => {
    expect(smsParts('a'.repeat(161))).toBe(2);
    expect(smsParts('a'.repeat(306))).toBe(2);
    expect(smsParts('a'.repeat(307))).toBe(3);
  });

  it('counts a Sinhala or Tamil text in much smaller parts, because a phone fits only 70 letters', () => {
    expect(smsParts('අ'.repeat(70))).toBe(1);
    expect(smsParts('අ'.repeat(71))).toBe(2);
    expect(smsParts('வ'.repeat(134))).toBe(2);
    expect(smsParts('වඅ'.padEnd(135, 'அ'))).toBe(3);
  });

  it('counts one Sinhala letter in an English line as the whole text being Unicode', () => {
    expect(smsParts(`${'a'.repeat(100)}අ`)).toBe(2);
  });
});

describe('fillSample', () => {
  it('shows the teacher their wording as a parent will read it', () => {
    expect(fillSample("{student}'s {class} fee for {month} is {amount}. - {teacher}")).toBe(
      "Amara's Grade 10 Maths fee for March 2027 is LKR 2,000. - K. Perera",
    );
  });

  it('leaves something it has no sample for as it is, so a mistake is visible', () => {
    expect(fillSample('Hello {nobody}')).toBe('Hello {nobody}');
  });
});
