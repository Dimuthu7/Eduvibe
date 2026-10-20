import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DriveStatus, Material } from '../../core/api/materials.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { Materials } from './materials';

const API = 'http://api.test';

const material = (id: string, over: Partial<Material> = {}): Material => ({
  id, classId: 'c1', title: `File ${id}`, fileName: `file-${id}.pdf`, contentType: 'application/pdf', sizeBytes: 2048, kind: 'PastPaper', topic: null, year: null,
  addedAt: '2026-10-10T04:00:00Z', canPreview: true, ...over,
});

const connected: DriveStatus = { configured: true, connected: true, email: 'sunil@example.com', needsReconnect: false };

describe('Materials', () => {
  let http: HttpTestingController;

  const setup = async (status: DriveStatus | 'locked', list: Material[] = []) => {
    TestBed.configureTestingModule({ imports: [Materials], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(Materials);
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/api/classes/classes`).flush([{ id: 'c1', title: 'Maths 2027' }, { id: 'c2', title: 'Science 2027' }]);
    const request = http.expectOne(`${API}/api/materials/drive`);
    if (status === 'locked') {
      request.flush({ code: 'plan_feature_locked' }, { status: 403, statusText: 'Forbidden' });
      http.expectOne(`${API}/api/plans/mine`).flush({
        planName: 'Free', endsOn: null, limits: [], features: [{ key: 'drive_materials', enabled: false, availableOn: 'Pro' }],
      });
    } else {
      request.flush(status);
      http.expectOne(`${API}/api/materials`).flush(list);
    }
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  };

  afterEach(() => http.verify());

  it('shows what the feature gives, and which plan has it, to a teacher whose plan lacks it', async () => {
    const { el } = await setup('locked');
    expect(el.textContent).toContain('materials.locked_title');
    expect(el.textContent).toContain('Pro');
    expect(el.querySelector('.filters')).toBeNull();
    expect(el.textContent).not.toContain('materials.connect');
  });

  it('asks a teacher with no Drive link to connect, and offers no way to add files yet', async () => {
    const { el } = await setup({ configured: true, connected: false, email: null, needsReconnect: false });
    expect(el.textContent).toContain('materials.connect_title');
    expect(el.textContent).not.toContain('materials.add');
  });

  it('says the server is not ready when no Google client is set up', async () => {
    const { el } = await setup({ configured: false, connected: false, email: null, needsReconnect: false });
    expect(el.textContent).toContain('materials.not_configured_title');
    expect(el.textContent).not.toContain('materials.connect_title');
  });

  it('asks to connect again when Google stopped accepting the link, and blocks adding', async () => {
    const { el } = await setup({ ...connected, needsReconnect: true });
    expect(el.textContent).toContain('materials.reconnect_title');
    expect((el.querySelector('header button') as HTMLButtonElement).disabled).toBe(true);
  });

  it('lists files with their class and kind, and narrows them by kind', async () => {
    const { fixture, el } = await setup(connected, [
      material('1', { title: '2024 Paper' }),
      material('2', { title: 'Algebra notes', kind: 'Notes', topic: 'Algebra', year: 2023 }),
    ]);
    expect(el.textContent).toContain('sunil@example.com');
    expect(Array.from(el.querySelectorAll('.row-title')).map((t) => t.textContent!.trim())).toEqual(['2024 Paper', 'Algebra notes']);
    expect(el.textContent).toContain('Maths 2027');
    expect(el.textContent).toContain('Algebra · 2023');

    const chips = Array.from(el.querySelectorAll('mat-chip-option')) as HTMLElement[];
    (chips.find((c) => c.textContent!.includes('materials.kind.Notes'))!.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(Array.from(el.querySelectorAll('.row-title')).map((t) => t.textContent!.trim())).toEqual(['Algebra notes']);
  });

  it('invites the first file when there are none', async () => {
    const { el } = await setup(connected, []);
    expect(el.textContent).toContain('materials.none');
  });
});
