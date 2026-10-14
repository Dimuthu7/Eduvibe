import { TestBed } from '@angular/core/testing';
import { linePath, TrendChart } from './trend-chart';

describe('linePath', () => {
  it('joins points and starts a new piece after a gap', () => {
    expect(linePath([0, 10, 20, 30], [5, 6, null, 8])).toBe('M0.0 5.0 L10.0 6.0 M30.0 8.0');
    expect(linePath([0, 10], [null, null])).toBe('');
  });
});

describe('TrendChart', () => {
  const render = () => {
    TestBed.configureTestingModule({ imports: [TrendChart] });
    const fixture = TestBed.createComponent(TrendChart);
    fixture.componentRef.setInput('points', [
      { label: 'Unit 1', detail: '1 Sept', value: 60, average: 70 },
      { label: 'Unit 2', detail: '1 Oct', value: null, average: 65 },
      { label: 'Unit 3', detail: '20 Oct', value: 85, average: 72 },
    ]);
    fixture.componentRef.setInput('ownLabel', 'Amara');
    fixture.componentRef.setInput('averageLabel', 'Class average');
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  };

  it('draws both lines with a legend, a gap for the missing mark and the latest value labelled', () => {
    const { el } = render();
    expect(el.querySelector('.legend')!.textContent).toContain('Amara');
    expect(el.querySelector('.legend')!.textContent).toContain('Class average');
    expect(el.querySelectorAll('circle.own')).toHaveLength(2);
    expect(el.querySelectorAll('circle.avg')).toHaveLength(3);
    expect(el.querySelector('path.own')!.getAttribute('d')!.match(/M/g)).toHaveLength(2);
    expect(el.querySelector('text.end')!.textContent).toBe('85%');
    expect(el.querySelector('svg')!.getAttribute('aria-label')).toBe('Unit 1: 60%, Unit 2: –, Unit 3: 85%');
  });

  it('shows an exam\'s values when it is tapped', () => {
    const { fixture, el } = render();
    el.querySelectorAll<SVGRectElement>('rect.hit')[1].dispatchEvent(new Event('pointerdown'));
    fixture.detectChanges();
    const tip = el.querySelector('.tip')!.textContent!;
    expect(tip).toContain('Unit 2');
    expect(tip).toContain('Amara: –');
    expect(tip).toContain('Class average: 65%');
  });
});
