export type Role = 'SuperAdmin' | 'Teacher' | 'InstituteAdmin' | 'Student' | 'Parent';

export interface User {
  id: string;
  phone: string;
  username: string | null;
  mustChooseUsername: boolean;
  firstName: string;
  lastName: string;
  fullName: string;
  email: string | null;
  language: string;
  roles: Role[];
  mustChangePassword: boolean;
  teacherId: string | null;
  district: string | null;
  streamId: string | null;
  subjectIds: string[];
}

export interface Session {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  user: User;
}

export interface ProfileInput {
  firstName: string;
  lastName: string;
  email: string;
  district: string | null;
  streamId: string | null;
  subjectIds: string[];
}
