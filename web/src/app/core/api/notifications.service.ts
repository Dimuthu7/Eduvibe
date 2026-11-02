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

/** Whether the server can send phone notifications, and the public key a phone needs to subscribe. */
export interface PushSettings {
  enabled: boolean;
  publicKey: string;
}

/** A phone's address and keys as the browser gives them. */
export interface PushDevice {
  endpoint: string;
  p256dh: string;
  auth: string;
  language: string;
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

  /** Closes one for good: it no longer shows in the person's list. Says how many are still unread. */
  dismiss(id: string) {
    return this.http.delete<{ unread: number }>(`${this.base}/${id}`);
  }

  /** Asked quietly: it runs in the background, and a failure just means the turn-on step is not offered. */
  pushSettings() {
    return this.http.get<PushSettings>(`${this.base}/push`, { context: quiet() });
  }

  /** Registers this phone for the signed-in person, or updates it (for example after the language changed). */
  savePushDevice(device: PushDevice, background = false) {
    return this.http.put<void>(`${this.base}/push/device`, device, background ? { context: quiet() } : {});
  }

  removePushDevice(endpoint: string) {
    return this.http.post<void>(`${this.base}/push/unsubscribe`, { endpoint }, { context: quiet() });
  }
}
