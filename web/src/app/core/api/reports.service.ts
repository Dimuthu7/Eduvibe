import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';

export type ReportKey = 'class-list' | 'fee-status' | 'exam-results' | 'attendance';

/** A report as rows, the same ones the Excel file holds. The app draws them as a PDF. */
export interface ReportTableData {
  title: string;
  headers: string[];
  rows: (string | number | null)[][];
}

/** Reports. The server builds the Excel file; the same report is also offered as rows that the app draws as a PDF. */
@Injectable({ providedIn: 'root' })
export class ReportsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(AppConfig).apiBaseUrl}/api/reports`;

  /** The report as a file. Goes through the same HTTP client as everything else, so the sign-in token and error handling apply. */
  download(key: ReportKey, params: Record<string, string>) {
    return this.http.get(`${this.base}/${key}`, { params, responseType: 'blob' });
  }

  /** The report as rows, for the PDF. */
  table(key: ReportKey, params: Record<string, string>) {
    return this.http.get<ReportTableData>(`${this.base}/${key}/table`, { params });
  }
}
