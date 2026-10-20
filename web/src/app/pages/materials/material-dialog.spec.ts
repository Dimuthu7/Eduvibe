import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Material, MaterialFolder } from '../../core/api/materials.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { MaterialDialog, MaterialDialogData } from './material-dialog';

const API = 'http://api.test';

const folders: MaterialFolder[] = [
  { id: 't1', classId: 'c1', kind: 'Notes', parentId: null, name: 'Term 1' },
  { id: 'w1', classId: 'c1', kind: 'Notes', parentId: 't1', name: 'Week 1' },
  { id: 'p1', classId: 'c1', kind: 'PastPaper', parentId: null, name: '2023' },
  { id: 'n2', classId: 'c2', kind: 'Notes', parentId: null, name: 'Other class' },
];

const material: Material = {
  id: 'm1', classId: 'c1', folderId: 'w1', title: 'Week 1 notes', fileName: 'w1.pdf', contentType: 'application/pdf', sizeBytes: 100, kind: 'Notes', topic: null,
  year: null, addedAt: '2026-10-10T04:00:00Z', canPreview: true,
};

describe('MaterialDialog folders', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  const setup = (data: Partial<MaterialDialogData>) => {
    TestBed.configureTestingModule({
      imports: [MaterialDialog],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { classes: [{ value: 'c1', label: 'Maths' }, { value: 'c2', label: 'Science' }], topics: [], folders, ...data } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(MaterialDialog);
    fixture.detectChanges();
    const dialog = fixture.componentInstance as unknown as {
      folderOptions(): { value: string; label: string }[];
      form: { controls: Record<string, { value: unknown; setValue(v: unknown): void }>; getRawValue(): unknown };
      submit(): void;
    };
    return { fixture, dialog };
  };

  afterEach(() => {
    http.verify();
    close.mockReset();
  });

  it('offers only the folders of the class and kind, named by where they sit, with "no folder" first', () => {
    const { dialog } = setup({ classId: 'c1', kind: 'Notes' });
    expect(dialog.folderOptions().map((o) => o.label)).toEqual(['materials.no_folder', 'Term 1', 'Term 1 / Week 1']);
  });

  it('starts in the folder the page was showing and forgets it when the kind changes', () => {
    const { fixture, dialog } = setup({ classId: 'c1', kind: 'Notes', folderId: 'w1' });
    expect(dialog.form.controls['folderId'].value).toBe('w1');
    dialog.form.controls['kind'].setValue('PastPaper');
    fixture.detectChanges();
    expect(dialog.folderOptions().map((o) => o.label)).toEqual(['materials.no_folder', '2023']);
    expect(dialog.form.controls['folderId'].value).toBe('');
  });

  it('sends the chosen folder with an edit, and null when the file goes back to the shelf', () => {
    const { dialog } = setup({ material });
    dialog.submit();
    const first = http.expectOne(`${API}/api/materials/m1`);
    expect(first.request.body).toMatchObject({ folderId: 'w1', kind: 'Notes' });
    first.flush(material);

    TestBed.resetTestingModule();
    const again = setup({ material });
    again.dialog.form.controls['folderId'].setValue('');
    again.dialog.submit();
    expect(http.expectOne(`${API}/api/materials/m1`).request.body).toMatchObject({ folderId: null });
  });
});
