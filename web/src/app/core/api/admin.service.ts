import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';

export interface TeacherSummary {
  id: string;
  firstName: string;
  lastName: string;
  fullName: string;
  phone: string;
  email: string | null;
  district: string | null;
  streamId: string | null;
  subjectIds: string[];
  isActive: boolean;
  mustChangePassword: boolean;
}

export interface NewTeacher {
  firstName: string;
  lastName: string;
  phone: string;
  email?: string;
  district: string;
  streamId: string;
  subjectIds: string[];
}

export interface Institute {
  id: string;
  name: string;
  district: string | null;
  town: string | null;
  address: string | null;
  phone: string | null;
  isActive: boolean;
  teacherIds: string[];
}

export type InstituteInput = Pick<Institute, 'name' | 'district'> & Partial<Pick<Institute, 'town' | 'address' | 'phone' | 'isActive'>>;

/** Super Admin calls: teachers and institutes. */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(AppConfig).apiBaseUrl;

  teachers() {
    return this.http.get<TeacherSummary[]>(`${this.base}/api/identity/teachers`);
  }

  createTeacher(teacher: NewTeacher) {
    return this.http.post<{ teacher: TeacherSummary; oneTimePassword: string }>(`${this.base}/api/identity/teachers`, teacher);
  }

  resetPassword(teacherId: string) {
    return this.http.post<{ oneTimePassword: string }>(`${this.base}/api/identity/teachers/${teacherId}/reset-password`, {});
  }

  setTeacherActive(teacherId: string, isActive: boolean) {
    return this.http.put<void>(`${this.base}/api/identity/teachers/${teacherId}/active`, { isActive });
  }

  institutes() {
    return this.http.get<Institute[]>(`${this.base}/api/classes/institutes`);
  }

  createInstitute(institute: InstituteInput) {
    return this.http.post<Institute>(`${this.base}/api/classes/institutes`, institute);
  }

  updateInstitute(id: string, institute: InstituteInput) {
    return this.http.put<Institute>(`${this.base}/api/classes/institutes/${id}`, institute);
  }

  setInstituteTeachers(id: string, teacherIds: string[]) {
    return this.http.put<void>(`${this.base}/api/classes/institutes/${id}/teachers`, { teacherIds });
  }

  myInstitutes() {
    return this.http.get<Institute[]>(`${this.base}/api/classes/my-institutes`);
  }
}
