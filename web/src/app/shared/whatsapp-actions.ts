import { inject, Injectable } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import type { WhatsAppSpec } from './whatsapp-dialog';

export type { WhatsAppSpec } from './whatsapp-dialog';

/** Opens the one WhatsApp "who will be messaged" dialog fees, attendance and exams share. Its code loads when it is first opened. */
@Injectable({ providedIn: 'root' })
export class WhatsAppActions {
  private readonly dialog = inject(MatDialog);

  async open(spec: WhatsAppSpec): Promise<void> {
    const { WhatsAppDialog } = await import('./whatsapp-dialog');
    this.dialog.open(WhatsAppDialog, { data: spec, width: '420px', maxWidth: '94vw', autoFocus: 'first-tabbable' });
  }
}
