import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';
import { quiet } from './quiet';

/** What a teacher is reporting. The words for each one live in the translation files. */
export const TICKET_KINDS = ['problem', 'question', 'idea', 'billing'] as const;

export type TicketKind = (typeof TICKET_KINDS)[number];

export type TicketStatus = 'Open' | 'InProgress' | 'Resolved';

export const TICKET_STATUSES: TicketStatus[] = ['Open', 'InProgress', 'Resolved'];

/** One message in a ticket's conversation, from the teacher or from support. */
export interface TicketReply {
  id: string;
  body: string;
  fromSupport: boolean;
  at: string;
}

/** A ticket as the teacher who raised it sees it. */
export interface Ticket {
  id: string;
  kind: string;
  details: string;
  screen: string | null;
  status: TicketStatus;
  hasShot: boolean;
  at: string;
  answeredAt: string | null;
  replies: TicketReply[];
}

/** The support screen: who to call, when, and everything this teacher has reported. */
export interface SupportScreen {
  phone: string | null;
  hours: string | null;
  tickets: Ticket[];
}

/** A ticket in the Super Admin's inbox, with the teacher who raised it. */
export interface AdminTicket extends Ticket {
  teacherId: string;
  teacherName: string;
}

export interface SupportSettings {
  phone: string | null;
  hours: string | null;
}

export interface NewTicket {
  kind: TicketKind;
  details: string;
  /** The screen the teacher was on, so support does not have to ask where they were. */
  screen?: string | null;
}

/** Report an issue, from the teacher's side and the Super Admin's. */
@Injectable({ providedIn: 'root' })
export class SupportService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  private url(path: string): string {
    return `${this.config.apiBaseUrl}/api/support${path}`;
  }

  /** The number to call, when it is answered, and this teacher's own tickets. */
  mine() {
    return this.http.get<SupportScreen>(this.url(''));
  }

  raise(input: NewTicket) {
    return this.http.post<Ticket>(this.url('/tickets'), input);
  }

  reply(ticketId: string, body: string) {
    return this.http.post<Ticket>(this.url(`/tickets/${ticketId}/replies`), { body });
  }

  /** The screenshot, sent on its own so a list never carries pictures. */
  saveShot(ticketId: string, file: Blob) {
    return this.http.put<void>(this.url(`/tickets/${ticketId}/shot`), file, {
      headers: { 'Content-Type': file.type || 'image/jpeg' },
    });
  }

  /** The picture itself. The API needs the sign-in header, so it is fetched and shown through `BlobImage`. */
  shot(ticketId: string, admin = false) {
    return this.http.get(this.url(`${admin ? '/admin' : ''}/tickets/${ticketId}/shot`), { responseType: 'blob', context: quiet() });
  }

  /** Every teacher's tickets, newest first; a status narrows it to one. */
  all(status?: TicketStatus) {
    return this.http.get<AdminTicket[]>(this.url('/admin/tickets'), { params: status ? { status } : {} });
  }

  answer(ticketId: string, body: string) {
    return this.http.post<AdminTicket>(this.url(`/admin/tickets/${ticketId}/replies`), { body });
  }

  setStatus(ticketId: string, status: TicketStatus) {
    return this.http.put<AdminTicket>(this.url(`/admin/tickets/${ticketId}/status`), { status });
  }

  settings() {
    return this.http.get<SupportSettings>(this.url('/admin/settings'));
  }

  saveSettings(input: SupportSettings) {
    return this.http.put<SupportSettings>(this.url('/admin/settings'), input);
  }
}
