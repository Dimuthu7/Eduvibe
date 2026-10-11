import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/** Where a student lands after signing in. Their classes, fees and results arrive here in later sprints. */
@Component({
  selector: 'app-welcome',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MatIconModule, TranslatePipe],
  template: `
    <mat-card appearance="outlined">
      <mat-card-content class="welcome">
        <mat-icon aria-hidden="true">waving_hand</mat-icon>
        <h1 class="page-title">{{ 'home.welcome' | t }}, {{ auth.user()?.firstName }}</h1>
        <p class="muted">{{ 'welcome.soon' | t }}</p>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .welcome { display: grid; justify-items: center; gap: 0.5rem; text-align: center; padding: 1.5rem 0.5rem; }
    .welcome mat-icon { width: 3rem; height: 3rem; font-size: 3rem; color: var(--mat-sys-primary); }
    .welcome p { margin: 0; }
  `,
})
export class Welcome {
  protected readonly auth = inject(AuthService);
}
