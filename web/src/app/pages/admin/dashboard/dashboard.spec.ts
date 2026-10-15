import { splitStats } from './dashboard';

describe('splitStats', () => {
  const stats = [
    { key: 'students', value: 120, label: null },
    { key: 'teachers', value: 8, label: null },
    { key: 'teachers_inactive', value: 2, label: null },
    { key: 'plan:a', value: 6, label: 'Free' },
    { key: 'plan:b', value: 2, label: 'Pro' },
    { key: 'institutes', value: 3, label: null },
  ];

  it('puts the headline numbers in a fixed order whatever order the server sends them', () => {
    expect(splitStats(stats).tiles.map((t) => [t.key, t.value])).toEqual([
      ['teachers', 8],
      ['students', 120],
      ['institutes', 3],
    ]);
  });

  it('lists the teachers on each plan with the plan name', () => {
    expect(splitStats(stats).plans.map((p) => [p.label, p.value])).toEqual([
      ['Free', 6],
      ['Pro', 2],
    ]);
  });

  it('reports teachers switched off, and zero when the server does not say', () => {
    expect(splitStats(stats).inactive).toBe(2);
    expect(splitStats([]).inactive).toBe(0);
  });
});
