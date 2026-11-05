import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { FeeTiming, MyFees } from '../core/api/fees.service';
import { AuthService } from '../core/auth/auth.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { TranslateService } from '../core/i18n/translate.service';
import { feeDueText } from './fee-due-text';
import { formatLkr, todayColombo } from './format';
import { PayDialog, PayTarget } from './pay-dialog';

/** One class whose fee needs attention: due soon, due today or late. */
export interface FeeAlertItem {
  classId: string;
  classTitle: string;
  /** The student's place in the class, and the fee's first month: what the pay screen needs to name the fee. */
  enrolmentId: string;
  month: string;
  /** What is still owed for the fee. */
  amount: number;
  dueDate: string;
  timing: Exclude<FeeTiming, ''>;
  daysLate: number;
}

const URGENCY: Record<Exclude<FeeTiming, ''>, number> = { Overdue: 0, DueToday: 1, DueSoon: 2 };

/**
 * The fees a student or parent should hear about: for each class the most urgent fee that is late, due today or due within a few days.
 * Late ones come first, then today's, then the soonest.
 */
export function feeAlerts(fees: readonly MyFees[], titleOf: (classId: string) => string): FeeAlertItem[] {
  const items: FeeAlertItem[] = [];
  for (const f of fees) {
    const urgent = f.months
      .filter((m): m is typeof m & { timing: Exclude<FeeTiming, ''> } => m.timing !== '' && m.due - m.paid > 0)
      .sort((a, b) => URGENCY[a.timing] - URGENCY[b.timing] || a.dueDate.localeCompare(b.dueDate))[0];
    if (urgent) {
      items.push({
        classId: f.classId, classTitle: titleOf(f.classId), enrolmentId: f.enrolmentId, month: urgent.month,
        amount: urgent.due - urgent.paid, dueDate: urgent.dueDate, timing: urgent.timing, daysLate: urgent.daysLate,
      });
    }
  }
  return items.sort((a, b) => URGENCY[a.timing] - URGENCY[b.timing] || a.dueDate.localeCompare(b.dueDate) || a.classTitle.localeCompare(b.classTitle));
}

const storageKey = (userId: string): string => `gurubook.fees-alert.${userId}`;

/**
 * A card at the top of the student's and parent's screen listing fees that are late, due today or due soon, so they cannot be missed when the app opens.
 * "Hide for today" quiets it until tomorrow (Sri Lanka time). A fee whose teacher takes bank transfers gets a Pay button, which opens the
 * pay screen; the rest are still settled with the teacher in person.
 */
@Component({
  selector: 'app-fees-alert',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    @if (items().length > 0 && !hidden()) {
      <section class="alert" [class.late]="late()" role="status" [attr.aria-label]="'alert.fees_title' | t">
        <div class="head">
          <mat-icon aria-hidden="true">{{ late() ? 'event_busy' : 'schedule' }}</mat-icon>
          <strong>{{ (late() ? 'alert.fees_late' : 'alert.fees_soon') | t }}</strong>
        </div>
        @for (item of items(); track item.classId) {
          <div class="item">
            <div class="row-main">
              <div class="row-title">{{ item.classTitle }}</div>
              <div class="row-meta">{{ when(item) }}</div>
            </div>
            <strong class="amount">{{ money(item.amount) }}</strong>
            @if (canPay(item)) {
              <button mat-stroked-button type="button" class="pay" (click)="pay(item)">{{ 'pay.button' | t }}</button>
            }
          </div>
        }
        <div class="actions">
          <button mat-button type="button" (click)="hideToday()">{{ 'alert.hide_today' | t }}</button>
        </div>
      </section>
    }
  `,
  styles: `
    :host { display: block; }
    .alert { margin-bottom: 1rem; padding: 0.75rem 1rem 0.25rem; border-radius: var(--mat-sys-corner-large); background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
    .alert.late { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .head { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.25rem; font: var(--mat-sys-title-small); }
    .head mat-icon { font-size: 1.5rem; width: 1.5rem; height: 1.5rem; }
    .item { display: flex; align-items: center; gap: 0.75rem; padding: 0.5rem 0; border-top: 1px solid color-mix(in srgb, currentColor 20%, transparent); }
    .item .row-meta { color: inherit; opacity: 0.85; }
    .amount { white-space: nowrap; font: var(--mat-sys-label-large); }
    .pay { flex: none; }
    .actions { display: flex; justify-content: flex-end; margin: 0 -0.5rem; }
  `,
})
export class FeesAlert {
  readonly items = input.required<FeeAlertItem[]>();

  /** The enrolments whose teacher takes bank transfers. Others get no Pay button, so nobody is led to a dead end. */
  readonly payable = input<readonly string[]>([]);

  /** The child whose fee this is, when a parent is looking. */
  readonly studentId = input<string | undefined>(undefined);

  /** Fires once a claim has been sent, so the screen behind can reload. */
  readonly claimed = output<void>();

  private readonly i18n = inject(TranslateService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  protected readonly money = formatLkr;
  protected readonly late = computed(() => this.items().some((i) => i.timing === 'Overdue'));
  protected readonly hidden = signal(this.hiddenToday());

  protected when(item: FeeAlertItem): string {
    return feeDueText(this.i18n, item);
  }

  protected canPay(item: FeeAlertItem): boolean {
    return this.payable().includes(item.enrolmentId);
  }

  protected pay(item: FeeAlertItem): void {
    const data: PayTarget = { enrolmentId: item.enrolmentId, month: item.month, classTitle: item.classTitle, studentId: this.studentId() };
    this.dialog
      .open(PayDialog, { data, width: '420px', maxWidth: '96vw' })
      .afterClosed()
      .subscribe((claim) => {
        if (claim) this.claimed.emit();
      });
  }

  protected hideToday(): void {
    this.hidden.set(true);
    try {
      localStorage.setItem(storageKey(this.auth.user()?.id ?? ''), todayColombo());
    } catch {
      // Storage can be blocked; the card is simply hidden until the page reloads.
    }
  }

  private hiddenToday(): boolean {
    try {
      return localStorage.getItem(storageKey(this.auth.user()?.id ?? '')) === todayColombo();
    } catch {
      return false;
    }
  }
}
