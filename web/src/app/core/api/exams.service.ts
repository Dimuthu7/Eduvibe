import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';

export interface GradeBand {
  grade: string;
  minPercent: number;
}

export interface GradeScheme {
  name: string;
  bands: GradeBand[];
}

export interface ExamPart {
  id: string;
  name: string;
  maxMark: number;
}

/** One student in an exam. Total, percent, grade and rank are set once every part has a mark; an absent student has none. */
export interface StudentResult {
  enrolmentId: string;
  indexNumber: number;
  fullName: string;
  isActive: boolean;
  absent: boolean;
  /** Marks entered so far, by part id. */
  marks: Record<string, number>;
  total: number | null;
  percent: number | null;
  grade: string | null;
  rank: number | null;
}

export interface ExamDetail {
  id: string;
  classId: string;
  classTitle: string;
  name: string;
  heldOn: string;
  maxTotal: number;
  parts: ExamPart[];
  bands: GradeBand[];
  summary: { students: number; marked: number; averagePercent: number | null };
  students: StudentResult[];
}

export interface ExamListItem {
  id: string;
  classId: string;
  classTitle: string;
  name: string;
  heldOn: string;
  maxTotal: number;
  parts: number;
  students: number;
  marked: number;
  averagePercent: number | null;
}

export interface ExamInput {
  classId: string;
  name: string;
  heldOn: string;
  /** Existing parts carry their id; new ones do not. */
  parts: { id?: string; name: string; maxMark: number }[];
}

export interface MarkRow {
  enrolmentId: string;
  absent: boolean;
  /** Part id to mark; a part left out or null has not been entered. */
  marks: Record<string, number | null>;
}

/** Exams, their marks and results, and the teacher's grade scheme. */
@Injectable({ providedIn: 'root' })
export class ExamsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(AppConfig).apiBaseUrl}/api/exams`;

  list(classId?: string, limit?: number) {
    const params: Record<string, string> = {};
    if (classId) params['classId'] = classId;
    if (limit) params['limit'] = String(limit);
    return this.http.get<ExamListItem[]>(this.base, { params });
  }

  exam(id: string) {
    return this.http.get<ExamDetail>(`${this.base}/${id}`);
  }

  create(input: ExamInput) {
    return this.http.post<ExamDetail>(this.base, input);
  }

  update(id: string, input: ExamInput) {
    return this.http.put<ExamDetail>(`${this.base}/${id}`, input);
  }

  remove(id: string) {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  /** Saves the rows sent and returns the whole exam with totals, grades and ranks worked out. */
  saveMarks(id: string, rows: MarkRow[]) {
    return this.http.put<ExamDetail>(`${this.base}/${id}/marks`, { rows });
  }

  gradeScheme() {
    return this.http.get<GradeScheme>(`${this.base}/grade-scheme`);
  }

  saveGradeScheme(bands: GradeBand[]) {
    return this.http.put<GradeScheme>(`${this.base}/grade-scheme`, { bands });
  }
}
