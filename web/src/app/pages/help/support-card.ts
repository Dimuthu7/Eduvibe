import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { PlansService } from '../../core/api/plans.service';
import { errorCode } from '../../core/api/problem';
import { SupportScreen, SupportService, Ticket } from '../../core/api/support.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { BlobImage } from '../../shared/blob-image';
import { formatDateTime } from '../../shared/format';
import { TicketDialog } from './ticket-dialog';

/**
 * Talking to support, at the top of the help page: the number to call, a button that reports what went wrong, and every ticket this
 * teacher has raised with what support wrote back. A Pro feature, so a teacher on Free is told what it is on rather than shown a lock.
 */
@Component({
  selector: 'app-support-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    TranslatePipe,
  ],
  template: `
    <mat-card>
      <mat-card-content>
        <div class="head">
          <div>
            <strong>{{ 'support.title' | t }}</strong>
            <p class="muted">{{ 'support.subtitle' | t }}</p>
          </div>
          @if (!locked()) {
            <button mat-flat-button type="button" (click)="report()">
              <mat-icon>flag</mat-icon>
              {{ 'support.report' | t }}
            </button>
          }
        </div>

        @if (locked()) {
          <p class="muted locked">
            {{ 'support.locked' | t }}
            @if (availableOn(); as plan) {
              {{ 'my_plan.available_on' | t }} <strong>{{ plan }}</strong>.
            }
          </p>
        } @else if (screen.data(); as data) {
          @if (data.phone) {
            <p class="phone">
              <a mat-stroked-button [href]="'tel:' + data.phone">
                <mat-icon>call</mat-icon>
                {{ 'support.call' | t }}
              </a>
              <span>
                {{ data.phone }}
                @if (data.hours) {
                  <span class="muted"> · {{ data.hours }}</span>
                }
              </span>
            </p>
          }

          @if (data.tickets.length > 0) {
            <mat-accordion class="tickets" displayMode="flat">
              @for (ticket of data.tickets; track ticket.id) {
                <mat-expansion-panel (opened)="showShot(ticket)">
                  <mat-expansion-panel-header>
                    <mat-panel-title>{{ 'support.kind.' + ticket.kind | t }}</mat-panel-title>
                    <mat-panel-description>
                      <span class="state {{ ticket.status }}">{{ 'support.status.' + ticket.status | t }}</span>
                    </mat-panel-description>
                  </mat-expansion-panel-header>

                  <p class="muted when raised">{{ when(ticket.at) }}</p>
                  <p class="said">{{ ticket.details }}</p>
                  @if (ticket.hasShot && shotOf() === ticket.id && shot.url(); as url) {
                    <img class="shot" [src]="url" [alt]="'support.shot' | t" />
                  }

                  @for (reply of ticket.replies; track reply.id) {
                    <div class="reply" [class.support]="reply.fromSupport">
                      <span class="who">{{ (reply.fromSupport ? 'support.them' : 'support.you') | t }}</span>
                      <p>{{ reply.body }}</p>
                      <span class="muted when">{{ when(reply.at) }}</span>
                    </div>
                  }

                  <mat-form-field class="answer">
                    <mat-label>{{ 'support.write' | t }}</mat-label>
                    <textarea matInput rows="2" [(ngModel)]="drafts[ticket.id]" [name]="'reply-' + ticket.id"></textarea>
                  </mat-form-field>
                  <button mat-stroked-button type="button" [disabled]="busy() || !(drafts[ticket.id] ?? '').trim()" (click)="send(ticket)">
                    {{ 'support.send' | t }}
                  </button>
                </mat-expansion-panel>
              }
            </mat-accordion>
          } @else {
            <p class="muted">{{ 'support.none' | t }}</p>
          }
        } @else if (screen.status() === 'error') {
          <p class="muted">{{ 'error.load_failed' | t }}</p>
          <button mat-stroked-button type="button" (click)="load()">{{ 'status.retry' | t }}</button>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: block; }
    .head { display: flex; gap: 1rem; align-items: flex-start; justify-content: space-between; flex-wrap: wrap; }
    .head p { margin: 0.25rem 0 0; font: var(--mat-sys-body-medium); }
    .locked { margin: 0.75rem 0 0; }
    .phone { display: flex; gap: 0.75rem; align-items: center; flex-wrap: wrap; margin: 1rem 0 0; }
    .tickets { display: block; margin-top: 1rem; }
    mat-panel-description { gap: 0.75rem; justify-content: flex-end; }
    .state { font-weight: 500; }
    .state.Open { color: var(--mat-sys-error); }
    .state.InProgress { color: var(--mat-sys-primary); }
    .when { font: var(--mat-sys-body-small); }
    .raised { margin: 0 0 0.5rem; }
    .said { margin: 0 0 0.75rem; white-space: pre-wrap; }
    .shot { display: block; max-width: 100%; margin-bottom: 0.75rem; border-radius: var(--mat-sys-corner-small); }
    .reply { display: grid; gap: 0.25rem; padding: 0.5rem 0.75rem; margin-bottom: 0.5rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container); }
    .reply.support { background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); }
    .reply p { margin: 0; white-space: pre-wrap; }
    .reply .who { font: var(--mat-sys-label-medium); font-weight: 600; }
    .answer { margin-top: 0.5rem; }
  `,
})
export class SupportCard implements OnInit {
  private readonly api = inject(SupportService);
  private readonly plans = inject(PlansService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  protected readonly screen = new Loadable<SupportScreen>();
  protected readonly locked = signal(false);
  protected readonly availableOn = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly shot = new BlobImage(inject(DestroyRef));
  protected readonly shotOf = signal<string | null>(null);
  protected readonly drafts: Record<string, string | undefined> = {};
  protected readonly when = formatDateTime;

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.locked.set(false);
    this.screen.status.set('loading');
    this.api.mine().subscribe({
      next: (data) => {
        this.screen.data.set(data);
        this.screen.status.set('ready');
      },
      error: (e) => this.failed(e),
    });
  }

