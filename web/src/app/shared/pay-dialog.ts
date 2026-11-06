import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PaymentClaim, PaymentsApi, PayWay } from '../core/api/payments.service';
import { errorCode, inlineError } from '../core/api/problem';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { ToastService } from '../core/ui/toast.service';
import { BlobImage } from './blob-image';
import { formatLkr, formatMonth } from './format';
import { SubmitButton } from './submit-button';

/** Which fee is being paid, and for whose child when a parent opens it. */
export interface PayTarget {
  enrolmentId: string;
  month: string;
  classTitle: string;
  /** Set only when a parent is paying for a child. */
  studentId?: string;
}

/** The biggest slip photo the app will send, matching what the API takes. */
const MAX_SLIP_BYTES = 2 * 1024 * 1024;

/**
 * How to pay a fee by bank transfer or LankaQR, and "I've paid" afterwards. The teacher's account details and QR are shown with copy
 * buttons, along with a short code to write on the transfer so the teacher can match it. Nothing is recorded against the fee until the
 * teacher confirms it: this only tells them money is on the way.
 */
@Component({
  selector: 'app-pay-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule, SubmitButton, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatProgressSpinnerModule, TranslatePipe,
  ],
  template: `
    <h2 mat-dialog-title>{{ 'pay.title' | t }}</h2>
    <mat-dialog-content>
      @if (loading()) {
        <div class="middle"><mat-spinner diameter="28" /></div>
      } @else if (failed()) {
        <p class="banner" role="alert">{{ failed() | t }}</p>
      } @else if (way(); as w) {
        <p class="who">
          <strong>{{ w.classTitle || data.classTitle }}</strong>
          <span class="muted"> · {{ month(w.month) }}</span>
        </p>

        @if (!w.canPay) {
          <p class="hint">{{ 'pay.in_person' | t }}</p>
        } @else {
          @if (claim(); as c) {
            <section class="state" [class.bad]="c.status === 'Rejected'">
              <div class="head">
                <mat-icon aria-hidden="true">{{ c.status === 'Confirmed' ? 'task_alt' : c.status === 'Rejected' ? 'error' : 'hourglass_top' }}</mat-icon>
                <strong>{{ 'pay.status.' + c.status.toLowerCase() | t }}</strong>
              </div>
              <p class="line">{{ money(c.amount) }} · {{ 'pay.reference' | t }} {{ c.reference }}</p>
              @if (c.status === 'Confirmed' && c.receiptNo) {
                <p class="line">{{ 'pay.receipt_no' | t }} {{ c.receiptNo }}</p>
              }
              @if (c.status === 'Rejected' && c.rejectReason) {
                <p class="line">{{ c.rejectReason }}</p>
              }
            </section>
          }

          @if (showForm()) {
            <p class="amount">
              <span>{{ 'pay.to_pay' | t }}</span>
              <strong>{{ money(w.outstanding) }}</strong>
            </p>

            <section class="details">
              <div class="row">
                <div class="row-main">
                  <div class="row-meta">{{ 'pay.reference' | t }}</div>
                  <div class="row-title mono">{{ w.reference }}</div>
                </div>
                <button mat-icon-button type="button" [attr.aria-label]="'pay.copy' | t" (click)="copy(w.reference)">
                  <mat-icon>content_copy</mat-icon>
                </button>
              </div>
              <p class="hint">{{ 'pay.reference_hint' | t }}</p>
              <div class="row">
                <div class="row-main">
                  <div class="row-meta">{{ 'pay.account_number' | t }}</div>
                  <div class="row-title mono">{{ w.accountNumber }}</div>
                </div>
                <button mat-icon-button type="button" [attr.aria-label]="'pay.copy' | t" (click)="copy(w.accountNumber)">
                  <mat-icon>content_copy</mat-icon>
                </button>
              </div>
              <div class="row">
                <div class="row-main">
                  <div class="row-meta">{{ 'pay.account_name' | t }}</div>
                  <div class="row-title">{{ w.accountName }}</div>
                </div>
              </div>
              <div class="row">
                <div class="row-main">
                  <div class="row-meta">{{ 'pay.bank' | t }}</div>
                  <div class="row-title">{{ w.bankName }}@if (w.branch) { <span class="muted"> · {{ w.branch }}</span> }</div>
                </div>
              </div>
              @if (w.note) {
                <p class="hint">{{ w.note }}</p>
              }
            </section>

            @if (qr.url(); as image) {
              <section class="qr">
                <div class="row-meta">{{ 'pay.lankaqr' | t }}</div>
                <img [src]="image" [alt]="'pay.lankaqr' | t" />
              </section>
            }

            <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
              <h3 class="section-title">{{ 'pay.i_paid' | t }}</h3>
              @if (error()) {
                <p class="banner" role="alert">{{ error() | t }}</p>
              }
              <mat-form-field>
                <mat-label>{{ 'fees.amount' | t }}</mat-label>
                <span matTextPrefix>LKR&nbsp;</span>
                <input matInput type="number" inputmode="decimal" formControlName="amount" min="1" step="any" required />
                <mat-hint>{{ 'pay.amount_hint' | t }}</mat-hint>
              </mat-form-field>
              <mat-form-field>
                <mat-label>{{ 'pay.note' | t }}</mat-label>
                <input matInput formControlName="note" maxlength="200" />
                <mat-hint>{{ 'pay.note_hint' | t }}</mat-hint>
              </mat-form-field>
              <div class="slip">
                <button mat-stroked-button type="button" (click)="file.click()">
                  <mat-icon>photo_camera</mat-icon> {{ (slip() ? 'pay.slip_chosen' : 'pay.add_slip') | t }}
                </button>
                <input #file type="file" accept="image/*" hidden (change)="pick($event)" />
                @if (slip()) {
                  <span class="muted">{{ slipName() }}</span>
                }
              </div>
            </form>
          }
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>{{ 'common.close' | t }}</button>
      @if (showForm()) {
        <app-submit-button label="pay.submit" [busy]="busy()" (click)="submit()" />
      }
    </mat-dialog-actions>
  `,
  styles: `
    .middle { display: flex; justify-content: center; padding: 1.5rem 0; }
    .who { margin: 0 0 0.75rem; }
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .hint { margin: 0.25rem 0 0.75rem; color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-body-small); }
    .amount { display: flex; align-items: baseline; justify-content: space-between; gap: 1rem; margin: 0 0 0.75rem; }
    .amount strong { font: var(--mat-sys-headline-small); }
    .state { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-large); background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
    .state.bad { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .state .head { display: flex; align-items: center; gap: 0.5rem; font: var(--mat-sys-title-small); }
    .state .head mat-icon { font-size: 1.25rem; width: 1.25rem; height: 1.25rem; }
    .state .line { margin: 0.25rem 0 0; }
    .details { border: 1px solid var(--mat-sys-outline-variant); border-radius: var(--mat-sys-corner-medium); padding: 0.25rem 0.75rem; }
    .row { display: flex; align-items: center; gap: 0.5rem; padding: 0.4rem 0; }
    .row + .row { border-top: 1px solid var(--mat-sys-outline-variant); }
    .row-main { flex: 1; min-width: 0; }
    .mono { font-family: ui-monospace, monospace; letter-spacing: 0.04em; }
    .qr { margin-top: 0.75rem; text-align: center; }
    .qr img { max-width: 220px; width: 100%; height: auto; border-radius: var(--mat-sys-corner-medium); }
    form { display: flex; flex-direction: column; gap: 0.25rem; }
    .section-title { margin: 1.25rem 0 0.5rem; font: var(--mat-sys-title-small); }
    .slip { display: flex; align-items: center; gap: 0.75rem; flex-wrap: wrap; }
  `,
})
export class PayDialog {
  protected readonly data = inject<PayTarget>(MAT_DIALOG_DATA);
  private readonly api = inject(PaymentsApi);
  private readonly toast = inject(ToastService);
  private readonly ref = inject<MatDialogRef<PayDialog, PaymentClaim | undefined>>(MatDialogRef);

