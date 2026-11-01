import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogConfig, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { ToastService } from '../../core/ui/toast.service';

/** Dialog options for {@link PasswordDialog}: the password is shown once, so only Done closes it. */
export function passwordDialogConfig(data: OneTimePasswordShare): MatDialogConfig<OneTimePasswordShare> {
  return { data, width: '28rem', maxWidth: 'calc(100vw - 1rem)', disableClose: true, autoFocus: 'dialog' };
}

export interface OneTimePasswordShare {
  name: string;
  phone: string;
  oneTimePassword: string;
  /** Message key for the heading: a new student, or a password reset. */
  title: string;
  /** For a parent instead of a student: message keys for the sentence above the details, the name label and the message to send. */
  keys?: { intro: string; person: string; share: string };
}

/**
 * Shows a one-time password once, with ways to pass it on: the phone's share sheet (WhatsApp, SMS and the rest), straight to WhatsApp,
 * or copied. Open it with {@link passwordDialogConfig} so a stray tap outside does not close it before the password is passed on.
 */
@Component({
  selector: 'app-password-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ data.title | t }}</h2>
    <mat-dialog-content>
      <p class="intro">{{ (data.keys?.intro ?? 'students.otp_intro') | t }}</p>
      <dl class="facts">
        <div>
          <dt>{{ (data.keys?.person ?? 'students.student') | t }}</dt>
          <dd>{{ data.name }}</dd>
        </div>
        <div>
          <dt>{{ 'students.username' | t }}</dt>
          <dd>{{ data.phone }}</dd>
        </div>
        <div>
          <dt>{{ 'students.otp' | t }}</dt>
          <dd class="otp">{{ data.oneTimePassword }}</dd>
        </div>
      </dl>
      <p class="muted note">{{ 'students.otp_once' | t }}</p>
      <div class="share-row">
        <button mat-flat-button type="button" class="share" (click)="share()">
          <mat-icon>share</mat-icon>
          {{ 'students.share' | t }}
        </button>
        <a mat-stroked-button class="whatsapp" [href]="whatsapp" target="_blank" rel="noopener">
          <mat-icon>send</mat-icon>
          {{ 'students.share_whatsapp' | t }}
        </a>
        <button mat-stroked-button type="button" class="copy" (click)="copy()">
          <mat-icon>content_copy</mat-icon>
          {{ 'common.copy' | t }}
        </button>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>{{ 'common.done' | t }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    .intro { margin-top: 0; }
    .facts { display: grid; gap: 0.75rem; margin: 0; }
    .facts div { display: flex; flex-direction: column; }
    dt { color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-label-medium); }
    dd { margin: 0; overflow-wrap: anywhere; }
    .otp { font: var(--mat-sys-headline-small); font-family: ui-monospace, monospace; letter-spacing: 0.08em; }
    .note { margin-bottom: 0; }
    .share-row { display: flex; flex-wrap: wrap; gap: 0.5rem; margin-top: 1rem; }
    .share-row > * { flex: 1 1 8rem; }
  `,
})
export class PasswordDialog {
  protected readonly data = inject<OneTimePasswordShare>(MAT_DIALOG_DATA);
  private readonly i18n = inject(TranslateService);
  private readonly toast = inject(ToastService);

  /** What the teacher sends the student: the sign-in details and where to open the app. */
  protected get message(): string {
    return this.i18n
      .translate(this.data.keys?.share ?? 'students.share_message')
      .replace('{name}', this.data.name)
      .replace('{phone}', this.data.phone)
      .replace('{otp}', this.data.oneTimePassword)
      .replace('{url}', location.origin);
  }

  protected get whatsapp(): string {
    return `https://wa.me/${this.data.phone.replace(/\D/g, '')}?text=${encodeURIComponent(this.message)}`;
  }

  /** Opens the phone's share sheet with the whole message; where there is none (most desktops) the message is copied instead. */
  protected async share(): Promise<void> {
    if (typeof navigator.share === 'function') {
      try {
        await navigator.share({ text: this.message });
        return;
      } catch (error) {
        // Closing the share sheet is a choice, not a failure; anything else falls back to copying.
        if (error instanceof DOMException && error.name === 'AbortError') return;
      }
    }

    await this.copy();
  }

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.message);
      this.toast.success('common.copied');
    } catch {
      // Clipboard access can be blocked; the password is still on screen to read out.
      this.toast.error('common.copy_failed');
    }
  }
}
