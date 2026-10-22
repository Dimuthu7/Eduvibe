const MOVED = ['theme', 'session', 'language'];

/** Copies settings saved under the old product name to the new keys once, so nobody is signed out by the rename. */
export function moveLegacyKeys(): void {
  try {
    for (const name of MOVED) {
      const old = localStorage.getItem(`eduvibe.${name}`);
      if (old === null) continue;
      if (localStorage.getItem(`gurubook.${name}`) === null) localStorage.setItem(`gurubook.${name}`, old);
      localStorage.removeItem(`eduvibe.${name}`);
    }
  } catch {
    // Storage can be blocked; the app works without it.
  }
}
