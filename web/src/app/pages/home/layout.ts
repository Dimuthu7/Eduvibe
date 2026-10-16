import { WidgetSetting } from '../../core/api/dashboard.service';

/** One widget the dashboard can show. A new widget is a new entry here, a component, and its translation keys. */
export interface WidgetDef {
  key: string;
  icon: string;
  /** Translation keys for the name and the one line that says what it shows. */
  title: string;
  about: string;
  /** On for a teacher who has not customised yet. */
  defaultOn: boolean;
}

/** Every widget, in the order a teacher who has not customised sees them. */
export const WIDGETS: readonly WidgetDef[] = [
  { key: 'today', icon: 'event', title: 'dashboard.today.title', about: 'dashboard.today.about', defaultOn: true },
  { key: 'joins', icon: 'person_add', title: 'dashboard.joins.title', about: 'dashboard.joins.about', defaultOn: true },
  { key: 'fees', icon: 'payments', title: 'dashboard.fees.title', about: 'dashboard.fees.about', defaultOn: true },
  { key: 'attendance', icon: 'event_busy', title: 'dashboard.attendance.title', about: 'dashboard.attendance.about', defaultOn: true },
  { key: 'exams', icon: 'trending_up', title: 'dashboard.exams.title', about: 'dashboard.exams.about', defaultOn: true },
  { key: 'institutes', icon: 'apartment', title: 'dashboard.institutes.title', about: 'dashboard.institutes.about', defaultOn: false },
];

export const defaultLayout = (catalog: readonly WidgetDef[] = WIDGETS): WidgetSetting[] =>
  catalog.map((w) => ({ key: w.key, enabled: w.defaultOn }));

/**
 * The saved layout made whole: widgets the app no longer has are dropped, and widgets the teacher has never seen
 * (added since they saved) join at the end with their own default.
 */
export function resolveLayout(saved: readonly WidgetSetting[], catalog: readonly WidgetDef[] = WIDGETS): WidgetSetting[] {
  const known = new Set(catalog.map((w) => w.key));
  const seen = new Set<string>();
  const result: WidgetSetting[] = [];
  for (const s of saved) {
    if (known.has(s.key) && !seen.has(s.key)) {
      seen.add(s.key);
      result.push({ key: s.key, enabled: s.enabled });
    }
  }
  for (const w of catalog) if (!seen.has(w.key)) result.push({ key: w.key, enabled: w.defaultOn });
  return result;
}

/** The list with one widget moved up (-1) or down (1). Moving past either end changes nothing. */
export function moveWidget(list: readonly WidgetSetting[], index: number, step: -1 | 1): WidgetSetting[] {
  const target = index + step;
  if (index < 0 || index >= list.length || target < 0 || target >= list.length) return [...list];
  const next = [...list];
  [next[index], next[target]] = [next[target], next[index]];
  return next;
}

/** The list with one widget switched on or off. */
export const setEnabled = (list: readonly WidgetSetting[], key: string, enabled: boolean): WidgetSetting[] =>
  list.map((w) => (w.key === key ? { ...w, enabled } : w));

/** True when the list is exactly what a teacher who never customised sees, so saving it can simply clear the saved layout. */
export function isDefaultLayout(list: readonly WidgetSetting[], catalog: readonly WidgetDef[] = WIDGETS): boolean {
  const base = defaultLayout(catalog);
  return list.length === base.length && list.every((w, i) => w.key === base[i].key && w.enabled === base[i].enabled);
}
