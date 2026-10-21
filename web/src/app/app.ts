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
import { TranslateService } from './core/i18n/translate.service';
import { BrandMark } from './shared/brand-mark';
import { LanguageMenu } from './shared/language-menu';
import { UnreadNotifications } from './core/state/unread-notifications';
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
  { route: '/students', label: 'nav.students', icon: 'groups' },
  { route: '/attendance', label: 'nav.attendance', icon: 'fact_check' },
  { route: '/fees', label: 'nav.fees', icon: 'payments' },
  { route: '/exams', label: 'nav.exams', icon: 'quiz' },
  { route: '/materials', label: 'nav.materials', icon: 'folder_open' },
  { route: '/whatsapp', label: 'nav.whatsapp', icon: 'chat' },
  { route: '/timetable', label: 'nav.timetable', icon: 'calendar_month' },
  { route: '/venues', label: 'nav.venues', icon: 'place' },
  { route: '/help', label: 'nav.help', icon: 'help' },
];
/** The four screens a teacher opens most on a phone; the rest are under More. */
const TEACHER_TABS: NavItem[] = TEACHER_NAV.filter((item) => ['/home', '/classes', '/attendance', '/fees'].includes(item.route));
const ADMIN_NAV: NavItem[] = [
  { route: '/admin/dashboard', label: 'nav.dashboard', icon: 'dashboard' },
  { route: '/admin/teachers', label: 'nav.teachers', icon: 'school' },
  { route: '/admin/institutes', label: 'nav.institutes', icon: 'apartment' },
  { route: '/admin/plans', label: 'nav.plans', icon: 'workspace_premium' },
  { route: '/admin/catalog', label: 'nav.catalog', icon: 'tune' },
  { route: '/admin/audit', label: 'nav.audit', icon: 'history' },
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
    LanguageMenu,
    BrandMark,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly loading = inject(LoadingService);
  protected readonly theme = inject(ThemeService);
  protected readonly themes = THEMES;
  protected readonly unread = inject(UnreadNotifications);
  private readonly i18n = inject(TranslateService);

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

  /** Teachers and students are told things; the Super Admin has no notifications. */
  protected readonly showsNotifications = computed(() => this.menuVisible() && (this.auth.hasRole('Teacher') || this.auth.hasRole('Student')));

  /** The number on the profile icon: nothing when there is nothing new, and "9+" so it never grows wide. */
  protected readonly badge = computed(() => {
    const count = this.unread.count();
    return !this.showsNotifications() || count === 0 ? '' : count > 9 ? '9+' : String(count);
  });

  protected readonly accountLabel = computed(() => {
    const label = this.i18n.translate('nav.account');
    const count = this.unread.count();
    return this.showsNotifications() && count > 0 ? `${label}, ${count} ${this.i18n.translate('notifications.unread')}` : label;
  });

  protected readonly tabs = TEACHER_TABS;

  /** A teacher on a phone gets the tab bar at the bottom, within thumb reach; everyone else keeps the menu button. */
  protected readonly showsTabs = computed(() => this.hasNav() && !this.wide() && this.auth.hasRole('Teacher') && !this.auth.hasRole('SuperAdmin'));

  protected readonly nav = computed(() => (this.auth.hasRole('SuperAdmin') ? ADMIN_NAV : this.auth.hasRole('Teacher') ? TEACHER_NAV : []));

  /** Students have no menu yet; the side menu appears for roles that have screens to list. */
  protected readonly hasNav = computed(() => this.menuVisible() && this.nav().length > 0);

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
