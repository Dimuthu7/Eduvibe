import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { inlineError } from '../../../core/api/problem';
import { AdminTicket, SupportService, TICKET_STATUSES, TicketStatus } from '../../../core/api/support.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Loadable } from '../../../core/state/loadable';
import { ToastService } from '../../../core/ui/toast.service';
import { BlobImage } from '../../../shared/blob-image';
import { formatDateTime } from '../../../shared/format';
import { SubmitButton } from '../../../shared/submit-button';

/**
 * Super Admin: every teacher's tickets, newest first. Writing back tells the teacher in the app, and the status says who is waiting on
 * whom. The number teachers are shown is set here too, because it belongs to nobody's own account.
 */
@Component({
  selector: 'app-admin-tickets',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    SubmitButton,
    TranslatePipe,
  ],
  template: `
    <a mat-button routerLink="/admin/dashboard" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'dashboard.title' | t }}
    </a>
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'tickets.title' | t }}</h1>
        <p class="page-subtitle">{{ 'tickets.subtitle' | t }}</p>
      </div>
    </header>

    <mat-card class="settings">
      <mat-card-content>
        <form class="row" (ngSubmit)="saveSettings()">
          <mat-form-field>
            <mat-label>{{ 'tickets.phone' | t }}</mat-label>
            <input matInput name="phone" [(ngModel)]="phone" inputmode="tel" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'tickets.hours' | t }}</mat-label>
            <input matInput name="hours" [(ngModel)]="hours" [attr.maxlength]="100" />
          </mat-form-field>
          <app-submit-button label="common.save" [busy]="savingSettings()" />
        </form>
        @if (settingsError(); as key) {
          <p class="field-error">{{ key | t }}</p>
        }
        <p class="muted hint">{{ 'tickets.phone_hint' | t }}</p>
      </mat-card-content>
    </mat-card>

    <mat-button-toggle-group class="filter" [ngModel]="filter()" (ngModelChange)="narrow($event)" name="status">
      <mat-button-toggle value="">{{ 'tickets.all' | t }}</mat-button-toggle>
      @for (status of statuses; track status) {
        <mat-button-toggle [value]="status">{{ 'support.status.' + status | t }}</mat-button-toggle>
      }
    </mat-button-toggle-group>

    @switch (tickets.status()) {
      @case ('ready') {
        @if (tickets.data(); as rows) {
          @if (rows.length === 0) {
            <p class="muted">{{ 'tickets.none' | t }}</p>
          }
          <mat-accordion class="list">
            @for (ticket of rows; track ticket.id) {
              <mat-expansion-panel (opened)="showShot(ticket)">
                <mat-expansion-panel-header>
                  <mat-panel-title>
                    <strong>{{ ticket.teacherName || ('tickets.unknown_teacher' | t) }}</strong>
                    <span class="muted"> · {{ 'support.kind.' + ticket.kind | t }}</span>
                  </mat-panel-title>
                  <mat-panel-description>
                    <span class="state {{ ticket.status }}">{{ 'support.status.' + ticket.status | t }}</span>
                    <span class="muted when">{{ when(ticket.at) }}</span>
                  </mat-panel-description>
                </mat-expansion-panel-header>

                <p class="said">{{ ticket.details }}</p>
                @if (ticket.screen) {
                  <p class="muted screen">{{ 'tickets.on_screen' | t }} {{ ticket.screen }}</p>
                }
                @if (ticket.hasShot && shotOf() === ticket.id && shot.url(); as url) {
                  <img class="shot" [src]="url" [alt]="'support.shot' | t" />
                }

                @for (reply of ticket.replies; track reply.id) {
                  <div class="reply" [class.support]="reply.fromSupport">
                    <span class="who">{{ reply.fromSupport ? ('support.them' | t) : ticket.teacherName }}</span>
                    <p>{{ reply.body }}</p>
                    <span class="muted when">{{ when(reply.at) }}</span>
                  </div>
                }

                <mat-form-field class="answer">
                  <mat-label>{{ 'tickets.answer' | t }}</mat-label>
                  <textarea matInput rows="3" [(ngModel)]="drafts[ticket.id]" [name]="'answer-' + ticket.id"></textarea>
                </mat-form-field>
                <div class="actions">
                  <button mat-flat-button type="button" [disabled]="busy() || !(drafts[ticket.id] ?? '').trim()" (click)="answer(ticket)">
                    {{ 'tickets.send' | t }}
                  </button>
                  @for (status of statuses; track status) {
                    @if (status !== ticket.status) {
                      <button mat-stroked-button type="button" [disabled]="busy()" (click)="setStatus(ticket, status)">
                        {{ 'tickets.mark.' + status | t }}
                      </button>
                    }
                  }
                </div>
              </mat-expansion-panel>
            }
          </mat-accordion>
        }
      }
      @case ('error') {
        <div class="empty">
          <p>{{ 'error.load_failed' | t }}</p>
          <button mat-stroked-button type="button" (click)="load()">{{ 'status.retry' | t }}</button>
        </div>
      }
      @default {
        <div class="skeleton"></div>
        <div class="skeleton"></div>
      }
    }
  `,
  styles: `
    .back { margin-bottom: 0.5rem; }
    .settings .row { display: flex; gap: 1rem; align-items: center; flex-wrap: wrap; }
    .settings mat-form-field { width: 14rem; }
    .hint { margin: 0; font: var(--mat-sys-body-small); }
    .filter { margin-bottom: 1rem; }
    .list { display: block; }
    mat-panel-description { gap: 0.75rem; justify-content: flex-end; }
    .state { font-weight: 500; }
    .state.Open { color: var(--mat-sys-error); }
    .state.InProgress { color: var(--mat-sys-primary); }
    .when { font: var(--mat-sys-body-small); }
    .said { margin: 0 0 0.5rem; white-space: pre-wrap; }
    .screen { margin: 0 0 0.75rem; font: var(--mat-sys-body-small); }
    .shot { display: block; max-width: min(100%, 24rem); margin-bottom: 0.75rem; border-radius: var(--mat-sys-corner-small); }
    .reply { display: grid; gap: 0.25rem; padding: 0.5rem 0.75rem; margin-bottom: 0.5rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); }
    .reply.support { background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); }
    .reply p { margin: 0; white-space: pre-wrap; }
    .reply .who { font: var(--mat-sys-label-medium); font-weight: 600; }
    .answer { margin-top: 0.5rem; }
    .actions { display: flex; gap: 0.5rem; flex-wrap: wrap; }
  `,
})
export class AdminTickets implements OnInit {
  private readonly api = inject(SupportService);
  private readonly toast = inject(ToastService);

