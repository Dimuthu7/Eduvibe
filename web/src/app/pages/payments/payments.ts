import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { BankDetails, PaymentsApi, TeacherClaim } from '../../core/api/payments.service';
import { inlineError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { BlobImage } from '../../shared/blob-image';
import { formatLkr, formatMonth } from '../../shared/format';
import { ReasonDialog } from '../../shared/reason-dialog';
import { SubmitButton } from '../../shared/submit-button';
import { LoadError } from '../../shared/load-error';

/** Nothing bigger than this is sent, matching what the API takes. */
const MAX_QR_BYTES = 2 * 1024 * 1024;

/**
 * Where a teacher's students send money, and the transfers waiting on their word. Confirming one records an ordinary bank transfer payment
 * with a receipt; rejecting it asks why and tells the student. GuruBook never holds the money: this is the teacher's own account and their
 * own decision.
 */
@Component({
  selector: 'app-payments',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    LoadError,
    ReactiveFormsModule, SubmitButton, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslatePipe,
  ],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'payments.title' | t }}</h1>
        <p class="page-subtitle">{{ 'payments.subtitle' | t }}</p>
      </div>
    </header>

    @switch (data.status()) {
      @case ('loading') {
        <p class="muted">{{ 'common.loading' | t }}</p>
      }
      @case ('error') {
        <app-load-error [error]="data.error()" (retry)="load()" />
      }
      @case ('ready') {
        <mat-card appearance="outlined">
          <mat-card-content>
            <h2 class="section-title first">{{ 'payments.details_title' | t }}</h2>
            <p class="muted hint">{{ 'payments.details_hint' | t }}</p>
            @if (error()) {
              <p class="banner" role="alert">{{ error() | t }}</p>
            }
            <form [formGroup]="form" (ngSubmit)="save()" novalidate>
              <mat-form-field>
                <mat-label>{{ 'pay.bank' | t }}</mat-label>
                <input matInput formControlName="bankName" maxlength="100" required />
              </mat-form-field>
              <mat-form-field>
                <mat-label>{{ 'pay.account_name' | t }}</mat-label>
                <input matInput formControlName="accountName" maxlength="100" required />
              </mat-form-field>
              <mat-form-field>
                <mat-label>{{ 'pay.account_number' | t }}</mat-label>
                <input matInput formControlName="accountNumber" maxlength="40" required />
              </mat-form-field>
              <mat-form-field>
                <mat-label>{{ 'payments.branch' | t }}</mat-label>
                <input matInput formControlName="branch" maxlength="100" />
              </mat-form-field>
              <mat-form-field>
                <mat-label>{{ 'payments.note' | t }}</mat-label>
                <input matInput formControlName="note" maxlength="300" />
                <mat-hint>{{ 'payments.note_hint' | t }}</mat-hint>
              </mat-form-field>
              <div class="actions">
                <app-submit-button label="common.save" [busy]="busy()" />
              </div>
            </form>

            <h3 class="section-title">{{ 'pay.lankaqr' | t }}</h3>
            <p class="muted hint">{{ 'payments.qr_hint' | t }}</p>
            @if (qr.url(); as image) {
              <img class="qr" [src]="image" [alt]="'pay.lankaqr' | t" />
            }
            <div class="actions start">
              <button mat-stroked-button type="button" (click)="file.click()">
                <mat-icon>qr_code_2</mat-icon> {{ (details()?.hasQr ? 'payments.replace_qr' : 'payments.add_qr') | t }}
              </button>
              <input #file type="file" accept="image/*" hidden (change)="pickQr($event)" />
              @if (details()?.hasQr) {
                <button mat-button type="button" (click)="removeQr()">{{ 'payments.remove_qr' | t }}</button>
              }
            </div>
          </mat-card-content>
        </mat-card>

        <h2 class="section-title">{{ 'payments.waiting_title' | t }}</h2>
        @if (waiting().length === 0) {
          <p class="muted">{{ 'payments.none_waiting' | t }}</p>
        }
        @for (claim of waiting(); track claim.id) {
          <mat-card appearance="outlined" class="claim">
            <mat-card-content>
              <div class="head">
                <div class="row-main">
                  <div class="row-title">{{ claim.fullName }}</div>
                  <div class="row-meta">{{ claim.classTitle }} · {{ month(claim.month) }}</div>
                </div>
                <strong class="amount">{{ money(claim.amount) }}</strong>
              </div>
              <p class="line">
                {{ 'pay.reference' | t }} <span class="mono">{{ claim.reference }}</span>
                @if (claim.outstanding !== claim.amount) {
                  <span class="muted"> · {{ 'payments.owes_now' | t }} {{ money(claim.outstanding) }}</span>
                }
              </p>
              @if (claim.note) {
                <p class="line">{{ claim.note }}</p>
              }
              @if (claim.hasSlip) {
                @if (slips().get(claim.id); as image) {
                  <img class="slip-image" [src]="image" [alt]="'payments.view_slip' | t" />
                } @else {
                  <button mat-button type="button" class="slip" (click)="showSlip(claim)">
                    <mat-icon aria-hidden="true">receipt_long</mat-icon> {{ 'payments.view_slip' | t }}
                  </button>
                }
              } @else {
                <p class="line muted">{{ 'payments.no_slip' | t }}</p>
              }
              <div class="actions">
                <button mat-button type="button" [disabled]="deciding()" (click)="reject(claim)">{{ 'payments.reject' | t }}</button>
                <button mat-flat-button type="button" [disabled]="deciding()" (click)="confirm(claim)">{{ 'payments.confirm' | t }}</button>
              </div>
            </mat-card-content>
          </mat-card>
        }

        @if (decided().length > 0) {
          <h2 class="section-title">{{ 'payments.decided_title' | t }}</h2>
          @for (claim of decided(); track claim.id) {
            <div class="past">
              <div class="row-main">
                <div class="row-title">{{ claim.fullName }}</div>
                <div class="row-meta">
                  {{ month(claim.month) }} ·
                  @if (claim.status === 'Confirmed') {
                    {{ 'payments.confirmed' | t }} {{ claim.receiptNo }}
                  } @else {
                    {{ 'payments.rejected' | t }}@if (claim.rejectReason) { : {{ claim.rejectReason }} }
                  }
                </div>
              </div>
              <strong class="amount">{{ money(claim.amount) }}</strong>
            </div>
          }
        }
      }
    }
  `,
  styles: `
    .hint { margin: 0 0 0.75rem; font: var(--mat-sys-body-small); }
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    form { display: flex; flex-direction: column; gap: 0.25rem; }
    .actions { display: flex; justify-content: flex-end; gap: 0.5rem; flex-wrap: wrap; }
    .actions.start { justify-content: flex-start; }
    .section-title { margin: 1.5rem 0 0.25rem; font: var(--mat-sys-title-medium); }
    .section-title.first { margin-top: 0; }
    .qr { display: block; max-width: 200px; width: 100%; height: auto; margin: 0 0 0.75rem; border-radius: var(--mat-sys-corner-medium); }
    .claim { margin-bottom: 0.75rem; }
    .head { display: flex; align-items: flex-start; gap: 0.75rem; }
    .row-main { flex: 1; min-width: 0; }
    .amount { white-space: nowrap; font: var(--mat-sys-title-small); }
    .line { margin: 0.35rem 0 0; }
    .mono { font-family: ui-monospace, monospace; letter-spacing: 0.04em; }
    .slip { margin-top: 0.25rem; }
    .slip-image { display: block; max-width: 240px; width: 100%; height: auto; margin-top: 0.5rem; border-radius: var(--mat-sys-corner-medium); }
    .empty { padding: 1.5rem 0; text-align: center; }
    .past { display: flex; align-items: center; gap: 0.75rem; padding: 0.6rem 0; border-bottom: 1px solid var(--mat-sys-outline-variant); }
  `,
})
export class PaymentsPage {
  private readonly api = inject(PaymentsApi);
  private readonly toast = inject(ToastService);
  private readonly dialog = inject(MatDialog);

  protected readonly data = new Loadable<{ details: BankDetails; claims: TeacherClaim[] }>();
  protected readonly money = formatLkr;
  protected readonly month = formatMonth;
  protected readonly busy = signal(false);
  protected readonly deciding = signal(false);
  protected readonly error = signal('');
  protected readonly details = computed(() => this.data.data()?.details);
  protected readonly waiting = computed(() => (this.data.data()?.claims ?? []).filter((c) => c.status === 'Open'));
  protected readonly decided = computed(() => (this.data.data()?.claims ?? []).filter((c) => c.status !== 'Open'));

  protected readonly qr = new BlobImage(inject(DestroyRef));
  protected readonly slips = signal<ReadonlyMap<string, string>>(new Map());
  private readonly slipUrls: string[] = [];
  private qrShown: number | null = null;

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    bankName: ['', [Validators.required]],
    accountName: ['', [Validators.required]],
    accountNumber: ['', [Validators.required]],
    branch: [''],
    note: [''],
  });

  constructor() {
    this.load();
    // The QR needs the sign-in header, so it is fetched rather than put in an src.
    effect(() => {
      const version = this.details()?.hasQr ? this.details()!.qrVersion : null;
      if (version === null || version === this.qrShown) return;
      this.qrShown = version;
      this.api.myQr(version).subscribe({ next: (blob) => this.qr.set(blob), error: () => this.qr.set(null) });
    });
    inject(DestroyRef).onDestroy(() => this.slipUrls.forEach((url) => URL.revokeObjectURL(url)));
    // The form follows what the server holds, on the first load and after a save, but never over something the teacher is still typing.
    effect(() => {
      const details = this.details();
      if (details && this.form.pristine) this.form.patchValue(details);
    });
  }

  /** Shows one claim's slip. It is fetched when asked for, so a list of claims does not pull every photo down.  */
  protected showSlip(claim: TeacherClaim): void {
    if (this.slips().has(claim.id)) return;
    this.api.slip(claim.id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        this.slipUrls.push(url);
        this.slips.update((current) => new Map(current).set(claim.id, url));
      },
      error: (e) => this.toast.error(inlineError(e)),
    });
  }

  protected save(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.busy.set(true);
    this.error.set('');
    this.api.saveDetails(this.form.getRawValue()).subscribe({
      next: (details) => {
        this.busy.set(false);
        this.apply(details);
        this.form.markAsPristine();
        this.toast.success('payments.details_saved');
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(inlineError(e));
      },
    });
  }

  protected pickQr(event: Event): void {
    const input = event.target as HTMLInputElement;
    const chosen = input.files?.[0];
    input.value = '';
    if (!chosen) return;
    if (chosen.size > MAX_QR_BYTES) {
      this.error.set('error.qr_too_large');
      return;
    }

    this.error.set('');
    this.api.saveQr(chosen).subscribe({
      next: (details) => {
        this.apply(details);
        this.toast.success('payments.qr_saved');
      },
      error: (e) => this.error.set(inlineError(e)),
    });
  }

  protected removeQr(): void {
    this.api.removeQr().subscribe({
      next: () => this.load(),
      error: (e) => this.error.set(inlineError(e)),
    });
  }

  protected confirm(claim: TeacherClaim): void {
    this.deciding.set(true);
    this.api.confirm(claim.id).subscribe({
      next: () => {
        this.deciding.set(false);
        this.toast.success('payments.confirmed_toast');
        this.load();
      },
      error: (e) => {
        this.deciding.set(false);
        this.toast.error(inlineError(e));
        this.load();
      },
    });
  }

  protected reject(claim: TeacherClaim): void {
    this.dialog
      .open(ReasonDialog, {
        data: { title: 'payments.reject_title', label: 'payments.reject_reason', hint: 'payments.reject_hint', submit: 'payments.reject' },
        width: '380px', maxWidth: '96vw',
      })
      .afterClosed()
      .subscribe((reason) => {
        if (!reason) return;
        this.deciding.set(true);
        this.api.reject(claim.id, reason).subscribe({
          next: () => {
            this.deciding.set(false);
            this.toast.success('payments.rejected_toast');
            this.load();
          },
          error: (e) => {
            this.deciding.set(false);
            this.toast.error(inlineError(e));
            this.load();
          },
        });
      });
  }

  protected load(): void {
    this.data.load(forkJoin({ details: this.api.details(), claims: this.api.claims() }));
  }

  /** Keeps the screen in step with what the server stored, so a trimmed value is what the teacher sees. */
  private apply(details: BankDetails): void {
    this.data.update((current) => ({ ...current, details }));
  }
}
