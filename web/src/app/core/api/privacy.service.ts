import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';

/** Super Admin: hand over or erase what the app holds about a teacher. */
@Injectable({ providedIn: 'root' })
export class PrivacyService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(AppConfig).apiBaseUrl}/api/privacy`;

  /** Everything held about a teacher, as a JSON file. */
  exportTeacher(teacherId: string) {
    return this.http.get(`${this.base}/teachers/${teacherId}/export`, { responseType: 'blob' });
  }

  /** Erases the teacher's data. The full name must be typed back; the teacher must be switched off first. */
  eraseTeacher(teacherId: string, confirm: string) {
    return this.http.post<void>(`${this.base}/teachers/${teacherId}/erase`, { confirm });
  }
}
