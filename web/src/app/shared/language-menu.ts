import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { AuthService } from '../core/auth/auth.service';
import { Language, LANGUAGES } from '../core/i18n/language';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { TranslateService } from '../core/i18n/translate.service';

/**
 * The language button in the top bar, on every screen including sign-in and sign-up. A signed-in person's choice is saved on their account;
 * before signing in it is kept on the phone and goes to the account at sign-in. The Super Admin stays in English and does not see it.
 */
@Component({
  selector: 'app-language-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    @if (shown()) {
      <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="'language.title' | t">
        <mat-icon>translate</mat-icon>
      </button>
      <mat-menu #menu="matMenu">
        @for (option of languages; track option.code) {
          <button mat-menu-item type="button" role="menuitemradio" [attr.aria-checked]="option.code === i18n.language()" (click)="pick(option.code)">
            <mat-icon>{{ option.code === i18n.language() ? 'check' : '' }}</mat-icon>
            <span [attr.lang]="option.code">{{ option.name }}</span>
          </button>
        }
      </mat-menu>
    }
  `,
  styles: `
    mat-icon { font-size: 1.5rem; }
  `,
})
export class LanguageMenu {
  private readonly auth = inject(AuthService);
  protected readonly i18n = inject(TranslateService);
  protected readonly languages = LANGUAGES;
  protected readonly shown = computed(() => !this.auth.hasRole('SuperAdmin'));

  protected pick(language: Language): void {
    if (language === this.i18n.language()) return;
    if (this.auth.signedIn()) {
      void this.i18n.use(language);
      this.auth.setLanguage(language).subscribe();
    } else {
      void this.i18n.pickBeforeSignIn(language);
    }
  }
}
