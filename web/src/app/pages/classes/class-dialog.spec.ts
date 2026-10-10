import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { CatalogStore } from '../../core/api/catalog.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { ClassDialog } from './class-dialog';

const API = 'http://api.test';

describe('ClassDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  beforeEach(() => {
    close.mockReset();
    TestBed.configureTestingModule({
      imports: [ClassDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MatDialogRef, useValue: { close } }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const store = TestBed.inject(CatalogStore);
    vi.spyOn(store, 'ensureLoaded').mockImplementation(() => {
      store.allStreams.set([{ id: 's1', name: 'A/L', sortOrder: 1, isActive: true }]);
      store.allSubjects.set([{ id: 'm1', name: 'Mathematics', sortOrder: 1, isActive: true }]);
    });
  });

  afterEach(() => http.verify());

  const setup = () => {
    const fixture = TestBed.createComponent(ClassDialog);
    fixture.detectChanges();
    http.expectOne(`${API}/api/classes/my-institutes`).flush([{ id: 'i1', name: 'Bright Minds' }]);
    http.expectOne(`${API}/api/classes/venues`).flush([{ id: 'v1', name: 'Home', isActive: true }]);
    fixture.detectChanges();
    const form = fixture.componentInstance['form'];
    const submit = () => {
      (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
      fixture.detectChanges();
    };
    const fill = (place: string) =>
      form.patchValue({ title: '2027 A/L Maths', subjectId: 'm1', streamId: 's1', examYear: 2027, medium: 'English', place, monthlyFee: 2500 });
    const slot = (day: number, start: string, end: string) => form.controls.slots.at(0).patchValue({ day, start, end });
    return { fixture, form, submit, fill, slot };
  };

  it('offers the teacher\'s institutes and venues as places', () => {
    const { fixture } = setup();
    expect(fixture.componentInstance['placeOptions']().map((o: { label: string }) => o.label)).toEqual([
      'Bright Minds (classes.place_institute)',
      'Home (classes.place_venue)',
    ]);
  });

  it('shows what is missing and sends nothing when submitted empty', () => {
    const { fixture, submit } = setup();
    submit();
    http.expectNone(`${API}/api/classes/classes`);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('mat-error').length).toBeGreaterThanOrEqual(6);
  });

  it('rejects a slot that ends before it starts and slots that overlap', () => {
    const { form, submit, fill, slot } = setup();
    fill('i:i1');
    slot(1, '18:00', '16:00');
    submit();
    http.expectNone(`${API}/api/classes/classes`);
    expect(form.controls.slots.at(0).hasError('slotOrder')).toBe(true);
  });

  it('rejects two slots on the same day that overlap', () => {
    const { fixture, form, submit, fill, slot } = setup();
    fill('i:i1');
    slot(1, '16:00', '18:00');
    fixture.componentInstance['addSlot']();
    form.controls.slots.at(1).patchValue({ day: 1, start: '17:00', end: '19:00' });
    submit();
    http.expectNone(`${API}/api/classes/classes`);
    expect(form.controls.slots.hasError('slotsOverlap')).toBe(true);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('validation.slots_overlap');
  });

  it('sends an institute class with its weekly slots', () => {
    const { submit, fill, slot } = setup();
    fill('i:i1');
    slot(3, '16:00', '18:00');
    submit();
    const request = http.expectOne(`${API}/api/classes/classes`);
    expect(request.request.body).toEqual({
      title: '2027 A/L Maths', subjectId: 'm1', streamId: 's1', examYear: 2027, medium: 'English',
      instituteId: 'i1', venueId: null, monthlyFee: 2500, slots: [{ day: 3, start: '16:00', end: '18:00' }],
    });
    request.flush({ id: 'c1' });
    expect(close).toHaveBeenCalledWith({ id: 'c1' });
  });

  it('sends a venue class with the venue id', () => {
    const { submit, fill, slot } = setup();
    fill('v:v1');
    slot(1, '09:00', '10:00');
    submit();
    const request = http.expectOne(`${API}/api/classes/classes`);
    expect(request.request.body).toMatchObject({ instituteId: null, venueId: 'v1' });
    request.flush({ id: 'c2' });
  });

  it('shows a rejected place under the place field', () => {
    const { form, submit, fill, slot } = setup();
    fill('i:i1');
    slot(1, '09:00', '10:00');
    submit();
    http.expectOne(`${API}/api/classes/classes`).flush({ code: 'place_invalid' }, { status: 400, statusText: 'Bad Request' });
    expect(form.controls.place.errors).toEqual({ server: 'error.place_invalid' });
    expect(close).not.toHaveBeenCalled();
  });
});
