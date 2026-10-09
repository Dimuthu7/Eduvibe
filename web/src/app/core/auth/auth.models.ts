export type Role = 'SuperAdmin' | 'Teacher' | 'InstituteAdmin' | 'Student' | 'Parent';

export interface User {
  id: string;
  phone: string;
  fullName: string;
  email: string | null;
  language: string;
  roles: Role[];
  mustChangePassword: boolean;
  teacherId: string | null;
  town: string | null;
  subjects: string | null;
}

export interface Session {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  user: User;
}
