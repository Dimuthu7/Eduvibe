import { moveLegacyKeys } from './legacy-keys';

describe('moveLegacyKeys', () => {
  beforeEach(() => localStorage.clear());

  it('moves old keys to the new names', () => {
    localStorage.setItem('eduvibe.session', 'tokens');
    localStorage.setItem('eduvibe.theme', 'dark');
    moveLegacyKeys();
    expect(localStorage.getItem('gurubook.session')).toBe('tokens');
    expect(localStorage.getItem('gurubook.theme')).toBe('dark');
    expect(localStorage.getItem('eduvibe.session')).toBeNull();
  });

  it('keeps a value already saved under the new name', () => {
    localStorage.setItem('eduvibe.language', 'si');
    localStorage.setItem('gurubook.language', 'ta');
    moveLegacyKeys();
    expect(localStorage.getItem('gurubook.language')).toBe('ta');
    expect(localStorage.getItem('eduvibe.language')).toBeNull();
  });
});
