import { Slot } from '../core/api/classes.service';
import { formatTime } from './format';

/** Days that share a time are named together: "Mon–Fri", or "Mon, Thu" when they are not in a row. */
function dayList(days: number[], translate: (key: string) => string): string {
  const name = (day: number) => translate('day.short.' + day);
  const runs: number[][] = [];
  for (const day of [...days].sort((a, b) => a - b)) {
    const last = runs[runs.length - 1];
    if (last && day === last[last.length - 1] + 1) last.push(day);
    else runs.push([day]);
  }

  // Three or more in a row read better as a range; one or two are listed.
  return runs.map((run) => (run.length >= 3 ? `${name(run[0])}–${name(run[run.length - 1])}` : run.map(name).join(', '))).join(', ');
}

/** "Mon, Thu 4:00 PM – 6:00 PM · Sat 9:00 AM – 11:00 AM", for a class's weekly times: days that meet at the same time come first. */
export function slotSummary(slots: readonly Slot[], translate: (key: string) => string): string {
  const groups = new Map<string, { days: number[]; start: string; end: string }>();
  for (const slot of [...slots].sort((a, b) => a.day - b.day)) {
    const key = `${slot.start}|${slot.end}`;
    const group = groups.get(key) ?? { days: [], start: slot.start, end: slot.end };
    group.days.push(slot.day);
    groups.set(key, group);
  }

  return [...groups.values()].map((g) => `${dayList(g.days, translate)} ${formatTime(g.start)} – ${formatTime(g.end)}`).join(' · ');
}
