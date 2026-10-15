import { TestBed } from '@angular/core/testing';
import { TranslateService } from '../../core/i18n/translate.service';
import messages from '../../../../public/i18n/en.json';
import { Help, HELP_TOPICS } from './help';

const en = messages as Record<string, string>;

describe('Help', () => {
  it('has text in en.json for every topic and step', () => {
    const missing = HELP_TOPICS.flatMap((t) => [`help.${t.id}.title`, ...Array.from({ length: t.steps }, (_, i) => `help.${t.id}.${i + 1}`)]).filter((k) => !en[k]);
    expect(missing).toEqual([]);
  });

  it('shows every topic with its steps', () => {
    TestBed.configureTestingModule({ imports: [Help] });
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => en[key] ?? key);
    const fixture = TestBed.createComponent(Help);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    for (const topic of HELP_TOPICS) expect(text).toContain(en[`help.${topic.id}.title`]);
    // The first topic is open; the rest render closed but still in the page.
    expect(text).toContain(en['help.start.1']);
  });
});
