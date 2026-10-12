import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';

export type MonthStatus = 'Paid' | 'PartlyPaid' | 'Unpaid' | 'FreeCard';
export type PaymentMethod = 'Cash' | 'BankTransfer' | 'Online';

export interface MonthRow {
  enrolmentId: string;
  indexNumber: number;
  fullName: string;
  status: MonthStatus;
  due: number;
  paid: number;
}

export interface MonthTotals {
  paid: number;
  partlyPaid: number;
  unpaid: number;
  freeCard: number;
  due: number;
  collected: number;
  outstanding: number;
}

/** One class in one month: who has paid, who has not, and the totals. */
export interface ClassMonth {
  classId: string;
  month: string;
  totals: MonthTotals;
  rows: MonthRow[];
}

/** What one student owes for one month. */
export interface FeeDue {
  month: string;
  due: number;
  paid: number;
  status: MonthStatus;
}

export interface PaymentRecord {
  id: string;
  enrolmentId: string;
  receiptNo: string;
  amount: number;
  method: PaymentMethod;
  paidOn: string;
  voidedAt: string | null;
  voidReason: string | null;
  allocations: { month: string; amount: number }[];
}

/** A student's months and payments in one class. */
export interface EnrolmentFees {
  enrolmentId: string;
  studentId: string;
  fullName: string;
  indexNumber: number;
  classId: string;
  classTitle: string;
  feePlan: 'Full' | 'Discount' | 'FreeCard';
  discountAmount: number;
  isActive: boolean;
  outstanding: number;
  dues: FeeDue[];
  payments: PaymentRecord[];
}

export interface PaymentInput {
  enrolmentId: string;
  amount: number;
  method: PaymentMethod;
  paidOn: string;
  /** Months to put the money toward ("yyyy-MM"); empty means the oldest unpaid months first. */
  months: string[];
}

/** A student's own fee status in one class. */
export interface MyFees {
  classId: string;
  outstanding: number;
  months: FeeDue[];
  receipts: { receiptNo: string; amount: number; method: PaymentMethod; paidOn: string }[];
}

/** Monthly fees: what is due, payments (and voiding them), and who has paid. */
@Injectable({ providedIn: 'root' })
export class FeesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(AppConfig).apiBaseUrl}/api/fees`;

  classMonth(classId: string, month: string) {
    return this.http.get<ClassMonth>(`${this.base}/classes/${classId}`, { params: { month } });
  }

  enrolment(enrolmentId: string) {
    return this.http.get<EnrolmentFees>(`${this.base}/enrolments/${enrolmentId}`);
  }

  pay(input: PaymentInput) {
    return this.http.post<PaymentRecord>(`${this.base}/payments`, input);
  }

  voidPayment(id: string, reason: string) {
    return this.http.post<PaymentRecord>(`${this.base}/payments/${id}/void`, { reason });
  }

  mine() {
    return this.http.get<MyFees[]>(`${this.base}/mine`);
  }
}
