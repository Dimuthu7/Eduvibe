import { TestBed } from '@angular/core/testing';
import { TranslateService } from '../core/i18n/translate.service';
import { MonthNav } from './month-nav';

describe('MonthNav', () => {
  const setup = (month: string, current = '2026-10') => {
    TestBed.configureTestingModule({ imports: [MonthNav] });
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(MonthNav);
    fixture.componentRef.setInput('month', month);
    fixture.componentRef.setInput('current', current);
    const changes: string[] = [];
    fixture.componentInstance.monthChange.subscribe((m) => changes.push(m));
    fixture.detectChanges();
    const buttons = () => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
    return { fixture, changes, buttons };
  };

  it('steps back a month, across a year end', () => {
    const { changes, buttons } = setup('2027-01', '2027-03');
    buttons()[0].click();
    expect(changes).toEqual(['2026-12']);
  });

  it('cannot go past the current month', () => {
    const { changes, buttons } = setup('2026-10');
    const next = buttons()[1];
    expect(next.disabled).toBe(true);
    next.click();
    expect(changes).toEqual([]);
  });

  it('steps forward and offers a way back to this month from an older one', () => {
    const { changes, buttons } = setup('2026-08');
    buttons()[1].click();
    buttons()[2].click();
    expect(changes).toEqual(['2026-09', '2026-10']);
  });

  it('stops three years back', () => {
    const { buttons } = setup('2023-10');
    expect(buttons()[0].disabled).toBe(true);
  });
});
