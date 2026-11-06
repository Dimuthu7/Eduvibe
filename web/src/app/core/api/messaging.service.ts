import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';

/** How an alert reaches a parent. Only `app` costs the teacher nothing. */
export type MessageWay = 'app' | 'whatsapp' | 'sms' | 'whatsapp_then_sms';

/** GuruBook's words for one event in one language, and the teacher's own when they wrote some. */
export interface MessageText {
  language: string;
  default: string;
  custom: string | null;
}

/** One of the messages that go out on their own, and what the teacher chose for it. */
export interface MessageEvent {
  event: string;
  on: boolean;
  way: MessageWay;
  note: string | null;
  /** Only for the low attendance warning: the share of classes a child must attend to say nothing. */
  percent: number | null;
  /** The ways this event can be sent: WhatsApp is missing where Meta has approved no template. */
  ways: MessageWay[];
  /** The fields its words may use, in the order their values are filled in. */
  fields: string[];
  texts: MessageText[];
}

export interface MessageSettings {
  events: MessageEvent[];
  lowAttendanceWeeks: number;
  lowAttendanceQuietDays: number;
  noteMaxLength: number;
}

export interface SaveRuleInput {
  on: boolean;
  way: MessageWay;
  note?: string | null;
  percent?: number | null;
}

/** Which alerts go out, how they are sent, and what the paid ones say. */
@Injectable({ providedIn: 'root' })
export class MessagingApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  private url(path: string): string {
    return `${this.config.apiBaseUrl}/api/messaging${path}`;
  }

  settings() {
    return this.http.get<MessageSettings>(this.url(''));
  }

  saveRule(event: string, input: SaveRuleInput) {
    return this.http.put<MessageSettings>(this.url(`/${event}`), input);
  }

  /** An empty text puts GuruBook's own words back. */
  saveText(event: string, language: string, text: string) {
    return this.http.put<MessageSettings>(this.url(`/${event}/texts/${language}`), { text });
  }
}
