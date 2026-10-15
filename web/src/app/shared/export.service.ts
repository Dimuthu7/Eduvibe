import { inject, Injectable } from '@angular/core';
import { MatBottomSheet } from '@angular/material/bottom-sheet';
import type { ExportSpec } from './export-sheet';

export type { ExportSpec } from './export-sheet';

/** Opens the one "Export / Share" sheet every report screen uses. The sheet's code is loaded only when it is first opened. */
@Injectable({ providedIn: 'root' })
export class ExportService {
  private readonly sheet = inject(MatBottomSheet);

  async open(spec: ExportSpec): Promise<void> {
    const { ExportSheet } = await import('./export-sheet');
    this.sheet.open(ExportSheet, { data: spec, autoFocus: 'first-tabbable' });
  }
}
