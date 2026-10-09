import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom, tap } from 'rxjs';
import { AppConfig } from '../config/app-config';
import { Role, Session, User } from './auth.models';

const STORAGE_KEY = 'eduvibe.session';

interface StoredSession {
  accessToken: string;
  refreshToken: string;
}

/** Holds the signed-in user and the tokens. Tokens survive a page reload; they are cleared on sign-out. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);
  private readonly router = inject(Router);

  private tokens: StoredSession | null = this.read();
  private refreshing: Promise<boolean> | null = null;

  readonly user = signal<User | null>(null);
  readonly signedIn = computed(() => this.user() !== null);

  get accessToken(): string | null {
    return this.tokens?.accessToken ?? null;
  }

  hasStoredSession(): boolean {
    return this.tokens !== null;
  }

  hasRole(role: Role): boolean {
    return this.user()?.roles.includes(role) ?? false;
  }

  /** Where this user starts after signing in. */
  homeRoute(): string {
    const user = this.user();
    if (!user) return '/login';
    if (user.mustChangePassword) return '/change-password';
    return this.hasRole('SuperAdmin') ? '/admin/teachers' : '/home';
  }

  login(phone: string, password: string) {
    return this.http
      .post<Session>(this.url('/api/identity/login'), { phone, password })
      .pipe(tap((session) => this.accept(session)));
  }

  changePassword(currentPassword: string, newPassword: string) {
    return this.http
      .post<Session>(this.url('/api/identity/change-password'), { currentPassword, newPassword })
      .pipe(tap((session) => this.accept(session)));
  }

  updateProfile(profile: { fullName: string; email: string; town: string; subjects: string }) {
    return this.http
      .put<User>(this.url('/api/identity/me'), profile)
      .pipe(tap((user) => this.user.set(user)));
  }

  /** Restores the user after a page reload. Returns false when there is no valid session. */
  async restore(): Promise<boolean> {
    if (this.user()) return true;
    if (!this.tokens) return false;
    try {
      this.user.set(await firstValueFrom(this.http.get<User>(this.url('/api/identity/me'))));
      return true;
    } catch {
      return (await this.refresh()) && this.user() !== null;
    }
  }

  /** Swaps the refresh token for a new pair. Concurrent callers share one request. */
  refresh(): Promise<boolean> {
    if (!this.tokens) return Promise.resolve(false);
    this.refreshing ??= firstValueFrom(
      this.http.post<Session>(this.url('/api/identity/refresh'), { refreshToken: this.tokens.refreshToken }),
    )
      .then((session) => {
        this.accept(session);
        return true;
      })
      .catch(() => {
        this.clear();
        return false;
      })
      .finally(() => (this.refreshing = null));
    return this.refreshing;
  }

  async logout(): Promise<void> {
    const refreshToken = this.tokens?.refreshToken;
    this.clear();
    if (refreshToken) {
      // Best effort: the local session is already gone even if the server cannot be reached.
      await firstValueFrom(this.http.post(this.url('/api/identity/logout'), { refreshToken })).catch(() => undefined);
    }
    await this.router.navigateByUrl('/login');
  }

  /** Called when the server rejects the session for good. */
  expire(): void {
    this.clear();
    void this.router.navigateByUrl('/login');
  }

  private accept(session: Session): void {
    this.tokens = { accessToken: session.accessToken, refreshToken: session.refreshToken };
    this.user.set(session.user);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.tokens));
    } catch {
      // Storage can be blocked; the session then lasts until the page closes.
    }
  }

  private clear(): void {
    this.tokens = null;
    this.user.set(null);
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      // Nothing to clean up.
    }
  }

  private read(): StoredSession | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? (JSON.parse(raw) as StoredSession) : null;
    } catch {
      return null;
    }
  }

  private url(path: string): string {
    return `${this.config.apiBaseUrl}${path}`;
  }
}
