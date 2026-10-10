import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, computed, inject, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSidenav, MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { filter, map } from 'rxjs';
import { AuthService, needsSetup } from './core/auth/auth.service';
import { TranslatePipe } from './core/i18n/translate.pipe';
import { LoadingService } from './core/ui/loading.service';
import { ThemePreference, ThemeService } from './core/ui/theme.service';

interface NavItem {
  route: string;
  label: string;
  icon: string;
}

const TEACHER_NAV: NavItem[] = [
  { route: '/home', label: 'nav.home', icon: 'home' },
  { route: '/classes', label: 'nav.classes', icon: 'menu_book' },
  { route: '/timetable', label: 'nav.timetable', icon: 'calendar_month' },
  { route: '/venues', label: 'nav.venues', icon: 'place' },
];
const ADMIN_NAV: NavItem[] = [
  { route: '/admin/teachers', label: 'nav.teachers', icon: 'school' },
  { route: '/admin/institutes', label: 'nav.institutes', icon: 'apartment' },
  { route: '/admin/catalog', label: 'nav.catalog', icon: 'tune' },
];

const THEMES: { value: ThemePreference; label: string; icon: string }[] = [
  { value: 'system', label: 'theme.system', icon: 'brightness_auto' },
  { value: 'light', label: 'theme.light', icon: 'light_mode' },
  { value: 'dark', label: 'theme.dark', icon: 'dark_mode' },
];

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatButtonModule,
    MatDividerModule,
    MatIconModule,
    MatListModule,
    MatMenuModule,
    MatProgressBarModule,
    MatSidenavModule,
    MatToolbarModule,
    TranslatePipe,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly loading = inject(LoadingService);
  protected readonly theme = inject(ThemeService);
  protected readonly themes = THEMES;

  private readonly sidenav = viewChild(MatSidenav);

  /** Wide screens keep the menu open beside the page; phones slide it over the page. */
  protected readonly wide = toSignal(
    inject(BreakpointObserver)
      .observe('(min-width: 960px)')
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  protected readonly menuVisible = computed(() => {
    const user = this.auth.user();
    return !!user && !needsSetup(user);
  });

  protected readonly nav = computed(() => (this.auth.hasRole('SuperAdmin') ? ADMIN_NAV : TEACHER_NAV));

  protected readonly currentTheme = computed(
    () => THEMES.find((t) => t.value === this.theme.preference()) ?? THEMES[0],
  );

  constructor() {
    // On a phone, close the menu after the person picks a screen.
    inject(Router)
      .events.pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe(() => {
        if (!this.wide()) void this.sidenav()?.close();
      });
  }

  protected toggleMenu(): void {
    void this.sidenav()?.toggle();
  }

  protected logout(): void {
    void this.auth.logout();
  }
}
