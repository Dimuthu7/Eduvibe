import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Role } from './auth.models';
import { AuthService } from './auth.service';

/** Needs a signed-in user who has already replaced a one-time password. */
export const activeGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!(await auth.restore())) return router.parseUrl('/login');
  return auth.user()!.mustChangePassword ? router.parseUrl('/change-password') : true;
};

/** Needs a signed-in user; used by the change-password screen itself. */
export const signedInGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  return (await auth.restore()) ? true : inject(Router).parseUrl('/login');
};

/** The sign-in screen: skip it when a session already exists. */
export const guestGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  return (await auth.restore()) ? inject(Router).parseUrl(auth.homeRoute()) : true;
};

export const roleGuard =
  (role: Role): CanActivateFn =>
  () => {
    const auth = inject(AuthService);
    return auth.hasRole(role) ? true : inject(Router).parseUrl(auth.homeRoute());
  };
