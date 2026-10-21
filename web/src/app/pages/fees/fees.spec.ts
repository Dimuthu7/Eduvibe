import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { monthOf, todayColombo } from '../../shared/format';
import { WhatsAppActions } from '../../shared/whatsapp-actions';
import { Fees } from './fees';

const API = 'http://api.test';
const month = monthOf(todayColombo());
const totals = { paid: 1, partlyPaid: 0, unpaid: 2, freeCard: 0, due: 4500, collected: 1500, outstanding: 3000 };

describe('Fees', () => {
  let http: HttpTestingController;

  const setup = (classId: string) => {
    TestBed.configureTestingModule({ imports: [Fees], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(Fees);
    fixture.componentRef.setInput('classId', classId);
    fixture.detectChanges();
    http.expectOne(`${API}/api/classes/classes?status=active`).flush([{ id: 'c1', title: 'Grade 6 ICT' }]);
    return fixture;
  };

  // Avatars ask once which students have a photo; none do here.
  const settlePhotos = () => http.match((r) => r.url.endsWith('/api/students/photos')).forEach((r) => r.flush([]));

  afterEach(() => {
    settlePhotos();
    http.verify();
  });

  it('shows each class and the grand totals for "all classes"', () => {
    const fixture = setup('all');
    http.expectOne(`${API}/api/fees/overview?month=${month}`).flush({
      month, totals, classes: [{ classId: 'c1', title: 'Grade 6 ICT', isArchived: false, totals }],
    });
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Grade 6 ICT');
    expect(text).toContain('fees.collected');
    // The Excel export is for one class, so it is not offered here.
    const html = (fixture.nativeElement as HTMLElement).innerHTML;
    expect(html).not.toContain('export.button');
    expect(html).not.toContain('whatsapp.remind');
  });

  it('shows the students of one class and offers the Excel export', () => {
    const fixture = setup('c1');
    http.expectOne(`${API}/api/fees/classes/c1?month=${month}`).flush({
      classId: 'c1', month, totals,
      rows: [{ enrolmentId: 'e1', indexNumber: 1, fullName: 'Amara Silva', status: 'Unpaid', due: 1500, paid: 0 }],
    });
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Amara Silva');
    expect(text).toContain('fees.status.Unpaid');
    expect((fixture.nativeElement as HTMLElement).innerHTML).toContain('export.button');
  });

  it('offers WhatsApp reminders for the class and month shown', () => {
    const fixture = setup('c1');
    http.expectOne(`${API}/api/fees/classes/c1?month=${month}`).flush({ classId: 'c1', month, totals, rows: [] });
    fixture.detectChanges();
    const open = vi.spyOn(TestBed.inject(WhatsAppActions), 'open').mockResolvedValue();
    const button = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.getAttribute('aria-label') === 'whatsapp.remind')!;
    button.click();
    expect(open).toHaveBeenCalledWith(expect.objectContaining({ path: `/api/fees/classes/c1/whatsapp?month=${month}`, title: 'whatsapp.fees_title' }));
  });
});
