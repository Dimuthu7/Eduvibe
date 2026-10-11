import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { ToastService } from '../../core/ui/toast.service';

export interface OneTimePasswordShare {
  name: string;
  phone: string;
  oneTimePassword: string;
  /** Message key for the heading: a new student, or a password reset. */
  title: string;
}

/** Shows a one-time password once, with ways to pass it to the student: copy it or send it on WhatsApp. */
@Component({
  selector: 'app-password-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ data.title | t }}</h2>
    <mat-dialog-content>
      <p class="intro">{{ 'students.otp_intro' | t }}</p>
      <dl class="facts">
        <div>
          <dt>{{ 'students.student' | t }}</dt>
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
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" (click)="copy()">
        <mat-icon>content_copy</mat-icon>
        {{ 'common.copy' | t }}
      </button>
      <a mat-stroked-button [href]="whatsapp" target="_blank" rel="noopener">
        <mat-icon>send</mat-icon>
        {{ 'students.share_whatsapp' | t }}
      </a>
      <button mat-flat-button type="button" mat-dialog-close>{{ 'common.done' | t }}</button>
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
    mat-dialog-actions { flex-wrap: wrap; }
  `,
})
export class PasswordDialog {
  protected readonly data = inject<OneTimePasswordShare>(MAT_DIALOG_DATA);
  private readonly i18n = inject(TranslateService);
  private readonly toast = inject(ToastService);

  /** What the teacher sends the student: the sign-in details and where to open the app. */
  private get message(): string {
    return this.i18n
      .translate('students.share_message')
      .replace('{name}', this.data.name)
      .replace('{phone}', this.data.phone)
      .replace('{otp}', this.data.oneTimePassword)
      .replace('{url}', location.origin);
  }

  protected get whatsapp(): string {
    return `https://wa.me/${this.data.phone.replace(/\D/g, '')}?text=${encodeURIComponent(this.message)}`;
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
