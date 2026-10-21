import { inject, Injectable } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import type { CardsSpec } from './cards-dialog';

export type { CardsSpec } from './cards-dialog';

/** Opens the "student cards" dialog a class and a student page share. Its code (and the QR encoder) load when it is first opened. */
@Injectable({ providedIn: 'root' })
export class CardActions {
  private readonly dialog = inject(MatDialog);

  async open(spec: CardsSpec): Promise<void> {
    const { CardsDialog } = await import('./cards-dialog');
    this.dialog.open(CardsDialog, { data: spec, width: '420px', maxWidth: '94vw', autoFocus: 'first-tabbable' });
  }
}
