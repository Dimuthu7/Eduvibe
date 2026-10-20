import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Material, StudentMaterial } from '../../core/api/materials.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { ToastService } from '../../core/ui/toast.service';
import { MyMaterials } from './my-materials';

const API = 'http://api.test';

const item = (id: string, classId: string, classTitle: string, over: Partial<Material> = {}): StudentMaterial => ({
  classTitle,
  teacherName: classId === 'c1' ? 'Nimal Perera' : null,
  material: {
    id, classId, title: `File ${id}`, fileName: `file-${id}.pdf`, contentType: 'application/pdf', sizeBytes: 340 * 1024, kind: 'PastPaper', topic: 'Algebra', year: 2024,
    addedAt: '2026-10-10T04:00:00Z', canPreview: true, ...over,
  },
});

describe('MyMaterials', () => {
  let http: HttpTestingController;

  const setup = async (list: StudentMaterial[], classId = '') => {
    TestBed.configureTestingModule({ imports: [MyMaterials], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(MyMaterials);
    fixture.componentRef.setInput('classId', classId);
    fixture.detectChanges();
    http.expectOne(`${API}/api/materials/mine`).flush(list);
    fixture.detectChanges();
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  };

  afterEach(() => http.verify());

  it('groups files by class with the teacher, and shows topic, year and size', async () => {
    const { el } = await setup([item('1', 'c1', 'Maths'), item('2', 'c2', 'Chemistry', { topic: null, year: null }), item('3', 'c1', 'Maths', { kind: 'Notes' })]);
    expect(Array.from(el.querySelectorAll('h2')).map((h) => h.textContent!.trim())).toEqual(['Chemistry', 'Maths']);
    expect(el.textContent).toContain('Nimal Perera');
    expect(el.textContent).toContain('materials.kind.PastPaper · Algebra · 2024 · 340 KB');
    expect(el.querySelectorAll('.panel-row').length).toBe(3);
  });

  it('offers a kind filter only when there is more than one kind', async () => {
    const one = await setup([item('1', 'c1', 'Maths')]);
    expect(one.el.querySelector('mat-chip-listbox')).toBeNull();
    http.verify();
    TestBed.resetTestingModule();
    const two = await setup([item('1', 'c1', 'Maths'), item('2', 'c1', 'Maths', { kind: 'Notes' })]);
    expect(two.el.querySelector('mat-chip-listbox')).not.toBeNull();
  });

  it('puts the class asked for first', async () => {
    const { el } = await setup([item('1', 'c1', 'Maths'), item('2', 'c2', 'Chemistry')], 'c1');
    expect(el.querySelector('h2')!.textContent!.trim()).toBe('Maths');
  });

  it('says so when nothing has been shared', async () => {
    const { el } = await setup([]);
    expect(el.textContent).toContain('materials.my_none');
  });

  it('tells the student to ask their teacher when the teacher\'s Drive is the problem', async () => {
    vi.spyOn(window, 'open').mockReturnValue({ location: { href: '' }, close: vi.fn() } as unknown as Window);
    const { fixture, el } = await setup([item('1', 'c1', 'Maths')]);
    const error = vi.spyOn(TestBed.inject(ToastService), 'error').mockImplementation(() => undefined);

    (el.querySelector('button.open') as HTMLButtonElement).click();
    http.expectOne((r) => r.url === `${API}/api/materials/mine/1/file`).flush(new Blob([JSON.stringify({ code: 'drive_reconnect' })]), { status: 409, statusText: 'Conflict' });
    await vi.waitFor(() => expect(error).toHaveBeenCalledWith('materials.unavailable'));
    fixture.detectChanges();
    expect((el.querySelector('button.open') as HTMLButtonElement).disabled).toBe(false);
  });
});
