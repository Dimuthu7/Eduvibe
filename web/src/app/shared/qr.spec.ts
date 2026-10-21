import { CARD_PREFIX, CARDS_PER_PAGE } from './card-pdf';
import { qrModules } from './qr';

describe('QR codes', () => {
  it('makes a square grid that is the same every time for the same text', async () => {
    const a = await qrModules(`${CARD_PREFIX}4LngDA_6VU6_6FgReyAECg`);
    const b = await qrModules(`${CARD_PREFIX}4LngDA_6VU6_6FgReyAECg`);
    expect(a.length).toBeGreaterThanOrEqual(21);
    expect(a.every((row) => row.length === a.length)).toBe(true);
    expect(a).toEqual(b);
    expect(a.flat().some(Boolean)).toBe(true);
  });

  it('has the three corner finder squares every scanner looks for', async () => {
    const m = await qrModules(`${CARD_PREFIX}abc`);
    const n = m.length;
    for (const [r, c] of [[0, 0], [0, n - 7], [n - 7, 0]]) {
      expect(m[r][c] && m[r][c + 6] && m[r + 6][c] && m[r + 6][c + 6]).toBe(true);
      expect(m[r + 3][c + 3]).toBe(true);
      expect(m[r + 1][c + 1]).toBe(false);
    }
  });

  it('puts ten cards on an A4 page and starts server codes with the same prefix', () => {
    expect(CARDS_PER_PAGE).toBe(10);
    expect(CARD_PREFIX).toBe('EV1:');
  });
});
