import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { Student } from '../../core/api/students.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { PasswordDialog } from './password-dialog';
import { StudentDetail } from './student-detail';

const API = 'http://api.test';

const student = (over: Partial<Student> = {}): Student => ({
  id: 's1', firstName: 'Kamal', lastName: 'Silva', fullName: 'Kamal Silva', phone: '+94771111111', school: null, grade: null,
  guardianName: 'Mrs Fernando', guardianPhone: '+94772222222', hasAccount: true, hasParent: false, enrolments: [], ...over,
});

describe('StudentDetail parent card', () => {
  let http: HttpTestingController;

  const setup = async (record: Student, plan: { enabled: boolean; availableOn: string | null } = { enabled: true, availableOn: null }) => {
    TestBed.configureTestingModule({ imports: [StudentDetail], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(StudentDetail);
    fixture.componentRef.setInput('id', 's1');
    fixture.detectChanges();
    await fixture.whenStable();
    http.match(`${API}/api/students/photos`).forEach((r) => r.flush([]));
    http.expectOne(`${API}/api/students/s1`).flush(record);
    http.expectOne(`${API}/api/plans/mine`).flush({ planName: 'Free', endsOn: null, limits: [], features: [{ key: 'parent_portal', ...plan }] });
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  };

  afterEach(() => {
    http.match(`${API}/api/students/photos`);
    http.verify();
  });

  const text = (fixture: { nativeElement: unknown }) => (fixture.nativeElement as HTMLElement).querySelector('.parent-card')!.textContent!;

  it('offers to invite the parent, and shows the one-time password of a new account', async () => {
    const fixture = await setup(student());
    expect(text(fixture)).toContain('students.parent.intro');
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open');

    const button = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.parent-card button')).find((b) => b.textContent!.includes('students.parent.invite')) as HTMLButtonElement;
    button.click();
    http.expectOne({ method: 'POST', url: `${API}/api/students/s1/parent` }).flush({ name: 'Mrs Fernando', phone: '+94772222222', accountCreated: true, oneTimePassword: 'ABCD-2345' });
    http.match(`${API}/api/students/photos`);
    http.expectOne(`${API}/api/students/s1`).flush(student({ hasParent: true }));
    fixture.detectChanges();

    expect(open).toHaveBeenCalledWith(PasswordDialog, expect.objectContaining({ data: expect.objectContaining({ oneTimePassword: 'ABCD-2345', phone: '+94772222222' }) }));
    expect(text(fixture)).toContain('students.parent.linked');
  });

  it('asks for the guardian phone first, and says when the plan does not have the parent portal', async () => {
    const noPhone = await setup(student({ guardianPhone: null }));
    expect(text(noPhone)).toContain('students.parent.need_phone');
    TestBed.resetTestingModule();

    const locked = await setup(student(), { enabled: false, availableOn: 'Pro' });
    expect(text(locked)).toContain('students.parent.locked');
    expect(text(locked)).toContain('Pro');
    expect((locked.nativeElement as HTMLElement).querySelector('.parent-card button')).toBeNull();
  });
});
