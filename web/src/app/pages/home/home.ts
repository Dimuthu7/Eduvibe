import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, of } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { AdminService } from '../../core/api/admin.service';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/** A teacher's start screen. Classes, fees and attendance are added here sprint by sprint. */
@Component({
  selector: 'app-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, TranslatePipe],
  template: `
    <h1>{{ 'home.welcome' | t }}, {{ auth.user()?.fullName }}</h1>
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ 'home.institutes' | t }}</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        @for (institute of institutes(); track institute.id) {
          <p class="row">
            <strong>{{ institute.name }}</strong>
            @if (institute.town) {
              <span class="muted">{{ institute.town }}</span>
            }
          </p>
        } @empty {
          <p class="muted">{{ 'home.no_institutes' | t }}</p>
        }
      </mat-card-content>
    </mat-card>
    <p class="muted">{{ 'home.more_soon' | t }}</p>
  `,
  styles: `
    :host { display: block; max-width: 40rem; margin: 1.5rem auto; padding: 0 1rem; }
    .row { display: flex; gap: 0.75rem; align-items: baseline; margin: 0.5rem 0; }
    .muted { opacity: 0.7; }
  `,
})
export class Home {
  protected readonly auth = inject(AuthService);
  protected readonly institutes = toSignal(
    inject(AdminService).myInstitutes().pipe(catchError(() => of([]))),
    { initialValue: [] },
  );
}
