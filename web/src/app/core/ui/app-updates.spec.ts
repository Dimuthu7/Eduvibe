import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { SwUpdate, VersionEvent } from '@angular/service-worker';
import { Subject, of } from 'rxjs';
import { TranslateService } from '../i18n/translate.service';
import { AppUpdates } from './app-updates';

describe('AppUpdates', () => {
  const setup = (isEnabled: boolean) => {
    const versionUpdates = new Subject<VersionEvent>();
    const sw = { isEnabled, versionUpdates, unrecoverable: new Subject(), checkForUpdate: vi.fn().mockResolvedValue(false) };
    const open = vi.fn().mockReturnValue({ onAction: () => of() });
    TestBed.configureTestingModule({ providers: [{ provide: SwUpdate, useValue: sw }, { provide: MatSnackBar, useValue: { open } }] });
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    TestBed.inject(AppUpdates).start();
    return { sw, versionUpdates, open };
  };

  it('checks for a new version on start and offers a reload once it is ready', () => {
    const { sw, versionUpdates, open } = setup(true);
    expect(sw.checkForUpdate).toHaveBeenCalledTimes(1);
    versionUpdates.next({ type: 'VERSION_DETECTED', version: { hash: 'b' } });
    expect(open).not.toHaveBeenCalled();
    versionUpdates.next({ type: 'VERSION_READY', currentVersion: { hash: 'a' }, latestVersion: { hash: 'b' } });
    expect(open).toHaveBeenCalledWith('update.ready', 'update.reload', expect.anything());
  });

  it('does nothing without a service worker', () => {
    const { sw } = setup(false);
    expect(sw.checkForUpdate).not.toHaveBeenCalled();
  });
});
