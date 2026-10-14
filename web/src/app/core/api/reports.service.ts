import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';

export type ReportKey = 'class-list' | 'fee-status' | 'exam-results' | 'attendance';

/** Excel reports. The server builds the file; the screen only saves it. */
@Injectable({ providedIn: 'root' })
export class ReportsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(AppConfig).apiBaseUrl}/api/reports`;

  /** The report as a file. Goes through the same HTTP client as everything else, so the sign-in token and error handling apply. */
  download(key: ReportKey, params: Record<string, string>) {
    return this.http.get(`${this.base}/${key}`, { params, responseType: 'blob' });
  }
}
