import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';
import { quiet } from './quiet';
import { ClassSession } from './attendance.service';
import { TimetableEntry } from './classes.service';
import { FeeOverview } from './fees.service';
import { Institute } from './admin.service';
import { StudentRow } from './students.service';

/** One widget in the teacher's saved layout. */
export interface WidgetSetting {
  key: string;
  enabled: boolean;
}

/** Customised is false until the teacher saves once, so the app uses its own default order. */
export interface DashboardLayout {
  widgets: WidgetSetting[];
  customised: boolean;
}

/** A student the teacher may want to ring: missed the latest classes, or came rarely lately. */
export interface WatchRow {
  enrolmentId: string;
  classId: string;
  classTitle: string;
  indexNumber: number;
  fullName: string;
  missedInARow: number;
  sessions: number;
  percent: number;
  lastAttended: string | null;
}

export interface Watch {
  windowDays: number;
  total: number;
  rows: WatchRow[];
}

/** The latest exam of one class. Marked of Students says how much of the marking is done. */
export interface ExamProgressClass {
  classId: string;
  classTitle: string;
  examId: string;
  examName: string;
  heldOn: string;
  students: number;
  marked: number;
  averagePercent: number | null;
  previousAverage: number | null;
  published: boolean;
}

/** A student whose percentage fell from the exam before to the latest one. */
export interface ExamDrop {
  enrolmentId: string;
  classId: string;
  classTitle: string;
  indexNumber: number;
  fullName: string;
  examName: string;
  previous: number;
  latest: number;
}

export interface ExamProgress {
  dropPoints: number;
  classes: ExamProgressClass[];
  droppedTotal: number;
  dropped: ExamDrop[];
}

/**
 * The teacher's start screen: the saved layout, and what each widget shows. Every widget asks quietly, because each one shows its own
 * message and retry button when it cannot load, and one slow or failing widget must not toast over the rest.
 */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(AppConfig).apiBaseUrl;

  layout() {
    return this.http.get<DashboardLayout>(`${this.api}/api/dashboard/layout`);
  }

  saveLayout(widgets: WidgetSetting[]) {
    return this.http.put<DashboardLayout>(`${this.api}/api/dashboard/layout`, { widgets });
  }

  /** Back to the app's own default order and choices. */
  resetLayout() {
    return this.http.delete<DashboardLayout>(`${this.api}/api/dashboard/layout`);
  }

  sessions(date: string) {
    return this.http.get<ClassSession[]>(`${this.api}/api/attendance/sessions`, { params: { date }, context: quiet() });
  }

  timetable() {
    return this.http.get<TimetableEntry[]>(`${this.api}/api/classes/timetable`, { context: quiet() });
  }

  pendingJoins() {
    return this.http.get<StudentRow[]>(`${this.api}/api/students`, { params: new HttpParams().set('status', 'pending'), context: quiet() });
  }

  feeOverview(month: string) {
    return this.http.get<FeeOverview>(`${this.api}/api/fees/overview`, { params: { month }, context: quiet() });
  }

  attendanceWatch() {
    return this.http.get<Watch>(`${this.api}/api/attendance/watch`, { context: quiet() });
  }

  examProgress() {
    return this.http.get<ExamProgress>(`${this.api}/api/exams/progress`, { context: quiet() });
  }

  institutes() {
    return this.http.get<Institute[]>(`${this.api}/api/classes/my-institutes`, { context: quiet() });
  }
}
