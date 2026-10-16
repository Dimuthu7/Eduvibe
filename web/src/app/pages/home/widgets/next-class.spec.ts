import { TimetableEntry } from '../../../core/api/classes.service';
import { nextClass, sessionStates } from './next-class';

const entry = (title: string, day: number, start: string): TimetableEntry => ({
  classId: title, title, subjectId: 's', day, start, end: '23:00', placeType: 'venue', placeId: 'v', placeName: 'Hall', instituteId: null, overlapsWith: [],
});

describe('nextClass', () => {
  const week = [entry('Mon maths', 1, '16:00'), entry('Thu science', 4, '09:00'), entry('Thu maths', 4, '16:00')];

  it('finds a later class today', () => {
    expect(nextClass(week, { day: 4, minutes: 10 * 60 })).toMatchObject({ daysAhead: 0, entry: { title: 'Thu maths' } });
  });

  it('moves to a following day when today is over', () => {
    expect(nextClass(week, { day: 4, minutes: 17 * 60 })).toMatchObject({ daysAhead: 4, entry: { title: 'Mon maths' } });
    expect(nextClass(week, { day: 2, minutes: 8 * 60 })).toMatchObject({ daysAhead: 2, entry: { title: 'Thu science' } });
  });

  it('comes back round to the same weekday next week', () => {
    expect(nextClass([entry('Thu only', 4, '09:00')], { day: 4, minutes: 12 * 60 })).toMatchObject({ daysAhead: 7 });
  });

  it('is null without a timetable', () => {
    expect(nextClass([], { day: 1, minutes: 0 })).toBeNull();
  });
});

describe('sessionStates', () => {
  const s = (start: string, end: string) => ({ start, end });

  it('marks what is over, what is on, the next one and the rest', () => {
    const day = [s('08:00', '09:00'), s('10:00', '12:00'), s('14:00', '15:00'), s('16:00', '17:00')];
    expect(sessionStates(day, 11 * 60)).toEqual(['over', 'now', 'next', 'later']);
    expect(sessionStates(day, 7 * 60)).toEqual(['next', 'later', 'later', 'later']);
    expect(sessionStates(day, 18 * 60)).toEqual(['over', 'over', 'over', 'over']);
  });
});
