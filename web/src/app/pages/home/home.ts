import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { AdminService, Institute } from '../../core/api/admin.service';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';

/** A teacher's start screen. Classes, fees and attendance are added here sprint by sprint. */
@Component({
  selector: 'app-home',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MatIconModule, MatListModule, TranslatePipe],
  template: `
    <h1 class="page-title">{{ 'home.welcome' | t }}, {{ auth.user()?.firstName }}</h1>
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ 'home.institutes' | t }}</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        @switch (institutes.status()) {
          @case ('ready') {
            <mat-list>
              @for (institute of institutes.data(); track institute.id) {
                <mat-list-item>
                  <mat-icon matListItemIcon>apartment</mat-icon>
                  <span matListItemTitle>{{ institute.name }}</span>
                  @if (institute.district) {
                    <span matListItemLine>{{ institute.district }}</span>
                  }
                </mat-list-item>
              } @empty {
                <p class="muted">{{ 'home.no_institutes' | t }}</p>
              }
            </mat-list>
          }
          @case ('error') {
            <p class="muted">{{ 'error.load_failed' | t }}</p>
          }
          @default {
            <div class="skeleton"></div>
          }
        }
      </mat-card-content>
    </mat-card>
    <p class="muted">{{ 'home.more_soon' | t }}</p>
  `,
})
export class Home {
  protected readonly auth = inject(AuthService);
  protected readonly institutes = new Loadable<Institute[]>();

  constructor() {
    this.institutes.load(inject(AdminService).myInstitutes());
  }
}
