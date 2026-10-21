import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DriveStatus, Material, MaterialFolder } from '../../core/api/materials.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { ToastService } from '../../core/ui/toast.service';
import { Materials } from './materials';

const API = 'http://api.test';

const material = (id: string, over: Partial<Material> = {}): Material => ({
  id, classId: 'c1', title: `File ${id}`, fileName: `file-${id}.pdf`, contentType: 'application/pdf', sizeBytes: 2048, kind: 'PastPaper', topic: null, year: null, folderId: null,
  addedAt: '2026-10-10T04:00:00Z', canPreview: true, ...over,
});

const folder = (id: string, name: string, parentId: string | null = null, over: Partial<MaterialFolder> = {}): MaterialFolder => ({
  id, name, parentId, classId: 'c1', kind: 'Notes', ...over,
});

const connected: DriveStatus = { configured: true, connected: true, email: 'sunil@example.com', needsReconnect: false };

describe('Materials', () => {
  let http: HttpTestingController;

  const setup = async (status: DriveStatus | 'locked', list: Material[] = [], folders: MaterialFolder[] = [], classId = '') => {
    TestBed.configureTestingModule({ imports: [Materials], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(Materials);
    fixture.componentRef.setInput('classId', classId);
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
      http.expectOne(`${API}/api/materials/folders`).flush(folders);
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

  it('shows where a file is, folders included, while several classes or kinds are listed together', async () => {
    const { el } = await setup(connected, [material('1', { kind: 'Notes', folderId: 'w1' })], [folder('t1', 'Term 1'), folder('w1', 'Week 1', 't1')]);
    expect(el.textContent).toContain('Maths 2027 · materials.kind.Notes · Term 1 / Week 1');
    expect(el.querySelector('.crumbs')).toBeNull();
    expect(el.textContent).toContain('materials.folders_hint');
  });

  describe('inside one class and one kind', () => {
    const files = [
      material('1', { title: 'Loose notes', kind: 'Notes' }),
      material('2', { title: 'Week 1 notes', kind: 'Notes', folderId: 'w1' }),
      material('3', { title: 'Other class notes', kind: 'Notes', classId: 'c2' }),
      material('4', { title: 'A past paper' }),
    ];
    const folders = [folder('t1', 'Term 1'), folder('w1', 'Week 1', 't1'), folder('x1', 'Hidden', null, { kind: 'Other' })];

    const open = async () => {
      const page = await setup(connected, files, folders, 'c1');
      const chip = (Array.from(page.el.querySelectorAll('mat-chip-option')) as HTMLElement[]).find((c) => c.textContent!.includes('materials.kind.Notes'))!;
      (chip.querySelector('button') as HTMLButtonElement).click();
      page.fixture.detectChanges();
      return page;
    };
    const titles = (el: HTMLElement) => Array.from(el.querySelectorAll('.row-title')).map((t) => t.textContent!.trim());
    const crumbs = (el: HTMLElement) => Array.from(el.querySelectorAll('.crumb')).map((t) => t.textContent!.trim());

    it('lists the shelf as folders first and then the files that are not in a folder', async () => {
      const { el } = await open();
      expect(crumbs(el)).toEqual(['materials.kind.Notes']);
      expect(titles(el)).toEqual(['Term 1', 'Loose notes']);
      expect(el.textContent).toContain('materials.folder_item');
      expect(el.textContent).toContain('materials.folder_new');
    });

    it('opens a folder, shows its path, and goes back by tapping a name in the path', async () => {
      const { fixture, el } = await open();
      (el.querySelector('.panel .open') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(crumbs(el)).toEqual(['materials.kind.Notes', 'Term 1']);
      expect(titles(el)).toEqual(['Week 1']);

      (el.querySelector('.panel .open') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(crumbs(el)).toEqual(['materials.kind.Notes', 'Term 1', 'Week 1']);
      expect(titles(el)).toEqual(['Week 1 notes']);

      (el.querySelectorAll('.crumb')[0] as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(titles(el)).toEqual(['Term 1', 'Loose notes']);
    });

    it('says a folder is empty, and will not remove one that holds something', async () => {
      const { fixture, el } = await open();
      const page = fixture.componentInstance as unknown as { removeFolder(f: MaterialFolder): void; folderId: { set(id: string | null): void } };
      const toast = vi.spyOn(TestBed.inject(ToastService), 'error').mockImplementation(() => undefined);
      page.removeFolder(folders[0]);
      expect(toast).toHaveBeenCalledWith('error.folder_not_empty');

      page.folderId.set('w1');
      fixture.detectChanges();
      page.folderId.set(null);
      fixture.detectChanges();
      expect(el.textContent).not.toContain('materials.folder_empty');
    });
  });

  it('says a shelf is empty and offers a new folder', async () => {
    const { fixture, el } = await setup(connected, [material('1')], [], 'c1');
    const chip = (Array.from(el.querySelectorAll('mat-chip-option')) as HTMLElement[]).find((c) => c.textContent!.includes('materials.kind.Other'))!;
    (chip.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.textContent).toContain('materials.folder_empty');
    expect(el.textContent).toContain('materials.folder_new');
  });

  describe('moving', () => {
    const files = [
      material('1', { title: 'Loose notes', kind: 'Notes', topic: 'Algebra', year: 2024 }),
      material('2', { title: 'Week 1 notes', kind: 'Notes', folderId: 'w1' }),
    ];
    const folders = [folder('t1', 'Term 1'), folder('w1', 'Week 1', 't1'), folder('a1', 'Archive'), folder('p1', '2023', null, { kind: 'PastPaper' })];

    type Page = {
      dragEnded(event: unknown, item: { file?: Material; folder?: MaterialFolder }): void;
      moveFile(m: Material): void;
      moveFolder(f: MaterialFolder): void;
    };

    const open = async () => {
      const page = await setup(connected, files, folders, 'c1');
      const chip = (Array.from(page.el.querySelectorAll('mat-chip-option')) as HTMLElement[]).find((c) => c.textContent!.includes('materials.kind.Notes'))!;
      (chip.querySelector('button') as HTMLButtonElement).click();
      page.fixture.detectChanges();
      return { ...page, page: page.fixture.componentInstance as unknown as Page };
    };

    /** Pretends the pointer is over the element with this data-drop value (jsdom cannot hit-test). */
    const pointerOver = (key: string | null) => {
      const el = document.createElement('div');
      if (key !== null) el.setAttribute('data-drop', key);
      Object.defineProperty(document, 'elementsFromPoint', { value: () => [el], configurable: true });
    };
    const dropped = { dropPoint: { x: 10, y: 10 }, source: { reset: vi.fn() } };

    afterEach(() => {
      Reflect.deleteProperty(document, 'elementsFromPoint');
    });

    it('moves a file into the folder it is dropped on, keeping its details', async () => {
      const { page } = await open();
      pointerOver('f:t1');
      page.dragEnded(dropped, { file: files[0] });
      const request = http.expectOne(`${API}/api/materials/1`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ title: 'Loose notes', kind: 'Notes', topic: 'Algebra', year: 2024, folderId: 't1' });
      request.flush({ ...files[0], folderId: 't1' });
    });

    it('moves a folder onto the shelf when it is dropped on the shelf name in the path', async () => {
      const { page } = await open();
      pointerOver('c:');
      page.dragEnded(dropped, { folder: folders[1] });
      const request = http.expectOne(`${API}/api/materials/folders/w1/move`);
      expect(request.request.body).toEqual({ kind: 'Notes', parentId: null });
      request.flush(folder('w1', 'Week 1'));
      // The lists are read again, since a folder can take its files to another shelf.
      http.expectOne(`${API}/api/materials`).flush(files);
      http.expectOne(`${API}/api/materials/folders`).flush(folders);
    });

    it.each([
      ['a folder dropped on itself', 'f:t1', { folder: folders[0] }],
      ['a folder dropped on the folder it is already in', 'f:t1', { folder: folders[1] }],
      ['a file dropped where it already is', 'f:w1', { file: files[1] }],
      ['anything dropped on nothing', null, { file: files[0] }],
    ])('does nothing for %s', async (_name, key, item) => {
      const { page } = await open();
      pointerOver(key);
      page.dragEnded(dropped, item);
      http.expectNone((r) => r.method === 'PUT');
    });

    it('offers every shelf and folder of the class except where the file is now', async () => {
      const { page } = await open();
      const dialog = TestBed.inject(MatDialog);
      const spy = vi.spyOn(dialog, 'open').mockReturnValue({ afterClosed: () => of('Notes|t1') } as never);
      page.moveFile(files[1]);
      const data = spy.mock.calls[0][1]!.data as { options: { value: string; label: string }[] };
      expect(data.options.map((o) => o.label)).toEqual([
        'materials.kind.Notes', 'materials.kind.Notes / Archive', 'materials.kind.Notes / Term 1',
        'materials.kind.PastPaper', 'materials.kind.PastPaper / 2023', 'materials.kind.Other',
      ]);
      expect(data.options.map((o) => o.value)).not.toContain('Notes|w1');
      const request = http.expectOne(`${API}/api/materials/2`);
      expect(request.request.body).toMatchObject({ kind: 'Notes', folderId: 't1' });
      request.flush({ ...files[1], folderId: 't1' });
    });

    it('does not offer a folder, or anything inside it, as its own destination', async () => {
      const { page } = await open();
      const spy = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of('PastPaper|') } as never);
      page.moveFolder(folders[0]);
      const data = spy.mock.calls[0][1]!.data as { options: { value: string }[] };
      expect(data.options.map((o) => o.value)).toEqual(['Notes|a1', 'PastPaper|', 'PastPaper|p1', 'Other|']);
      const request = http.expectOne(`${API}/api/materials/folders/t1/move`);
      expect(request.request.body).toEqual({ kind: 'PastPaper', parentId: null });
      request.flush(folder('t1', 'Term 1', null, { kind: 'PastPaper' }));
      http.expectOne(`${API}/api/materials`).flush(files);
      http.expectOne(`${API}/api/materials/folders`).flush(folders);
    });
  });
});
