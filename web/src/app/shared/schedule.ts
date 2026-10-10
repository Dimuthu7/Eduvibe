import { Slot } from '../core/api/classes.service';
import { formatTime } from './format';

/** "Mon 4:00 PM – 6:00 PM · Thu 4:00 PM – 6:00 PM", for a class's weekly times. */
export function slotSummary(slots: readonly Slot[], translate: (key: string) => string): string {
  return slots
    .map((s) => `${translate('day.short.' + s.day)} ${formatTime(s.start)} – ${formatTime(s.end)}`)
    .join(' · ');
}