  protected report(): void {
    this.dialog
      .open<TicketDialog, undefined, Ticket>(TicketDialog, { width: '34rem', maxWidth: 'calc(100vw - 1rem)' })
      .afterClosed()
      .subscribe((ticket) => {
        if (!ticket) return;
        this.toast.success('support.sent');
        this.load();
      });
  }

  protected send(ticket: Ticket): void {
    const body = (this.drafts[ticket.id] ?? '').trim();
    if (!body || this.busy()) return;
    this.busy.set(true);
    this.api.reply(ticket.id, body).subscribe({
      next: (updated) => {
        this.drafts[ticket.id] = '';
        this.replace(updated);
        this.busy.set(false);
      },
      error: () => this.busy.set(false),
    });
  }

  /** The picture is fetched only when its ticket is opened, so a long list never pulls pictures down. */
  protected showShot(ticket: Ticket): void {
    if (!ticket.hasShot || this.shotOf() === ticket.id) return;
    this.shotOf.set(ticket.id);
    this.shot.set(null);
    this.api.shot(ticket.id).subscribe({
      next: (blob) => this.shot.set(blob.size > 0 ? blob : null),
      error: () => this.shot.set(null),
    });
  }

  private replace(ticket: Ticket): void {
    const data = this.screen.data();
    if (!data) return;
    this.screen.data.set({ ...data, tickets: data.tickets.map((t) => (t.id === ticket.id ? ticket : t)) });
  }

  private failed(e: unknown): void {
    if (errorCode(e) === 'plan_feature_locked') {
      this.locked.set(true);
      this.screen.status.set('ready');
      this.plans.mine().subscribe({
        next: (plan) => this.availableOn.set(plan.features.find((f) => f.key === 'priority_support')?.availableOn ?? null),
        error: () => undefined,
      });
    } else {
      this.screen.error.set(e);
      this.screen.status.set('error');
    }
  }
}
