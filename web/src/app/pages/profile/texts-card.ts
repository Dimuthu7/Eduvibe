import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { SmsApi } from '../../core/api/sms.service';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ToastService } from '../../core/ui/toast.service';

/**
 * One switch: whether this phone takes text messages from GuruBook. A parent or a student can turn it off themselves, for every teacher at
 * once, because a text arrives with no way to answer the right one. Teachers do not see it: their own choice is on the Text messages screen.
 */
@Component({
  selector: 'app-texts-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MatSlideToggleModule, TranslatePipe],
  template: `
    @if (shows && loaded()) {
      <mat-card appearance="outlined" class="texts">
        <mat-card-content>
          <h2 class="section-title first">{{ 'sms.my_texts_title' | t }}</h2>
          <div class="line">
            <span>{{ 'sms.my_texts' | t }}</span>
            <mat-slide-toggle [checked]="on()" [disabled]="busy()" [aria-label]="'sms.my_texts' | t" (change)="set($event.checked)" />
          </div>
          <p class="muted hint">{{ 'sms.my_texts_hint' | t }}</p>
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: `
    :host { display: contents; }
    .texts { margin-top: 1rem; }
    .section-title.first { margin-top: 0; }
    .line { display: flex; align-items: center; justify-content: space-between; gap: 1rem; min-height: 2.75rem; }
    .hint { margin: 0.25rem 0 0; font: var(--mat-sys-body-small); }
  `,
})
export class TextsCard {
  private readonly api = inject(SmsApi);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  protected readonly shows = this.auth.hasRole('Parent') || this.auth.hasRole('Student');
  protected readonly loaded = signal(false);
  protected readonly on = signal(true);
  protected readonly busy = signal(false);

  constructor() {
    if (this.shows) {
      // Without an answer the card stays away: a switch that may not be what the server holds is worse than no switch.
      this.api.myTexts().subscribe({
        next: (mine) => {
          this.on.set(mine.on);
          this.loaded.set(true);
        },
        error: () => undefined,
      });
    }
  }

  protected set(on: boolean): void {
    this.busy.set(true);
    this.api.saveMyTexts(on).subscribe({
      next: (saved) => {
        this.on.set(saved.on);
        this.busy.set(false);
        this.toast.success(saved.on ? 'sms.my_texts_on' : 'sms.my_texts_off');
      },
      error: () => this.busy.set(false),
    });
  }
}
