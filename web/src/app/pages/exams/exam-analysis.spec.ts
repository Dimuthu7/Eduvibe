import { TestBed } from '@angular/core/testing';
import { ExamDetail } from '../../core/api/exams.service';
import { TranslateService } from '../../core/i18n/translate.service';
import { ExamAnalysis, short } from './exam-analysis';

const exam = (over: Partial<ExamDetail['analysis']> = {}): ExamDetail => ({
  id: 'x1', classId: 'c1', classTitle: 'Grade 6 ICT', name: 'Term test', heldOn: '2026-10-10', maxTotal: 80, publishedAt: null,
  parts: [{ id: 'p1', name: 'MCQ', maxMark: 40 }, { id: 'p2', name: 'Essay', maxMark: 40 }],
  bands: [{ grade: 'A', minPercent: 75 }, { grade: 'S', minPercent: 35 }, { grade: 'W', minPercent: 0 }],
  summary: { students: 5, marked: 4, averagePercent: 60 },
  analysis: {
    sat: 4, absent: 1, highest: 72, lowest: 20, average: 48.5, median: 51, passMark: 35, passed: 3, passRate: 75,
    grades: [{ grade: 'A', count: 1 }, { grade: 'S', count: 2 }, { grade: 'W', count: 1 }],
    parts: [{ partId: 'p1', name: 'MCQ', maxMark: 40, average: 30 }, { partId: 'p2', name: 'Essay', maxMark: 40, average: null }],
    ...over,
  },
  students: [],
});

describe('ExamAnalysis', () => {
  const render = (detail: ExamDetail) => {
    TestBed.configureTestingModule({ imports: [ExamAnalysis] });
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(ExamAnalysis);
    fixture.componentRef.setInput('exam', detail);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('shows the pass rate, the failing grade and the four figures with their percentages', () => {
    const el = render(exam());
    expect(el.querySelector('.pass strong')!.textContent).toBe('75%');
    expect(el.textContent).toContain('3 / 4');
    expect(el.textContent).toContain('35% (W exams.fails)');
    expect(el.textContent).toContain('exams.absent 1');
    const tiles = Array.from(el.querySelectorAll('.tile')).map((t) => Array.from(t.children).map((c) => c.textContent!.trim()).join(' | '));
    expect(tiles).toEqual([
      'exams.highest | 72 / 80 | 90%', 'exams.lowest | 20 / 80 | 25%', 'exams.average | 48.5 / 80 | 60.63%', 'exams.median | 51 / 80 | 63.75%',
    ]);
  });

  it('draws a bar per grade against the most common grade, and part averages against each maximum', () => {
    const el = render(exam());
    const fills = Array.from(el.querySelectorAll<HTMLElement>('.fill')).map((f) => f.style.width);
    expect(fills).toEqual(['50%', '100%', '50%', '75%', '0%']);
    expect(el.textContent).toContain('30 / 40');
  });

  it('says so when nobody has every part marked', () => {
    const el = render(exam({ sat: 0, highest: null, lowest: null, average: null, median: null, passed: 0, passRate: null }));
    expect(el.textContent).toContain('exams.analysis_none');
    expect(el.querySelector('.tile')).toBeNull();
  });

  it('says everyone passes when the grading has one grade', () => {
    expect(render(exam({ passMark: null })).textContent).toContain('exams.everyone_passes');
  });

  it('writes numbers without needless decimals', () => {
    expect(short(70)).toBe('70');
    expect(short(65.5)).toBe('65.5');
    expect(short(60.625)).toBe('60.63');
  });
});
