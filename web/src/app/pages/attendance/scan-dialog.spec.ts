import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Subject } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { ScanDialog } from './scan-dialog';

describe('ScanDialog', () => {
  const close = vi.fn();

  const open = () => {
    TestBed.configureTestingModule({
      imports: [ScanDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { sessionId: 's1', arrived: ['e1', 'e2'] } },
        { provide: MatDialogRef, useValue: { close, disableClose: false, backdropClick: () => new Subject(), keydownEvents: () => new Subject() } },
      ],
    });
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    vi.spyOn(TestBed.inject(TranslateService), 'format').mockImplementation((key, values) => `${key} ${JSON.stringify(values)}`);
    const fixture = TestBed.createComponent(ScanDialog);
    fixture.detectChanges();
    return fixture;
  };

  afterEach(() => {
    close.mockReset();
    vi.unstubAllGlobals();
  });

  it('says so when the device has no camera, and still lets the teacher finish', async () => {
    vi.stubGlobal('navigator', { ...navigator, mediaDevices: undefined });
    const fixture = open();
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('scan.no_camera');
    expect(root.textContent).toContain('"count":2');

    Array.from(root.querySelectorAll('button')).find((b) => b.textContent!.includes('scan.done'))!.click();
    expect(close).toHaveBeenCalledWith(['e1', 'e2']);
  });

  it('explains a blocked camera', async () => {
    const denied = new DOMException('no', 'NotAllowedError');
    vi.stubGlobal('navigator', { ...navigator, mediaDevices: { getUserMedia: () => Promise.reject(denied) } });
    const fixture = open();
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('scan.camera_blocked');
  });
});
