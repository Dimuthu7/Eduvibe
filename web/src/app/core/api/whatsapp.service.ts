import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';
import { quiet } from './quiet';

/** Whether WhatsApp is open to this teacher and this server, and the month's allowance (limit null = no cap). */
export interface WhatsAppStatus {
  enabled: boolean;
  configured: boolean;
  /** The server only pretends to send (a demo). */
  simulated: boolean;
  limit: number | null;
  used: number;
  remaining: number | null;
}

/** What sending would do, from the screen that offers the send. */
export interface WhatsAppPreview {
  configured: boolean;
  total: number;
  ready: number;
  noNumber: number;
  optedOut: number;
  alreadySent: number;
  overAllowance: number;
  limit: number | null;
  used: number;
  noNumberNames: string[];
}

export interface WhatsAppSendResult {
  configured: boolean;
  queued: number;
  noNumber: number;
  optedOut: number;
  alreadySent: number;
  overAllowance: number;
}

export type WhatsAppMessageStatus = 'Queued' | 'Sent' | 'Delivered' | 'Read' | 'Failed';

export interface WhatsAppMessage {
  id: string;
  studentName: string;
  kind: 'fee_reminder' | 'absence_alert' | 'results';
  status: WhatsAppMessageStatus;
  error: string | null;
  phone: string;
  createdAt: string;
}

export interface WhatsAppList {
  status: WhatsAppStatus;
  items: WhatsAppMessage[];
}

@Injectable({ providedIn: 'root' })
export class WhatsAppApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  private url(path: string): string {
    return `${this.config.apiBaseUrl}${path}`;
  }

  status() {
    return this.http.get<WhatsAppStatus>(this.url('/api/whatsapp/status'));
  }

  messages() {
    return this.http.get<WhatsAppList>(this.url('/api/whatsapp/messages'));
  }

  /** Preview and send share one address on the owning module's route (fees, attendance, exams). */
  preview(path: string) {
    return this.http.get<WhatsAppPreview>(this.url(path), { context: quiet() });
  }

  send(path: string) {
    return this.http.post<WhatsAppSendResult>(this.url(path), null);
  }
}