  protected readonly statuses = TICKET_STATUSES;
  protected readonly tickets = new Loadable<AdminTicket[]>();
  protected readonly filter = signal<TicketStatus | ''>('');
  protected readonly busy = signal(false);
  protected readonly savingSettings = signal(false);
  protected readonly settingsError = signal('');
  protected readonly shot = new BlobImage(inject(DestroyRef));
  protected readonly shotOf = signal<string | null>(null);
  protected readonly drafts: Record<string, string | undefined> = {};
  protected readonly when = formatDateTime;
  protected phone = '';
  protected hours = '';

  ngOnInit(): void {
    this.load();
    this.api.settings().subscribe({
      next: (settings) => {
        this.phone = settings.phone ?? '';
        this.hours = settings.hours ?? '';
      },
      error: () => undefined,
    });
  }

  protected load(): void {
    this.tickets.load(this.api.all(this.filter() || undefined));
  }

  protected narrow(status: TicketStatus | ''): void {
    this.filter.set(status);
    this.load();
  }

  protected answer(ticket: AdminTicket): void {
    const body = (this.drafts[ticket.id] ?? '').trim();
    if (!body || this.busy()) return;
    this.busy.set(true);
    this.api.answer(ticket.id, body).subscribe({
      next: (updated) => {
        this.drafts[ticket.id] = '';
        this.replace(updated);
        this.busy.set(false);
        this.toast.success('tickets.answered');
      },
      error: () => this.busy.set(false),
    });
  }

  protected setStatus(ticket: AdminTicket, status: TicketStatus): void {
    this.busy.set(true);
    this.api.setStatus(ticket.id, status).subscribe({
      next: (updated) => {
        this.replace(updated);
        this.busy.set(false);
      },
      error: () => this.busy.set(false),
    });
  }

  protected saveSettings(): void {
    if (this.savingSettings()) return;
    this.savingSettings.set(true);
    this.settingsError.set('');
    this.api.saveSettings({ phone: this.phone.trim() || null, hours: this.hours.trim() || null }).subscribe({
      next: (settings) => {
        this.phone = settings.phone ?? '';
        this.hours = settings.hours ?? '';
        this.savingSettings.set(false);
        this.toast.success('tickets.settings_saved');
      },
      error: (e) => {
        this.settingsError.set(inlineError(e));
        this.savingSettings.set(false);
      },
    });
  }

  /** The picture is fetched only when its ticket is opened, so the inbox never pulls pictures down. */
  protected showShot(ticket: AdminTicket): void {
    if (!ticket.hasShot || this.shotOf() === ticket.id) return;
    this.shotOf.set(ticket.id);
    this.shot.set(null);
    this.api.shot(ticket.id, true).subscribe({
      next: (blob) => this.shot.set(blob.size > 0 ? blob : null),
      error: () => this.shot.set(null),
    });
  }

  private replace(ticket: AdminTicket): void {
    // A ticket that no longer matches the filter drops out, so the list says what it says it is showing.
    this.tickets.update((rows) =>
      this.filter() && this.filter() !== ticket.status
        ? rows.filter((t) => t.id !== ticket.id)
        : rows.map((t) => (t.id === ticket.id ? ticket : t)),
    );
  }
}