  protected readonly money = formatLkr;
  protected readonly month = formatMonth;
  protected readonly loading = signal(true);
  protected readonly failed = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly way = signal<PayWay | null>(null);
  protected readonly claim = signal<PaymentClaim | null>(null);
  protected readonly slip = signal<File | null>(null);
  protected readonly slipName = computed(() => this.slip()?.name ?? '');

  /** The form is for a fee that can still be paid and has nothing already waiting on the teacher. */
  protected readonly showForm = computed(() => {
    const way = this.way();
    return way !== null && way.canPay && way.outstanding > 0 && this.claim()?.status !== 'Open';
  });

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    amount: [null as number | null, [Validators.required, Validators.min(1)]],
    note: [''],
  });

  /** The teacher's LankaQR, once the pay screen says there is one. */
  protected readonly qr = new BlobImage(inject(DestroyRef));

  constructor() {
    this.api.how(this.data.enrolmentId, this.data.month, this.data.studentId).subscribe({
      next: (way) => {
        this.way.set(way);
        this.claim.set(way.claim);
        this.form.controls.amount.setValue(way.outstanding > 0 ? way.outstanding : null);
        this.loading.set(false);
        if (way.canPay && way.hasQr) {
          // The image only helps, so a failure leaves the account details to pay by.
          this.api.qr(this.data.enrolmentId, way.qrVersion, this.data.studentId).subscribe({ next: (blob) => this.qr.set(blob), error: () => this.qr.set(null) });
        }
      },
      error: (e) => {
        this.failed.set(inlineError(e));
        this.loading.set(false);
      },
    });
  }

  protected copy(text: string): void {
    navigator.clipboard?.writeText(text).then(
      () => this.toast.success('pay.copied'),
      () => this.toast.error('pay.copy_failed'),
    );
  }

  protected pick(event: Event): void {
    const chosen = (event.target as HTMLInputElement).files?.[0] ?? null;
    if (chosen && chosen.size > MAX_SLIP_BYTES) {
      this.error.set('error.slip_too_large');
      return;
    }

    this.error.set('');
    this.slip.set(chosen);
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;

    this.busy.set(true);
    this.error.set('');
    const { amount, note } = this.form.getRawValue();
    this.api
      .claim(this.data.enrolmentId, { month: this.data.month, amount: Number(amount), note: note.trim() || null }, this.data.studentId)
      .subscribe({
        next: (claim) => this.afterClaim(claim),
        error: (e) => {
          this.busy.set(false);
          this.error.set(inlineError(e));
        },
      });
  }

  /** The claim is made; the slip, if there is one, is a second request so the photo never blocks the claim itself. */
  private afterClaim(claim: PaymentClaim): void {
    const file = this.slip();
    if (!file) {
      this.done(claim);
      return;
    }

    this.api.addSlip(claim.id, file).subscribe({
      next: (withSlip) => this.done(withSlip),
      error: (e) => {
        // The teacher already has the claim, so the slip failing is worth saying but not worth losing it over.
        this.toast.error(errorCode(e) === 'slip_too_large' ? 'error.slip_too_large' : 'pay.slip_failed');
        this.done(claim);
      },
    });
  }

  private done(claim: PaymentClaim): void {
    this.busy.set(false);
    this.toast.success('pay.sent');
    this.ref.close(claim);
  }
}
