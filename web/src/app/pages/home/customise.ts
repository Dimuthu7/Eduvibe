import { ChangeDetectionStrategy, Component, input, linkedSignal, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { WidgetSetting } from '../../core/api/dashboard.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { SubmitButton } from '../../shared/submit-button';
import { defaultLayout, moveWidget, setEnabled, WIDGETS, WidgetDef } from './layout';

/**
 * Lets the teacher choose which widgets show and in what order. Buttons move a widget up or down, because dragging is awkward on
 * a phone. Nothing is saved until Save, and Cancel drops the changes.
 */
@Component({
  selector: 'app-customise',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatIconModule, MatSlideToggleModule, SubmitButton, TranslatePipe],
  template: `
    <div class="page-header">
      <div>
        <h1 class="page-title">{{ 'dashboard.customise.title' | t }}</h1>
        <p class="page-subtitle">{{ 'dashboard.customise.hint' | t }}</p>
      </div>
    </div>
    <form (ngSubmit)="save.emit(draft())">
      <div class="panel">
        @for (w of draft(); track w.key; let i = $index; let first = $first; let last = $last) {
          @let def = definition(w.key);
          <div class="panel-row" [class.off]="!w.enabled">
            <mat-icon class="icon" aria-hidden="true">{{ def.icon }}</mat-icon>
            <div class="row-main">
              <div class="row-title">{{ def.title | t }}</div>
              <div class="row-meta">{{ def.about | t }}</div>
            </div>
            <div class="move">
              <button mat-icon-button type="button" [disabled]="first" [attr.aria-label]="'dashboard.customise.up' | t" (click)="move(i, -1)">
                <mat-icon>arrow_upward</mat-icon>
              </button>
              <button mat-icon-button type="button" [disabled]="last" [attr.aria-label]="'dashboard.customise.down' | t" (click)="move(i, 1)">
                <mat-icon>arrow_downward</mat-icon>
              </button>
            </div>
            <mat-slide-toggle [checked]="w.enabled" [aria-label]="def.title | t" (change)="toggle(w.key, $event.checked)" />
          </div>
        }
      </div>
      <div class="actions">
        <button mat-button type="button" (click)="reset()">{{ 'dashboard.customise.reset' | t }}</button>
        <span class="spacer"></span>
        <button mat-button type="button" (click)="cancel.emit()">{{ 'common.cancel' | t }}</button>
        <app-submit-button label="common.save" [busy]="busy()" />
      </div>
    </form>
  `,
  styles: `
    mat-icon { font-size: 1.5rem; }
    .panel-row.off .icon, .panel-row.off .row-main { opacity: 0.6; }
    .icon { flex: none; color: var(--mat-sys-primary); }
    .move { display: flex; flex-direction: column; flex: none; }
    .move button { --mat-icon-button-state-layer-size: 32px; --mat-icon-button-icon-size: 20px; width: 32px; height: 32px; padding: 4px; }
    .actions { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; margin-top: 1rem; }
    .spacer { flex: 1; }
  `,
})
export class Customise {
  /** The layout as it is now. */
  readonly initial = input.required<WidgetSetting[]>();
  readonly busy = input(false);
  readonly save = output<WidgetSetting[]>();
  readonly cancel = output();

  protected readonly draft = linkedSignal(() => this.initial());

  protected definition(key: string): WidgetDef {
    return WIDGETS.find((w) => w.key === key)!;
  }

  protected move(index: number, step: -1 | 1): void {
    this.draft.update((list) => moveWidget(list, index, step));
  }

  protected toggle(key: string, enabled: boolean): void {
    this.draft.update((list) => setEnabled(list, key, enabled));
  }

  protected reset(): void {
    this.draft.set(defaultLayout());
  }
}
