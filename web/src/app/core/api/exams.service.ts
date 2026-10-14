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

/**
 * How the class did, from the students who sat the exam and have every part marked. Highest, lowest, average and median
 * are totals out of the exam's maximum. Only the lowest grade fails; passMark is where the next grade starts.
 */
export interface ExamAnalysis {
  sat: number;
  absent: number;
  highest: number | null;
  lowest: number | null;
  average: number | null;
  median: number | null;
  passMark: number | null;
  passed: number;
  passRate: number | null;
  /** Best grade first. */
  grades: { grade: string; count: number }[];
  parts: { partId: string; name: string; maxMark: number; average: number | null }[];
}

export interface ExamDetail {
  id: string;
  classId: string;
  classTitle: string;
  name: string;
  heldOn: string;
  maxTotal: number;
  /** When the results were shared with students, or null while they are not. */
  publishedAt: string | null;
  parts: ExamPart[];
  bands: GradeBand[];
  summary: { students: number; marked: number; averagePercent: number | null };
  analysis: ExamAnalysis;
  students: StudentResult[];
}

/** One exam in a student's history. Rank reads "rank of ranked"; classAverage is the ranked students' average percentage. */
export interface HistoryItem {
  examId: string;
  name: string;
  heldOn: string;
  maxTotal: number;
  absent: boolean;
  total: number | null;
  percent: number | null;
  grade: string | null;
  rank: number | null;
  ranked: number;
  classAverage: number | null;
  publishedAt: string | null;
}

/** One student's results across the exams of one class, oldest first, with their other classes to switch to. */
export interface StudentHistory {
  enrolmentId: string;
  studentId: string;
  indexNumber: number;
  fullName: string;
  classId: string;
  classTitle: string;
  exams: HistoryItem[];
  classes: { enrolmentId: string; classId: string; title: string }[];
}

/** A published exam as the student sees it: only their own marks. */
export interface MyExam {
  examId: string;
  classId: string;
  name: string;
  heldOn: string;
  maxTotal: number;
  absent: boolean;
  total: number | null;
  percent: number | null;
  grade: string | null;
  rank: number | null;
  ranked: number;
  parts: { name: string; maxMark: number; mark: number | null }[];
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
  publishedAt: string | null;
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

  /** Shares the results with the class: each student then sees their own marks, grade and rank. */
  publish(id: string) {
    return this.http.post<ExamDetail>(`${this.base}/${id}/publish`, null);
  }

  unpublish(id: string) {
    return this.http.post<ExamDetail>(`${this.base}/${id}/unpublish`, null);
  }

  /** One student's results across every exam of their class. */
  history(enrolmentId: string) {
    return this.http.get<StudentHistory>(`${this.base}/enrolments/${enrolmentId}/history`);
  }

  /** The signed-in student's published results, newest first. */
  mine() {
    return this.http.get<MyExam[]>(`${this.base}/mine`);
  }

  gradeScheme() {
    return this.http.get<GradeScheme>(`${this.base}/grade-scheme`);
  }

  saveGradeScheme(bands: GradeBand[]) {
    return this.http.put<GradeScheme>(`${this.base}/grade-scheme`, { bands });
  }
}
