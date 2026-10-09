import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { SystemInfo, SystemService } from '../../core/api/system.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

type State = { kind: 'loading' } | { kind: 'ok'; info: SystemInfo } | { kind: 'error' };

/** Shows whether the API is reachable. Used to confirm each deployment end to end. */
@Component({
  selector: 'app-status',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatCardModule, MatProgressBarModule, TranslatePipe],
  template: `
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ 'status.title' | t }}</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        @switch (state().kind) {
          @case ('loading') {
            <p>{{ 'status.loading' | t }}</p>
            <mat-progress-bar mode="indeterminate" />
          }
          @case ('ok') {
            @if (okInfo(); as info) {
              <p class="ok">{{ 'status.ok' | t }}</p>
              <dl>
                <dt>{{ 'status.version' | t }}</dt>
                <dd>{{ info.version }}</dd>
                <dt>{{ 'status.environment' | t }}</dt>
                <dd>{{ info.environment }}</dd>
                <dt>{{ 'status.modules' | t }}</dt>
                <dd>{{ info.modules.join(', ') }}</dd>
              </dl>
            }
          }
          @case ('error') {
            <p class="field-error">{{ 'status.error' | t }}</p>
            <button mat-stroked-button type="button" (click)="load()">{{ 'status.retry' | t }}</button>
          }
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    dl { display: grid; grid-template-columns: max-content 1fr; gap: 0.25rem 1rem; margin: 0; }
    dt { color: var(--mat-sys-on-surface-variant); }
    dd { margin: 0; }
    .ok { font-weight: 500; }
  `,
})
export class Status {
  private readonly system = inject(SystemService);

  protected readonly state = signal<State>({ kind: 'loading' });

  constructor() {
    this.load();
  }

  protected okInfo(): SystemInfo | null {
    const s = this.state();
    return s.kind === 'ok' ? s.info : null;
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });
    this.system.info().subscribe({
      next: (info) => this.state.set({ kind: 'ok', info }),
      error: () => this.state.set({ kind: 'error' }),
    });
  }
}
