import { inject, Injectable } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '../i18n/translate.service';

/** Short messages at the bottom of the screen. Pass a translation key, never plain text. */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly snackBar = inject(MatSnackBar);
  private readonly i18n = inject(TranslateService);

  success(key: string): void {
    this.show(key, 3000);
  }

  error(key: string): void {
    this.show(key, 6000, 'toast-error');
  }

  private show(key: string, duration: number, panelClass?: string): void {
    this.snackBar.open(this.i18n.translate(key), this.i18n.translate('toast.close'), {
      duration,
      panelClass,
      politeness: panelClass ? 'assertive' : 'polite',
    });
  }
}
