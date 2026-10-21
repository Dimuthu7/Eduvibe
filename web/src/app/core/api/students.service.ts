import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';
import { quiet } from './quiet';

export type EnrolmentStatus = 'Pending' | 'Active' | 'Left';
export type FeePlan = 'Full' | 'Discount' | 'FreeCard';

export const FEE_PLANS: FeePlan[] = ['Full', 'Discount', 'FreeCard'];

/** One enrolment with its student: a row of a class list or of the pending list. */
export interface StudentRow {
  enrolmentId: string;
  studentId: string;
  classId: string;
  classTitle: string;
  indexNumber: number;
  firstName: string;
  lastName: string;
  fullName: string;
  phone: string;
  school: string | null;
  grade: string | null;
  status: EnrolmentStatus;
  feePlan: FeePlan;
  discountAmount: number;
  joinedOn: string;
}

export interface StudentEnrolment {
  id: string;
  classId: string;
  classTitle: string;
  indexNumber: number;
  status: EnrolmentStatus;
  feePlan: FeePlan;
  discountAmount: number;
  joinedOn: string;
}

export interface Student {
  id: string;
  firstName: string;
  lastName: string;
  fullName: string;
  phone: string;
  school: string | null;
  grade: string | null;
  guardianName: string | null;
  guardianPhone: string | null;
  hasAccount: boolean;
  /** This teacher has linked the guardian as a parent who can see the student's records. */
  hasParent: boolean;
  enrolments: StudentEnrolment[];
}

export interface StudentDetails {
  firstName: string;
  lastName: string;
  school: string;
  grade: string;
  guardianName: string;
  guardianPhone: string;
}

export interface AddStudentInput extends StudentDetails {
  phone: string;
  classId: string;
  indexNumber: number | null;
  feePlan: FeePlan;
  discountAmount: number | null;
  enrolExisting?: boolean;
}

export interface EnrolInput {
  classId: string;
  indexNumber: number | null;
  feePlan: FeePlan;
  discountAmount: number | null;
}

export interface EnrolmentChange {
  indexNumber: number | null;
  status: EnrolmentStatus;
  feePlan: FeePlan;
  discountAmount: number | null;
}

export interface StudentCreated {
  row: StudentRow;
  /** Only for a brand new student: shown once so the teacher can pass it on. */
  oneTimePassword: string | null;
}

export type InviteStatus = 'Active' | 'Expired' | 'Full' | 'Revoked';

export interface InviteLink {
  id: string;
  classId: string;
  classTitle: string;
  token: string;
  expiresAt: string;
  maxUses: number | null;
  usedCount: number;
  status: InviteStatus;
  createdAt: string;
}

export interface InviteInfo {
  classTitle: string;
  teacherName: string | null;
  expiresAt: string;
}

export interface RegistrationInput extends StudentDetails {
  phone: string;
  password: string;
}

/** "created": a new account with the chosen password. "existing_account": the phone number already had one. */
export interface Registered {
  outcome: 'created' | 'existing_account';
  classTitle: string;
}

/** A class as the student sees it: who teaches it and when it meets. Start and end are "HH:mm". */
export interface MyClass {
  enrolmentId: string;
  classId: string;
  classTitle: string;
  teacherName: string | null;
  slots: { day: number; start: string; end: string }[];
}

/** What the teacher gets back from inviting a parent. The password is set only when the parent had no account yet. */
export interface ParentInvited {
  name: string;
  phone: string;
  accountCreated: boolean;
  oneTimePassword: string | null;
}

export interface ChildClass {
  enrolmentId: string;
  classId: string;
  classTitle: string;
  teacherName: string | null;
  /** The teacher's plan does not include the parent portal, so nothing of this class is shown. */
  locked: boolean;
  slots: { day: number; start: string; end: string }[];
}

/** One child of the signed-in parent, with the classes a teacher has shared. */
export interface Child {
  studentId: string;
  firstName: string;
  fullName: string;
  classes: ChildClass[];
}

/** A student who has a profile photo. The version changes whenever the photo does, so a phone keeps what it has until then. */
export interface StudentPhotoEntry {
  studentId: string;
  version: number;
  /** The teacher's own enrolments of this student, so a row that only knows its enrolment can find the photo. */
  enrolmentIds: string[];
}

