import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';

/** How a teacher's paid messages go out. */
export type MessageChannel = 'whatsapp_then_sms' | 'sms_only';

export const MESSAGE_CHANNELS: readonly MessageChannel[] = ['whatsapp_then_sms', 'sms_only'];

/** Whether SMS is open to this teacher and this server. Limit and used count paid parts, not messages. */
export interface SmsStatus {
  enabled: boolean;
  configured: boolean;
  /** The server only pretends to send (a demo). */
  simulated: boolean;
  limit: number | null;
  used: number;
  remaining: number | null;
  channel: MessageChannel;
}

export type SmsMessageStatus = 'Queued' | 'Sent' | 'Delivered' | 'Failed';

export interface SmsMessage {
  id: string;
  studentName: string;
  kind: 'fee_reminder' | 'absence_alert' | 'results' | 'payment_received' | 'low_attendance';
  status: SmsMessageStatus;
  error: string | null;
  phone: string;
  text: string;
  parts: number;
  /** True when WhatsApp could not deliver the message and this text took over. */
  fallback: boolean;
  /** True while this number is asking for no more texts. */
  stopped: boolean;
  createdAt: string;
}

export interface SmsList {
  status: SmsStatus;
  items: SmsMessage[];
}

@Injectable({ providedIn: 'root' })
export class SmsApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  private url(path: string): string {
    return `${this.config.apiBaseUrl}${path}`;
  }

  status() {
    return this.http.get<SmsStatus>(this.url('/api/sms/status'));
  }

  messages() {
    return this.http.get<SmsList>(this.url('/api/sms/messages'));
  }

  saveChannel(channel: MessageChannel) {
    return this.http.put<{ channel: MessageChannel }>(this.url('/api/sms/channel'), { channel });
  }

  /** Stops, or starts again, texts to the number one message went to. */
  stop(id: string, on: boolean) {
    return this.http.put<{ on: boolean }>(this.url(`/api/sms/messages/${id}/stop`), { on });
  }

  /** Whether this person's own number is taking texts. For a parent or a student, about themselves. */
  myTexts() {
    return this.http.get<{ on: boolean }>(this.url('/api/sms/my-texts'));
  }

  saveMyTexts(on: boolean) {
    return this.http.put<{ on: boolean }>(this.url('/api/sms/my-texts'), { on });
  }
}
