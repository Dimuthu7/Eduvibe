import { defaultLayout, isDefaultLayout, moveWidget, resolveLayout, setEnabled, WidgetDef } from './layout';

const def = (key: string, defaultOn = true): WidgetDef => ({ key, icon: 'x', title: `t.${key}`, about: `a.${key}`, defaultOn });
const catalog = [def('a'), def('b'), def('c', false)];

describe('dashboard layout', () => {
  it('starts as the catalog order with each default', () => {
    expect(defaultLayout(catalog)).toEqual([
      { key: 'a', enabled: true },
      { key: 'b', enabled: true },
      { key: 'c', enabled: false },
    ]);
    expect(resolveLayout([], catalog)).toEqual(defaultLayout(catalog));
  });

  it('keeps the saved order and choices', () => {
    const saved = [
      { key: 'c', enabled: true },
      { key: 'a', enabled: false },
      { key: 'b', enabled: true },
    ];
    expect(resolveLayout(saved, catalog)).toEqual(saved);
  });

  it('drops widgets that no longer exist and repeats, and adds new ones at the end with their default', () => {
    const saved = [
      { key: 'gone', enabled: true },
      { key: 'b', enabled: false },
      { key: 'b', enabled: true },
    ];
    expect(resolveLayout(saved, catalog)).toEqual([
      { key: 'b', enabled: false },
      { key: 'a', enabled: true },
      { key: 'c', enabled: false },
    ]);
  });

  it('moves a widget up or down and stops at the ends', () => {
    const list = defaultLayout(catalog);
    expect(moveWidget(list, 1, -1).map((w) => w.key)).toEqual(['b', 'a', 'c']);
    expect(moveWidget(list, 1, 1).map((w) => w.key)).toEqual(['a', 'c', 'b']);
    expect(moveWidget(list, 0, -1).map((w) => w.key)).toEqual(['a', 'b', 'c']);
    expect(moveWidget(list, 2, 1).map((w) => w.key)).toEqual(['a', 'b', 'c']);
    expect(list.map((w) => w.key)).toEqual(['a', 'b', 'c']);
  });

  it('switches one widget without touching the others', () => {
    const next = setEnabled(defaultLayout(catalog), 'c', true);
    expect(next.map((w) => w.enabled)).toEqual([true, true, true]);
  });

  it('knows when a list is just the default', () => {
    expect(isDefaultLayout(defaultLayout(catalog), catalog)).toBe(true);
    expect(isDefaultLayout(setEnabled(defaultLayout(catalog), 'a', false), catalog)).toBe(false);
    expect(isDefaultLayout(moveWidget(defaultLayout(catalog), 0, 1), catalog)).toBe(false);
  });
});
