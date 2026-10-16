import { TimetableEntry } from '../../../core/api/classes.service';
import { minutes } from '../../../shared/format';

/** The next weekly class: how many days ahead (0 is later today) and which one. */
export interface NextClass {
  daysAhead: number;
  entry: TimetableEntry;
}

/**
 * The first class on the weekly timetable that starts after the given moment, looking a full week ahead.
 * Day is 1 (Monday) to 7 (Sunday) and minutes count from midnight, both in Sri Lanka.
 */
export function nextClass(entries: readonly TimetableEntry[], now: { day: number; minutes: number }): NextClass | null {
  for (let daysAhead = 0; daysAhead <= 7; daysAhead++) {
    const day = ((now.day - 1 + daysAhead) % 7) + 1;
    const found = entries
      .filter((e) => e.day === day && (daysAhead > 0 || minutes(e.start) > now.minutes))
      .sort((a, b) => minutes(a.start) - minutes(b.start) || a.title.localeCompare(b.title))[0];
    if (found) return { daysAhead, entry: found };
  }
  return null;
}

/** Where a session stands against the clock: over, on now, the next to start, or still later. */
export type SessionState = 'over' | 'now' | 'next' | 'later';

/** The state of each of the day's sessions, in the order given (earliest first). Only the first to start after now is "next". */
export function sessionStates(sessions: readonly { start: string; end: string }[], nowMinutes: number): SessionState[] {
  let nextGiven = false;
  return sessions.map((s) => {
    if (minutes(s.end) <= nowMinutes) return 'over';
    if (minutes(s.start) <= nowMinutes) return 'now';
    if (nextGiven) return 'later';
    nextGiven = true;
    return 'next';
  });
}
