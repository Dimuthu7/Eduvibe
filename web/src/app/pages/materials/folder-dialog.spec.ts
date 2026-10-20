import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MaterialFolder } from '../../core/api/materials.service';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { FolderDialog, FolderDialogData } from './folder-dialog';

const API = 'http://api.test';

describe('FolderDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  const setup = (data: FolderDialogData) => {
    TestBed.configureTestingModule({
      imports: [FolderDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } }],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(FolderDialog);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const type = (text: string) => {
      const input = el.querySelector('input') as HTMLInputElement;
      input.value = text;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const submit = () => {
      (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };
    return { fixture, el, type, submit };
  };

  afterEach(() => {
    http.verify();
    close.mockReset();
  });

  it('makes a folder in the place it was opened from and closes with it', () => {
    const { type, submit } = setup({ classId: 'c1', kind: 'Notes', parentId: 't1' });
    type('  Week 1 ');
    submit();
    const request = http.expectOne(`${API}/api/materials/folders`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ classId: 'c1', kind: 'Notes', parentId: 't1', name: 'Week 1' });
    const saved: MaterialFolder = { id: 'f1', classId: 'c1', kind: 'Notes', parentId: 't1', name: 'Week 1' };
    request.flush(saved);
    expect(close).toHaveBeenCalledWith(saved);
  });

  it('renames an existing folder, starting from its name', () => {
    const folder: MaterialFolder = { id: 'f1', classId: 'c1', kind: 'Notes', parentId: null, name: 'Term 1' };
    const { el, type, submit } = setup({ folder, classId: 'c1', kind: 'Notes', parentId: null });
    expect((el.querySelector('input') as HTMLInputElement).value).toBe('Term 1');
    type('First term');
    submit();
    const request = http.expectOne(`${API}/api/materials/folders/f1`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ name: 'First term' });
    request.flush({ ...folder, name: 'First term' });
    expect(close).toHaveBeenCalled();
  });

  it('asks for a name before sending anything', () => {
    const { el, type, submit } = setup({ classId: 'c1', kind: 'Notes', parentId: null });
    type('   ');
    submit();
    http.expectNone(`${API}/api/materials/folders`);
    expect(el.querySelector('mat-error')?.textContent).toContain('validation.required');
  });

  it('shows a name that is already used beside the field and keeps the dialog open', () => {
    const { fixture, el, type, submit } = setup({ classId: 'c1', kind: 'Notes', parentId: null });
    type('Term 1');
    submit();
    http.expectOne(`${API}/api/materials/folders`).flush({ code: 'folder_name_taken' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(el.querySelector('mat-error')?.textContent).toContain('error.folder_name_taken');
    expect(close).not.toHaveBeenCalled();
  });
});
