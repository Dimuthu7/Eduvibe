/** Letters that fit in one paid part of a text message, and in each part once it needs more than one. */
const GSM_SINGLE = 160;
const GSM_MULTI = 153;
const UNICODE_SINGLE = 70;
const UNICODE_MULTI = 67;

/**
 * How many paid parts a text message takes. A phone fits 160 Latin letters in one part but only 70 Sinhala or Tamil ones, and a teacher pays
 * per part, so the Messages page counts them while they type. The API counts the same way when the message actually goes out.
 */
export function smsParts(text: string): number {
  if (text.length === 0) {
    return 1;
  }

  const unicode = [...text].some((c) => c.charCodeAt(0) > 127);
  const single = unicode ? UNICODE_SINGLE : GSM_SINGLE;
  const multi = unicode ? UNICODE_MULTI : GSM_MULTI;
  return text.length <= single ? 1 : Math.ceil(text.length / multi);
}

/** Sample values for a preview, so a teacher sees their wording as a parent will read it. */
export const sampleValues: Record<string, string> = {
  student: 'Amara',
  class: 'Grade 10 Maths',
  month: 'March 2027',
  amount: 'LKR 2,000',
  teacher: 'K. Perera',
  date: 'Mon 15 Mar',
  time: '16:05',
  receipt: 'R-000123',
  percent: '60%',
  weeks: '4',
  exam: 'Term test',
  marks: '78 / 100',
  grade: 'B',
  rank: '3 of 24',
};

/** The wording with its fields filled in with sample values, the way the API fills them in with the real ones. */
export function fillSample(pattern: string): string {
  return pattern.replace(/\{([a-z]+)\}/g, (whole, name: string) => sampleValues[name] ?? whole);
}
