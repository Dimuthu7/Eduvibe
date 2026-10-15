import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';
import { quiet } from './quiet';

/** One thing the person is told. Kind and Params say what happened; the screen writes the words. */
export interface AppNotification {
  id: string;
  kind: string;
  params: Record<string, unknown>;
  link: string | null;
  /** How many events this stands for (three students asked to join one class). */
  count: number;
  isRead: boolean;
  at: string;
}

export interface NotificationList {
  unread: number;
  items: AppNotification[];
}

@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(AppConfig).apiBaseUrl}/api/notifications`;

  list() {
    return this.http.get<NotificationList>(this.base);
  }

  /** How many are unread, asked quietly because it runs in the background. */
  unread() {
    return this.http.get<{ unread: number }>(`${this.base}/unread`, { context: quiet() });
  }

  read(id: string) {
    return this.http.post<{ unread: number }>(`${this.base}/${id}/read`, {});
  }

  readAll() {
    return this.http.post<{ unread: number }>(`${this.base}/read-all`, {});
  }
}
