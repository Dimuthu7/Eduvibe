import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { MessageSettings, MessagingApi, SaveRuleInput } from '../../core/api/messaging.service';
import { PlansService } from '../../core/api/plans.service';
import { errorCode } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { MessageEventCard } from './message-event-card';
import { LoadError } from '../../shared/load-error';

/**
 * The Messages page: every alert GuruBook sends on its own, whether it goes out, how it is sent, and what the paid ones say. Everything
 * starts on and in the app, so a teacher who never comes here pays for nothing. A Pro feature.
 */
@Component({
  selector: 'app-message-settings',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LoadError, MatButtonModule, MatExpansionModule, MatIconModule, MessageEventCard, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'messages.title' | t }}</h1>
        <p class="page-subtitle">{{ 'messages.subtitle' | t }}</p>
      </div>
    </header>

    @if (locked()) {
      <div class="notice">
        <mat-icon>lock</mat-icon>
        <div>
          <strong>{{ 'messages.locked_title' | t }}</strong>
          <p>
            {{ 'messages.locked_message' | t }}
            @if (availableOn()) {
              {{ 'my_plan.available_on' | t }} <strong>{{ availableOn() }}</strong>.
            }
          </p>
        </div>
      </div>
    } @else {
      @switch (settings.status()) {
        @case ('ready') {
          @if (settings.data(); as data) {
            <mat-accordion class="events">
              @for (item of data.events; track item.event) {
                <app-message-event-card
                  [item]="item"
                  [noteMax]="data.noteMaxLength"
                  [weeks]="data.lowAttendanceWeeks"
                  [quietDays]="data.lowAttendanceQuietDays"
                  [busy]="busy()"
                  (rule)="saveRule(item.event, $event)"
                  (text)="saveText(item.event, $event.language, $event.text)"
                />
              }
            </mat-accordion>
            <p class="muted foot">{{ 'messages.foot' | t }}</p>
          }
        }
        @case ('error') {
          <app-load-error [error]="settings.error()" (retry)="load()" />
        }
        @default {
          <div class="skeleton"></div>
          <div class="skeleton"></div>
          <div class="skeleton"></div>
        }
      }
    }
  `,
  styles: `
    .notice { display: flex; gap: 0.75rem; align-items: flex-start; padding: 0.75rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); margin-bottom: 1rem; }
    .notice mat-icon { font-size: 24px; width: 24px; height: 24px; flex: none; color: var(--mat-sys-primary); }
    .notice p { margin: 0.25rem 0 0; font: var(--mat-sys-body-medium); }
    .events { display: block; }
    .foot { margin: 1rem 0 0; font: var(--mat-sys-body-small); }
  `,
})
export class MessageSettingsPage implements OnInit {
  private readonly api = inject(MessagingApi);
  private readonly plans = inject(PlansService);
  private readonly toast = inject(ToastService);

  protected readonly settings = new Loadable<MessageSettings>();
  protected readonly locked = signal(false);
  protected readonly availableOn = signal<string | null>(null);
  protected readonly busy = signal(false);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.locked.set(false);
    this.settings.status.set('loading');
    this.api.settings().subscribe({
      next: (data) => {
        this.settings.data.set(data);
        this.settings.status.set('ready');
      },
      error: (e) => this.failed(e),
    });
  }

  protected saveRule(event: string, input: SaveRuleInput): void {
    this.busy.set(true);
    this.api.saveRule(event, input).subscribe({
      next: (data) => this.saved(data),
      error: () => this.busy.set(false),
    });
  }

  protected saveText(event: string, language: string, text: string): void {
    this.busy.set(true);
    this.api.saveText(event, language, text).subscribe({
      next: (data) => this.saved(data),
      error: () => this.busy.set(false),
    });
  }

  private saved(data: MessageSettings): void {
    this.settings.data.set(data);
    this.busy.set(false);
    this.toast.success('messages.saved');
  }

  private failed(e: unknown): void {
    if (errorCode(e) === 'plan_feature_locked') {
      this.locked.set(true);
      this.plans.mine().subscribe({
        next: (plan) => this.availableOn.set(plan.features.find((f) => f.key === 'custom_messages')?.availableOn ?? null),
        error: () => undefined,
      });
    } else {
      this.settings.error.set(e);
      this.settings.status.set('error');
    }
  }
}
