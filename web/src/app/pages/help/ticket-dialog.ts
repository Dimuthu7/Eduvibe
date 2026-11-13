import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { inlineError } from '../../core/api/problem';
import { SupportService, Ticket, TICKET_KINDS, TicketKind } from '../../core/api/support.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { SubmitButton } from '../../shared/submit-button';

const MAX_DETAILS = 2000;

/**
 * Report an issue: what kind it is, what happened, and a screenshot if they have one. The screen the teacher came from is sent with it,
 * so support does not have to ask where they were.
 */
@Component({
  selector: 'app-ticket-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    SubmitButton,
    TranslatePipe,
  ],
  template: `
    <h2 mat-dialog-title>{{ 'support.report' | t }}</h2>
    <form (ngSubmit)="send()">
      <mat-dialog-content>
        <mat-form-field>
          <mat-label>{{ 'support.kind' | t }}</mat-label>
          <mat-select [(ngModel)]="kind" name="kind">
            @for (option of kinds; track option) {
              <mat-option [value]="option">{{ 'support.kind.' + option | t }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field>
          <mat-label>{{ 'support.details' | t }}</mat-label>
          <textarea
            matInput
            rows="5"
            name="details"
            [(ngModel)]="details"
            [attr.maxlength]="maxDetails"
            [placeholder]="'support.details_hint' | t"
          ></textarea>
          <mat-hint>{{ details.length }} / {{ maxDetails }}</mat-hint>
        </mat-form-field>

        <div class="shot">
          <button mat-stroked-button type="button" (click)="picker.click()">
            <mat-icon>image</mat-icon>
            {{ (file() ? 'support.shot_change' : 'support.shot_add') | t }}
          </button>
          @if (file(); as chosen) {
            <span class="muted">{{ chosen.name }}</span>
          }
          <input #picker type="file" accept="image/jpeg,image/png,image/webp" hidden (change)="choose($event)" />
        </div>

        @if (error(); as key) {
          <p class="banner" role="alert">{{ key | t }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions>
        <button mat-button type="button" matDialogClose>{{ 'common.cancel' | t }}</button>
        <app-submit-button
          [label]="sent() ? 'common.close' : 'support.send'"
          [busy]="busy()"
          [disabled]="!sent() && details.trim().length === 0"
        />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .shot { display: flex; gap: 0.75rem; align-items: center; flex-wrap: wrap; margin-bottom: 0.5rem; }
    .banner { margin: 0.5rem 0 0; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
  `,
})
export class TicketDialog {
  private readonly api = inject(SupportService);
  private readonly ref = inject<MatDialogRef<TicketDialog, Ticket>>(MatDialogRef);

  protected readonly kinds = TICKET_KINDS;
  protected readonly maxDetails = MAX_DETAILS;
  protected kind: TicketKind = 'problem';
  protected details = '';
  protected readonly file = signal<File | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  /** Set once the report has reached support, so a failed screenshot cannot send the report twice. */
  protected readonly sent = signal<Ticket | null>(null);

  /** Where the teacher was when they pressed Report, which is the most useful thing support can be told. */
  private readonly screen = inject(Router).url;

  protected choose(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.file.set(input.files?.[0] ?? null);
    input.value = '';
  }

  protected send(): void {
    const already = this.sent();
    if (already) {
      this.ref.close(already);
      return;
    }
    if (this.busy() || this.details.trim().length === 0) return;
    this.busy.set(true);
    this.error.set('');
    this.api.raise({ kind: this.kind, details: this.details.trim(), screen: this.screen }).subscribe({
      next: (ticket) => this.withShot(ticket),
      error: (e) => {
        this.error.set(inlineError(e));
        this.busy.set(false);
      },
    });
  }

  /** The picture goes up on its own once the ticket exists, so a failed upload still leaves the report with support. */
  private withShot(ticket: Ticket): void {
    const chosen = this.file();
    if (!chosen) {
      this.ref.close(ticket);
      return;
    }

    this.api.saveShot(ticket.id, chosen).subscribe({
      next: () => this.ref.close({ ...ticket, hasShot: true }),
      error: (e) => {
        // The report itself is in, so the button now only closes; the picture can be sent again from the ticket.
        this.sent.set(ticket);
        this.error.set(inlineError(e) || 'support.shot_failed');
        this.busy.set(false);
      },
    });
  }
}
