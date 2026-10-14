import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { Grading } from './grading';

const API = 'http://api.test';

describe('Grading', () => {
  let http: HttpTestingController;

  const setup = async () => {
    TestBed.configureTestingModule({ imports: [Grading], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(Grading);
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne(`${API}/api/exams/grade-scheme`).flush({
      name: 'O/L', bands: [{ grade: 'W', minPercent: 0 }, { grade: 'A', minPercent: 75 }],
    });
    fixture.detectChanges();
    const component = fixture.componentInstance as unknown as {
      bands: import('@angular/forms').FormArray;
      add: () => void;
      remove: (i: number) => void;
      useDefault: () => void;
      submit: () => void;
    };
    return { fixture, component };
  };

  afterEach(() => http.verify());

  it('lists the bands highest first', async () => {
    const { component } = await setup();
    expect(component.bands.value).toEqual([{ grade: 'A', minPercent: 75 }, { grade: 'W', minPercent: 0 }]);
  });

  it('sends the edited bands', async () => {
    const { component } = await setup();
    component.add();
    component.bands.at(2).patchValue({ grade: ' B ', minPercent: '50' });
    component.submit();
    const req = http.expectOne(`${API}/api/exams/grade-scheme`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ bands: [{ grade: 'A', minPercent: 75 }, { grade: 'W', minPercent: 0 }, { grade: 'B', minPercent: 50 }] });
    req.flush({ name: 'O/L', bands: [{ grade: 'A', minPercent: 75 }, { grade: 'B', minPercent: 50 }, { grade: 'W', minPercent: 0 }] });
    expect(component.bands.length).toBe(3);
  });

  it('will not send a grade with no name or a percentage over 100', async () => {
    const { component } = await setup();
    component.bands.at(0).patchValue({ grade: ' ', minPercent: 101 });
    component.submit();
    http.expectNone(`${API}/api/exams/grade-scheme`);
    expect(component.bands.at(0).get('grade')!.errors).toMatchObject({ required: true });
    expect(component.bands.at(0).get('minPercent')!.errors).toMatchObject({ max: expect.anything() });
  });

  it('can go back to the O/L default', async () => {
    const { component } = await setup();
    component.useDefault();
    expect(component.bands.value.map((b: { grade: string }) => b.grade)).toEqual(['A', 'B', 'C', 'S', 'W']);
  });

  it('shows a refusal from the server in the banner', async () => {
    const { fixture, component } = await setup();
    component.submit();
    http.expectOne(`${API}/api/exams/grade-scheme`).flush({ code: 'band_zero_missing' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('error.band_zero_missing');
  });
});
