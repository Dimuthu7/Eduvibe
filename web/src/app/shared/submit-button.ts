import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '../core/i18n/translate.pipe';

/** The main button of a form. Shows a spinner and blocks double submits while the request runs. */
@Component({
  selector: 'app-submit-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <button mat-flat-button type="submit" [disabled]="disabled() || busy()" [attr.aria-busy]="busy()">
      @if (busy()) {
        <mat-spinner diameter="18" />
      }
      {{ label() | t }}
    </button>
  `,
  styles: `
    :host { display: contents; }
    mat-spinner { display: inline-block; margin-right: 0.5rem; vertical-align: middle; }
  `,
})
export class SubmitButton {
  readonly label = input.required<string>();
  readonly busy = input(false);
  readonly disabled = input(false);
}