/** A teacher's students, their enrolments in classes, and the links students use to sign themselves up. */
@Injectable({ providedIn: 'root' })
export class StudentsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(AppConfig).apiBaseUrl}/api/students`;

  students(filter: { classId?: string; status?: string; q?: string } = {}) {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) if (value) params = params.set(key, value);
    return this.http.get<StudentRow[]>(this.base, { params });
  }

  student(id: string) {
    return this.http.get<Student>(`${this.base}/${id}`);
  }

  add(input: AddStudentInput) {
    return this.http.post<StudentCreated>(this.base, input);
  }

  update(id: string, details: StudentDetails) {
    return this.http.put<void>(`${this.base}/${id}`, details);
  }

  enrol(studentId: string, input: EnrolInput) {
    return this.http.post<StudentRow>(`${this.base}/${studentId}/enrolments`, input);
  }

  updateEnrolment(id: string, change: EnrolmentChange) {
    return this.http.put<StudentRow>(`${this.base}/enrolments/${id}`, change);
  }

  approve(enrolmentId: string) {
    return this.http.post<StudentRow>(`${this.base}/enrolments/${enrolmentId}/approve`, {});
  }

  reject(enrolmentId: string) {
    return this.http.post<void>(`${this.base}/enrolments/${enrolmentId}/reject`, {});
  }

  resetPassword(studentId: string) {
    return this.http.post<{ oneTimePassword: string }>(`${this.base}/${studentId}/reset-password`, {});
  }

  /** Links the student's guardian phone to a parent account, made now if there is none. */
  inviteParent(studentId: string) {
    return this.http.post<ParentInvited>(`${this.base}/${studentId}/parent`, {});
  }

  resetParentPassword(studentId: string) {
    return this.http.post<{ oneTimePassword: string }>(`${this.base}/${studentId}/parent/reset-password`, {});
  }

  removeParent(studentId: string) {
    return this.http.delete<void>(`${this.base}/${studentId}/parent`);
  }

  /** The signed-in parent's children. */
  family() {
    return this.http.get<Child[]>(`${this.base}/family`);
  }

  /** Who has a photo among the teacher's students. Quiet: a failure just leaves initials showing. */
  photos() {
    return this.http.get<StudentPhotoEntry[]>(`${this.base}/photos`, { context: quiet() });
  }

  photo(studentId: string, version: number) {
    return this.http.get(`${this.base}/${studentId}/photo`, { params: { v: version }, responseType: 'blob', context: quiet() });
  }

  setPhoto(studentId: string, image: Blob) {
    return this.http.put<StudentPhotoEntry>(`${this.base}/${studentId}/photo`, image, { headers: { 'Content-Type': image.type || 'image/jpeg' } });
  }

  removePhoto(studentId: string) {
    return this.http.delete<void>(`${this.base}/${studentId}/photo`);
  }

  /** The signed-in student's own photo; null when the teacher has not added one. */
  myPhoto() {
    return this.http.get(`${this.base}/me/photo`, { responseType: 'blob', context: quiet() });
  }

  /** The signed-in student's own classes. */
  myClasses() {
    return this.http.get<MyClass[]>(`${this.base}/me/classes`);
  }

  invites(classId?: string) {
    return this.http.get<InviteLink[]>(`${this.base}/invites`, { params: classId ? { classId } : {} });
  }

  createInvite(classId: string, expiresInDays: number, maxUses: number | null) {
    return this.http.post<InviteLink>(`${this.base}/invites`, { classId, expiresInDays, maxUses });
  }

  revokeInvite(id: string) {
    return this.http.post<InviteLink>(`${this.base}/invites/${id}/revoke`, {});
  }

  /** The two calls behind the public sign-up page. */
  inviteInfo(token: string) {
    return this.http.get<InviteInfo>(`${this.base}/register/${encodeURIComponent(token)}`);
  }

  register(token: string, input: RegistrationInput) {
    return this.http.post<Registered>(`${this.base}/register/${encodeURIComponent(token)}`, input);
  }
}
