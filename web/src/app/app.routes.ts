import { Routes } from '@angular/router';
import { activeGuard, guestGuard, roleGuard, signedInGuard } from './core/auth/guards';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'home' },
  { path: 'login', canActivate: [guestGuard], loadComponent: () => import('./pages/login/login').then((m) => m.Login) },
  {
    path: 'change-password',
    canActivate: [signedInGuard],
    loadComponent: () => import('./pages/change-password/change-password').then((m) => m.ChangePassword),
  },
  { path: 'status', loadComponent: () => import('./pages/status/status').then((m) => m.Status) },
  {
    path: '',
    canActivate: [activeGuard],
    children: [
      { path: 'home', canActivate: [roleGuard('Teacher')], loadComponent: () => import('./pages/home/home').then((m) => m.Home) },
      { path: 'profile', loadComponent: () => import('./pages/profile/profile').then((m) => m.Profile) },
      {
        path: 'admin',
        canActivate: [roleGuard('SuperAdmin')],
        children: [
          { path: 'teachers', loadComponent: () => import('./pages/admin/teachers/teachers').then((m) => m.AdminTeachers) },
          { path: 'institutes', loadComponent: () => import('./pages/admin/institutes/institutes').then((m) => m.AdminInstitutes) },
          { path: 'catalog', loadComponent: () => import('./pages/admin/catalog/catalog').then((m) => m.AdminCatalog) },
          { path: '**', redirectTo: 'teachers' },
        ],
      },
    ],
  },
  { path: '**', redirectTo: 'home' },
];
