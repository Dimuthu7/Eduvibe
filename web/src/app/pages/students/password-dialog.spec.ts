import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslateService } from '../../core/i18n/translate.service';
import { ToastService } from '../../core/ui/toast.service';
import { OneTimePasswordShare, PasswordDialog, passwordDialogConfig } from './password-dialog';

const data: OneTimePasswordShare = {
  name: 'Mrs Fernando', phone: '+94772222222', oneTimePassword: 'ABCD-2345', title: 'students.parent.invited_title',
  keys: { intro: 'students.parent.otp_intro', person: 'students.parent.person', share: 'students.parent.share_message' },
};

describe('PasswordDialog', () => {
  const setup = () => {
    TestBed.configureTestingModule({
      imports: [PasswordDialog],
      providers: [{ provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close: vi.fn() } }],
    });
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) =>
      key === 'students.parent.share_message' ? 'Hi {name}: {phone} / {otp} at {url}' : key,
    );
    const toast = TestBed.inject(ToastService);
    vi.spyOn(toast, 'success').mockImplementation(() => undefined);
    vi.spyOn(toast, 'error').mockImplementation(() => undefined);
    const fixture = TestBed.createComponent(PasswordDialog);
    fixture.detectChanges();
    return { fixture, toast, el: fixture.nativeElement as HTMLElement };
  };

  afterEach(() => vi.unstubAllGlobals());

  it('shows the password with Share, WhatsApp and Copy, and only Done closes it', () => {
    const { el } = setup();
    expect(el.querySelector('.otp')!.textContent).toContain('ABCD-2345');
    expect(el.querySelector('button.share')).not.toBeNull();
    expect(el.querySelector('button.copy')).not.toBeNull();
    const wa = el.querySelector<HTMLAnchorElement>('a.whatsapp')!;
    expect(wa.href).toContain('https://wa.me/94772222222?text=');
    expect(decodeURIComponent(wa.href)).toContain(`Hi Mrs Fernando: +94772222222 / ABCD-2345 at ${location.origin}`);
    expect(passwordDialogConfig(data).disableClose).toBe(true);
  });

  it('opens the share sheet with the sign-in message', async () => {
    const share = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', { ...navigator, share });
    const { el } = setup();
    el.querySelector<HTMLButtonElement>('button.share')!.click();
    await Promise.resolve();
    expect(share).toHaveBeenCalledWith({ text: `Hi Mrs Fernando: +94772222222 / ABCD-2345 at ${location.origin}` });
  });

  it('copies the message when the browser has no share sheet', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    const { el, toast } = setup();
    el.querySelector<HTMLButtonElement>('button.share')!.click();
    await vi.waitFor(() => expect(toast.success).toHaveBeenCalledWith('common.copied'));
    expect(writeText).toHaveBeenCalledWith(expect.stringContaining('ABCD-2345'));
  });
});
