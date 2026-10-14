import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { Exams } from './exams';

const API = 'http://api.test';

describe('Exams', () => {
  let http: HttpTestingController;

  const setup = (classId = '') => {
    TestBed.configureTestingModule({ imports: [Exams], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(Exams);
    if (classId) fixture.componentRef.setInput('classId', classId);
    fixture.detectChanges();
    http.expectOne(`${API}/api/classes/classes?status=all`).flush([{ id: 'c1', title: 'Grade 6 ICT' }]);
    return fixture;
  };

  afterEach(() => http.verify());

  it('lists every exam with how many students are marked and the average', () => {
    const fixture = setup();
    http.expectOne(`${API}/api/exams`).flush([
      { id: 'x1', classId: 'c1', classTitle: 'Grade 6 ICT', name: 'Term test', heldOn: '2026-10-10', maxTotal: 100, parts: 2, students: 10, marked: 8, averagePercent: 62.5 },
      { id: 'x2', classId: 'c1', classTitle: 'Grade 6 ICT', name: 'Quiz', heldOn: '2026-09-10', maxTotal: 20, parts: 1, students: 10, marked: 0, averagePercent: null },
    ]);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Term test');
    expect(text).toContain('62.5%');
    expect(text).toContain('Quiz');
    expect(text).toContain('–');
  });

  it('starts on the class from the address and asks only for its exams', () => {
    const fixture = setup('c1');
    http.expectOne((r) => r.url === `${API}/api/exams` && r.params.get('classId') === 'c1').flush([]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('exams.none');
  });
});
