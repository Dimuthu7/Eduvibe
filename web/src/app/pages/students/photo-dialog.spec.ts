import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { AppConfig } from '../../core/config/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { PhotoDialog } from './photo-dialog';

describe('PhotoDialog', () => {
  it('says so and keeps Save off when the file cannot be read as a picture', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { studentId: 's1', file: new File(['not a picture'], 'x.jpg', { type: 'image/jpeg' }) } },
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue('http://api.test');
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(PhotoDialog);
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 20));
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('students.photo.unreadable');
    expect(el.querySelector('app-submit-button button')?.hasAttribute('disabled')).toBe(true);
    http.verify();
  });
});
