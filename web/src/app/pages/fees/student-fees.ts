import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { EnrolmentFees, FeesService, PaymentRecord } from '../../core/api/fees.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { AuthService } from '../../core/auth/auth.service';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { FeeStatus } from '../../shared/fee-status';
import { fileNamePart } from '../../shared/download';
import { formatDate, formatLkr, formatMonth, formatMonthShort } from '../../shared/format';
import { drawReceipt, shareOrSave } from '../../shared/receipt';
import { PaymentDialog } from './payment-dialog';
import { VoidDialog } from './void-dialog';

/** One student's fees in one class: every month with what is due and paid, and every payment with its receipt number. */
@Component({
  selector: 'app-student-fees',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, FeeStatus, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <a mat-button routerLink="/fees" [queryParams]="{ classId: fees()?.classId, month: month() || null }" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'fees.title' | t }}
    </a>

    @switch (record.status()) {
      @case ('ready') {
        @if (fees(); as f) {
          <header class="page-header">
            <div>
              <h1 class="page-title">{{ f.fullName }}</h1>
              <p class="page-subtitle">#{{ f.indexNumber }} · {{ f.classTitle }} · {{ planText(f) }}</p>
            </div>
            <button mat-flat-button type="button" (click)="pay(f)" [disabled]="f.feePlan === 'FreeCard'">
              <mat-icon>add</mat-icon>
              {{ 'fees.record_payment' | t }}
            </button>
          </header>

          <div class="owing" [class.clear]="f.outstanding === 0">
            <span class="muted">{{ 'fees.outstanding' | t }}</span>
            <strong>{{ money(f.outstanding) }}</strong>
          </div>

          <h2 class="section-title">{{ 'fees.months_title' | t }}</h2>
          <div class="panel">
            @for (d of f.dues; track d.month) {
              <div class="panel-row">
                <div class="row-main">
                  <div class="row-title">{{ monthName(d.month) }}</div>
                  <div class="row-meta">{{ d.status === 'FreeCard' ? ('fees.free_card' | t) : money(d.paid) + ' / ' + money(d.due) }}</div>
                </div>
                <app-fee-status [status]="d.status" />
              </div>
            } @empty {
              <div class="panel-row muted">{{ 'fees.no_months' | t }}</div>
            }
          </div>

          <h2 class="section-title">{{ 'fees.payments_title' | t }}</h2>
          <div class="panel">
            @for (p of f.payments; track p.id) {
              <div class="panel-row" [class.voided]="!!p.voidedAt">
                <mat-icon class="lead" aria-hidden="true">{{ p.voidedAt ? 'block' : 'receipt_long' }}</mat-icon>
                <div class="row-main">
                  <div class="row-title amount">{{ money(p.amount) }}</div>
                  <div class="row-meta"><span class="receipt-no">{{ p.receiptNo }}</span> · {{ 'fees.method.' + p.method | t }}</div>
                  <div class="row-meta">{{ date(p.paidOn) }}</div>
                  @if (p.voidedAt) {
                    <div class="row-meta void-note">{{ 'fees.void_tag' | t }}: {{ p.voidReason }}</div>
                  } @else if (p.allocations.length > 0) {
                    <div class="row-meta">{{ allocationText(p) }}</div>
                  }
                </div>
                @if (!p.voidedAt) {
                  <button mat-icon-button type="button" (click)="shareReceipt(f, p)" [attr.aria-label]="'fees.receipt' | t">
                    <mat-icon>share</mat-icon>
                  </button>
                  <button mat-button type="button" (click)="confirmVoid(p)">{{ 'fees.void' | t }}</button>
                }
              </div>
            } @empty {
              <div class="panel-row muted">{{ 'fees.no_payments' | t }}</div>
            }
          </div>
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
    .back { margin-left: -0.75rem; margin-bottom: 0.5rem; }
    .owing { display: grid; gap: 0.125rem; margin-bottom: 0.5rem; padding: 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .owing .muted { color: inherit; opacity: 0.8; }
    .owing strong { font: var(--mat-sys-headline-small); }
    .owing.clear { background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .lead { color: var(--mat-sys-on-surface-variant); }
    .amount { font-weight: 600; }
    .receipt-no { white-space: nowrap; }
    .voided .amount { text-decoration: line-through; }
    .voided { color: var(--mat-sys-on-surface-variant); }
    .void-note { color: var(--mat-sys-error); }
  `,
})
export class StudentFees {
  /** The enrolment id from the route, and the month the teacher came from. */
  readonly enrolmentId = input.required<string>();
  readonly month = input<string>('');

  private readonly api = inject(FeesService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(TranslateService);
  private readonly auth = inject(AuthService);

  protected readonly record = new Loadable<EnrolmentFees>();
  protected readonly fees = computed(() => this.record.data());
  protected readonly money = formatLkr;
  protected readonly monthName = formatMonth;
  protected readonly date = formatDate;

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.record.load(this.api.enrolment(this.enrolmentId()));
  }

  protected planText(f: EnrolmentFees): string {
    const plan = this.i18n.translate(`students.plan.${f.feePlan}`);
    return f.feePlan === 'Discount' ? `${plan} · ${formatLkr(f.discountAmount)}` : plan;
  }

  protected allocationText(p: PaymentRecord): string {
    return p.allocations.map((a) => `${formatMonthShort(a.month)} ${formatLkr(a.amount)}`).join(' · ');
  }

  /** Makes the payment's receipt as an image and offers it to share (WhatsApp, for example), or saves it. */
  protected async shareReceipt(f: EnrolmentFees, p: PaymentRecord): Promise<void> {
    const t = (key: string) => this.i18n.translate(key);
    const user = this.auth.user();
    const image = await drawReceipt(
      {
        teacher: `${user?.firstName ?? ''} ${user?.lastName ?? ''}`.trim(),
        className: f.classTitle,
        student: f.fullName,
        receiptNo: p.receiptNo,
        amount: formatLkr(p.amount),
        method: t(`fees.method.${p.method}`),
        paidOn: formatDate(p.paidOn),
        months: p.allocations.map((a) => formatMonthShort(a.month)).join(', '),
      },
      {
        title: t('receipt.title'),
        receiptNo: t('receipt.number'),
        student: t('receipt.student'),
        className: t('receipt.class'),
        paidBy: t('fees.method'),
        date: t('receipt.date'),
        months: t('receipt.months'),
        amount: t('receipt.amount'),
        thanks: t('receipt.thanks'),
      },
    );
    const result = await shareOrSave(image, `${fileNamePart(`${t('receipt.title')} ${p.receiptNo}`)}.png`, `${t('receipt.title')} ${p.receiptNo}`);
    if (result === 'saved') this.toast.success('fees.receipt_saved');
  }

  protected pay(f: EnrolmentFees): void {
    this.dialog
      .open<PaymentDialog, EnrolmentFees, PaymentRecord>(PaymentDialog, { data: f, width: '40rem', maxWidth: 'calc(100vw - 1rem)' })
      .afterClosed()
      .subscribe((payment) => {
        if (!payment) return;
        this.toast.success('fees.recorded');
        this.load();
      });
  }

  protected confirmVoid(p: PaymentRecord): void {
    this.dialog
      .open<VoidDialog, PaymentRecord, PaymentRecord>(VoidDialog, { data: p, width: '36rem', maxWidth: 'calc(100vw - 1rem)' })
      .afterClosed()
      .subscribe((voided) => {
        if (!voided) return;
        this.toast.success('fees.voided');
        this.load();
      });
  }
}
