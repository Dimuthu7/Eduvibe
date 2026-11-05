import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';

/** Where a teacher's students send money. Empty strings mean the teacher has not filled this in. */
export interface BankDetails {
  bankName: string;
  accountName: string;
  accountNumber: string;
  branch: string;
  note: string;
  hasQr: boolean;
  /** Changes whenever the QR image changes, so the phone keeps the old one no longer than it should. */
  qrVersion: number;
}

export type ClaimStatus = 'Open' | 'Confirmed' | 'Rejected';

/** A claim as the person who made it sees it. */
export interface PaymentClaim {
  id: string;
  status: ClaimStatus;
  amount: number;
  month: string;
  reference: string;
  note: string;
  hasSlip: boolean;
  createdAt: string;
  decidedAt: string | null;
  rejectReason: string | null;
  receiptNo: string | null;
}

/** What a student or parent is shown on the pay screen. `canPay` is false when the teacher takes money in person. */
export interface PayWay {
  canPay: boolean;
  reference: string;
  outstanding: number;
  month: string;
  dueDate: string;
  classTitle: string;
  bankName: string;
  accountName: string;
  accountNumber: string;
  branch: string;
  note: string;
  hasQr: boolean;
  qrVersion: number;
  claim: PaymentClaim | null;
}

/** A claim as the teacher sees it, with who it is from. `outstanding` is what the fee still owes right now. */
export interface TeacherClaim {
  id: string;
  enrolmentId: string;
  studentId: string;
  indexNumber: number;
  fullName: string;
  classTitle: string;
  status: ClaimStatus;
  amount: number;
  outstanding: number;
  month: string;
  reference: string;
  note: string;
  hasSlip: boolean;
  createdAt: string;
  decidedAt: string | null;
  rejectReason: string | null;
  receiptNo: string | null;
}

export interface BankDetailsInput {
  bankName: string;
  accountName: string;
  accountNumber: string;
  branch: string;
  note: string;
}

/** Paying a fee by bank transfer or LankaQR: the teacher's details, and the claims waiting on their word. */
@Injectable({ providedIn: 'root' })
export class PaymentsApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  private url(path: string): string {
    return `${this.config.apiBaseUrl}/api/payments${path}`;
  }

  details() {
    return this.http.get<BankDetails>(this.url('/details'));
  }

  saveDetails(input: BankDetailsInput) {
    return this.http.put<BankDetails>(this.url('/details'), input);
  }

  /**
   * The teacher's own QR image. It is fetched rather than put in an `src`, because the API needs the sign-in header; the version is sent
   * so the browser keeps an unchanged image and a new one is a new address.
   */
  myQr(version: number) {
    return this.http.get(this.url('/qr'), { params: { v: version }, responseType: 'blob' });
  }

  saveQr(file: Blob) {
    return this.http.put<BankDetails>(this.url('/qr'), file, { headers: { 'content-type': file.type || 'image/jpeg' } });
  }

  removeQr() {
    return this.http.delete<void>(this.url('/qr'));
  }

  claims() {
    return this.http.get<TeacherClaim[]>(this.url('/claims'));
  }

  /** The slip photo of one claim, for the teacher looking at it. */
  slip(id: string) {
    return this.http.get(this.url(`/claims/${id}/slip`), { responseType: 'blob' });
  }

  confirm(id: string, amount?: number) {
    return this.http.post<PaymentClaim>(this.url(`/claims/${id}/confirm`), { amount: amount ?? null });
  }

  reject(id: string, reason: string) {
    return this.http.post<PaymentClaim>(this.url(`/claims/${id}/reject`), { reason });
  }

  /** The fees that can be paid this way: the signed-in student's own, or one child's. */
  payable(studentId?: string) {
    return this.http.get<string[]>(this.url(studentId ? `/children/${studentId}` : '/mine'));
  }

  how(enrolmentId: string, month: string, studentId?: string) {
    return this.http.get<PayWay>(this.url(`${this.base(studentId)}/${enrolmentId}`), { params: { month } });
  }

  /** The QR of the teacher who owns this enrolment, for their own student or that student's parent. */
  qr(enrolmentId: string, version: number, studentId?: string) {
    return this.http.get(this.url(`${this.base(studentId)}/${enrolmentId}/qr`), { params: { v: version }, responseType: 'blob' });
  }

  claim(enrolmentId: string, body: { month: string; amount: number; note: string | null }, studentId?: string) {
    return this.http.post<PaymentClaim>(this.url(`${this.base(studentId)}/${enrolmentId}/claims`), body);
  }

  addSlip(claimId: string, file: Blob) {
    return this.http.put<PaymentClaim>(this.url(`/claims/${claimId}/slip`), file, {
      headers: { 'content-type': file.type || 'image/jpeg' },
    });
  }

  private base(studentId?: string): string {
    return studentId ? `/children/${studentId}` : '/mine';
  }
}
