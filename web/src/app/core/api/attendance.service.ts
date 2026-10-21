import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';

export type AttendanceStatus = 'Present' | 'Late' | 'Absent';

/** One meeting of a class on one date. */
export interface ClassSession {
  id: string;
  classId: string;
  classTitle: string;
  date: string;
  start: string;
  end: string;
  isExtra: boolean;
  note: string | null;
  /** True once the teacher has saved attendance for it. */
  taken: boolean;
  present: number;
  late: number;
  absent: number;
  /** Today and the week before: attendance can still be marked or corrected. */
  editable: boolean;
}

export interface SessionStudent {
  enrolmentId: string;
  indexNumber: number;
  fullName: string;
  status: AttendanceStatus;
}

export interface SessionDetail {
  session: ClassSession;
  students: SessionStudent[];
}

export interface ExtraSessionInput {
  classId: string;
  date: string;
  start: string;
  end: string;
  note: string | null;
}

export interface StudentSummary {
  enrolmentId: string;
  indexNumber: number;
  fullName: string;
  sessions: number;
  present: number;
  late: number;
  absent: number;
  percent: number;
}

export interface ClassSummary {
  classId: string;
  from: string;
  to: string;
  sessions: number;
  percent: number;
  students: StudentSummary[];
}

export interface ReportEntry {
  date: string;
  start: string;
  end: string;
  isExtra: boolean;
  status: AttendanceStatus;
}

/** One student's attendance in one class over a period: every class that was marked, oldest first. */
export interface StudentReport {
  enrolmentId: string;
  studentId: string;
  indexNumber: number;
  fullName: string;
  classId: string;
  classTitle: string;
  from: string;
  to: string;
  sessions: number;
  present: number;
  late: number;
  absent: number;
  percent: number;
  entries: ReportEntry[];
  /** The teacher's classes this student is in, so the report can switch between them. */
  classes: { enrolmentId: string; classId: string; title: string }[];
}

/** A student's own attendance in one class. */
export interface MyAttendance {
  classId: string;
  sessions: number;
  present: number;
  late: number;
  absent: number;
  percent: number;
  recent: { date: string; status: AttendanceStatus }[];
}

/** Class sessions, who attended them, and attendance summaries. */
@Injectable({ providedIn: 'root' })
export class AttendanceService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(AppConfig).apiBaseUrl}/api/attendance`;

  /** The sessions of one day; the server makes them from the class timetable the first time a recent day is opened. */
  sessions(date: string) {
    return this.http.get<ClassSession[]>(`${this.base}/sessions`, { params: { date } });
  }

  session(id: string) {
    return this.http.get<SessionDetail>(`${this.base}/sessions/${id}`);
  }

  addExtra(input: ExtraSessionInput) {
    return this.http.post<ClassSession>(`${this.base}/sessions`, input);
  }

  /** Saves the class's attendance. Only the students who are not present need listing. */
  save(id: string, marks: { enrolmentId: string; status: AttendanceStatus }[]) {
    return this.http.put<ClassSession>(`${this.base}/sessions/${id}/marks`, { marks });
  }

  remove(id: string) {
    return this.http.delete<void>(`${this.base}/sessions/${id}`);
  }

  summary(classId: string, from: string, to: string) {
    return this.http.get<ClassSummary>(`${this.base}/summary`, { params: { classId, from, to } });
  }

  studentReport(enrolmentId: string, from: string, to: string) {
    return this.http.get<StudentReport>(`${this.base}/enrolments/${enrolmentId}/report`, { params: { from, to } });
  }

  mine() {
    return this.http.get<MyAttendance[]>(`${this.base}/mine`);
  }

  /** One child's attendance in each class, for a parent. */
  child(studentId: string) {
    return this.http.get<MyAttendance[]>(`${this.base}/children/${studentId}`);
  }
}
