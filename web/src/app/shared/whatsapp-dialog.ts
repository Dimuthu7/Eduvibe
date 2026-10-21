import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { errorCode, inlineError } from '../core/api/problem';
import { WhatsAppApi, WhatsAppPreview, WhatsAppStatus } from '../core/api/whatsapp.service';
import { PlansService } from '../core/api/plans.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { TranslateService } from '../core/i18n/translate.service';
import { ToastService } from '../core/ui/toast.service';
import { SubmitButton } from './submit-button';

/** What a screen tells the dialog: where the preview and send live, and the words around them. */
export interface WhatsAppSpec {
  /** Address of the preview (GET) and the send (POST), for example /api/fees/classes/{id}/whatsapp?month=2027-03. */
  path: string;
  /** Translation keys. */
  title: string;
  intro: string;
}

type Stage = 'loading' | 'locked' | 'unconfigured' | 'error' | 'ready';

/**
 * Shows who a WhatsApp send would reach, and why it would skip the others, before anything goes out. A teacher on the Free plan sees
 * what the feature is and which plan has it instead; a server without WhatsApp set up says so.
 */
@Component({
  selector: 'app-whatsapp-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, MatProgressSpinnerModule, RouterLink, SubmitButton, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ data.title | t }}</h2>
    <mat-dialog-content>
      @switch (stage()) {
        @case ('loading') {
          <div class="busy"><mat-spinner diameter="24" /> {{ 'whatsapp.loading' | t }}</div>
        }
        @case ('locked') {
          <div class="notice">
            <mat-icon>lock</mat-icon>
            <div>
              <strong>{{ 'whatsapp.locked_title' | t }}</strong>
              <p>
                {{ 'whatsapp.locked_message' | t }}
                @if (availableOn()) {
                  {{ 'my_plan.available_on' | t }} <strong>{{ availableOn() }}</strong>.
                }
              </p>
            </div>
          </div>
        }
        @case ('unconfigured') {
          <div class="notice">
            <mat-icon>chat_bubble_outline</mat-icon>
            <div>
              <strong>{{ 'whatsapp.unconfigured_title' | t }}</strong>
              <p>{{ 'whatsapp.unconfigured_message' | t }}</p>
            </div>
          </div>
        }
        @case ('error') {
          <div class="notice error" role="alert">
            <mat-icon>error_outline</mat-icon>
            <div>
              <p>{{ (failure() || 'whatsapp.load_failed') | t }}</p>
              <button mat-button type="button" (click)="load()">{{ 'status.retry' | t }}</button>
            </div>
          </div>
        }
        @case ('ready') {
          @if (status()?.simulated) {
            <p class="test-mode"><mat-icon>science</mat-icon> {{ 'whatsapp.test_mode' | t }}</p>
          }
          <p class="intro">{{ data.intro | t }}</p>
          @if (preview(); as p) {
            <dl class="counts">
              <div class="main"><dt>{{ 'whatsapp.will_send' | t }}</dt><dd>{{ p.ready }}</dd></div>
              @if (p.alreadySent > 0) {
                <div><dt>{{ 'whatsapp.already_sent' | t }}</dt><dd>{{ p.alreadySent }}</dd></div>
              }
              @if (p.noNumber > 0) {
                <div><dt>{{ 'whatsapp.no_number' | t }}</dt><dd>{{ p.noNumber }}</dd></div>
              }
              @if (p.optedOut > 0) {
                <div><dt>{{ 'whatsapp.opted_out' | t }}</dt><dd>{{ p.optedOut }}</dd></div>
              }
              @if (p.overAllowance > 0) {
                <div class="warn"><dt>{{ 'whatsapp.over_allowance' | t }}</dt><dd>{{ p.overAllowance }}</dd></div>
              }
            </dl>
            @if (p.noNumberNames.length > 0) {
              <p class="names">{{ p.noNumberNames.join(', ') }}</p>
            }
            <p class="allowance">{{ allowance() }}</p>
            <a class="log-link" mat-button routerLink="/whatsapp" mat-dialog-close>{{ 'whatsapp.see_sent' | t }}</a>
          }
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>{{ (stage() === 'ready' ? 'common.cancel' : 'common.close') | t }}</button>
      @if (stage() === 'ready') {
        <app-submit-button
          label="whatsapp.send"
          [busy]="sending()"
          [disabled]="(preview()?.ready ?? 0) === 0"
          (click)="send()"
        />
      }
    </mat-dialog-actions>
  `,
  styles: `
    .busy { display: flex; align-items: center; gap: 0.75rem; color: var(--mat-sys-on-surface-variant); }
    .notice { display: flex; gap: 0.75rem; align-items: flex-start; padding: 0.75rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); }
    .notice mat-icon { font-size: 24px; width: 24px; height: 24px; flex: none; color: var(--mat-sys-primary); }
    .notice.error { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .notice.error mat-icon { color: inherit; }
    .notice p { margin: 0.25rem 0 0; font: var(--mat-sys-body-medium); }
    .test-mode { display: flex; align-items: center; gap: 0.5rem; margin: 0 0 0.75rem; padding: 0.5rem 0.75rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); font: var(--mat-sys-body-medium); }
    .test-mode mat-icon { font-size: 20px; width: 20px; height: 20px; flex: none; }
    .intro { margin: 0 0 1rem; color: var(--mat-sys-on-surface-variant); }
    .counts { margin: 0; display: grid; gap: 0.5rem; }
    .counts div { display: flex; justify-content: space-between; gap: 1rem; font: var(--mat-sys-body-large); }
    .counts dt { color: var(--mat-sys-on-surface-variant); }
    .counts dd { margin: 0; font-variant-numeric: tabular-nums; }
    .counts .main { font: var(--mat-sys-title-medium); padding-bottom: 0.5rem; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .counts .main dt { color: var(--mat-sys-on-surface); }
    .counts .warn dd, .counts .warn dt { color: var(--mat-sys-error); }
    .names { margin: 0.5rem 0 0; font: var(--mat-sys-body-small); color: var(--mat-sys-on-surface-variant); overflow-wrap: anywhere; }
    .allowance { margin: 1rem 0 0; font: var(--mat-sys-body-small); color: var(--mat-sys-on-surface-variant); }
    .log-link { margin-left: -0.75rem; }
  `,
})
export class WhatsAppDialog implements OnInit {
  protected readonly data = inject<WhatsAppSpec>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<WhatsAppDialog>>(MatDialogRef);
  private readonly api = inject(WhatsAppApi);
  private readonly plans = inject(PlansService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(TranslateService);

  protected readonly stage = signal<Stage>('loading');
  protected readonly status = signal<WhatsAppStatus | null>(null);
  protected readonly preview = signal<WhatsAppPreview | null>(null);
  protected readonly availableOn = signal<string | null>(null);
  protected readonly failure = signal('');
  protected readonly sending = signal(false);

  protected readonly allowance = computed(() => {
    const p = this.preview();
    if (!p) return '';
    return p.limit === null
      ? this.i18n.format('whatsapp.allowance_unlimited', { used: p.used })
      : this.i18n.format('whatsapp.allowance', { used: p.used, limit: p.limit });
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.stage.set('loading');
    this.api.status().subscribe({
      next: (status) => {
        this.status.set(status);
        if (!status.enabled) {
          this.showLocked();
        } else if (!status.configured) {
          this.stage.set('unconfigured');
        } else {
          this.loadPreview();
        }
      },
      error: () => {
        this.failure.set('');
        this.stage.set('error');
      },
    });
  }

  private showLocked(): void {
    this.stage.set('locked');
    // Which plan has it is nice to know, but the dialog is complete without it.
    this.plans.mine().subscribe({
      next: (plan) => this.availableOn.set(plan.features.find((f) => f.key === 'whatsapp')?.availableOn ?? null),
      error: () => undefined,
    });
  }

  private loadPreview(): void {
    this.api.preview(this.data.path).subscribe({
      next: (preview) => {
        this.preview.set(preview);
        this.stage.set('ready');
      },
      error: (e) => {
        this.failure.set(errorCode(e) === 'plan_feature_locked' ? '' : inlineError(e));
        if (errorCode(e) === 'plan_feature_locked') {
          this.showLocked();
        } else {
          this.stage.set('error');
        }
      },
    });
  }

  protected send(): void {
    if (this.sending()) return;
    this.sending.set(true);
    this.api.send(this.data.path).subscribe({
      next: () => {
        this.toast.success('whatsapp.queued');
        this.ref.close(true);
      },
      error: (e) => {
        this.sending.set(false);
        if (errorCode(e) === 'whatsapp_not_configured') this.stage.set('unconfigured');
      },
    });
  }
}
