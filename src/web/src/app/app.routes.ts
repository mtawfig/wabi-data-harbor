import { Routes } from '@angular/router';
import { authGuard, guestGuard, organizationAdminGuard, organizationUserGuard, platformAdministratorGuard } from './core/auth/auth.guards';
import { PagePlaceholderComponent } from './shared/ui/page-placeholder.component';

function placeholder(path: string, titleKey: string, phase: string, extra: Partial<Routes[number]> = {}): Routes[number] {
  return { path, component: PagePlaceholderComponent, data: { titleKey, phase }, ...extra };
}

export const routes: Routes = [
  { path: '', loadComponent: () => import('./features/public/landing/landing.component').then((m) => m.LandingComponent) },
  { path: 'login', canActivate: [guestGuard], loadComponent: () => import('./features/public/login/login.component').then((m) => m.LoginComponent) },
  { path: 'bootstrap', loadComponent: () => import('./features/public/bootstrap/bootstrap.component').then((m) => m.BootstrapComponent) },
  { path: 'change-password', canActivate: [authGuard], loadComponent: () => import('./features/auth/change-password.component').then((m) => m.ChangePasswordComponent) },
  {
    path: 'app',
    canActivate: [authGuard, organizationUserGuard],
    loadComponent: () => import('./features/organization/shell/organization-shell.component').then((m) => m.OrganizationShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'ask' },
      { path: 'dashboard', loadComponent: () => import('./features/organization/dashboard/user-dashboard.component').then((m) => m.UserDashboardComponent) },
      { path: 'ask', loadComponent: () => import('./features/organization/ask/ask.component').then((m) => m.AskComponent) },
      placeholder('history', 'nav.org.history', '9'),
      placeholder('reports', 'nav.org.reports', '9'),
      placeholder('favorites', 'nav.org.favorites', '9'),
      placeholder('shared-with-me', 'nav.org.sharedWithMe', '10'),
      placeholder('shared-by-me', 'nav.org.sharedByMe', '10'),
      placeholder('support', 'nav.org.support', '4'),
      placeholder('notifications', 'nav.org.notifications', '4'),
      placeholder('profile', 'nav.org.profile', '2'),
      {
        path: 'admin',
        canActivate: [organizationAdminGuard],
        children: [
          placeholder('dashboard', 'nav.org.admin.dashboard', '7'),
          placeholder('users', 'nav.org.admin.users', '2'),
          placeholder('departments', 'nav.org.admin.departments', '2'),
          placeholder('sections', 'nav.org.admin.sections', '2'),
          placeholder('roles', 'nav.org.admin.roles', '2'),
          placeholder('applications', 'nav.org.admin.applications', '6'),
          placeholder('metadata', 'nav.org.admin.metadata', '6'),
          placeholder('permissions', 'nav.org.admin.permissions', '6'),
          placeholder('insights', 'nav.org.admin.insights', '7'),
          placeholder('integrations', 'nav.org.admin.integrations', '10'),
          placeholder('settings', 'nav.org.admin.settings', '2'),
        ],
      },
    ],
  },
  {
    path: 'platform',
    canActivate: [authGuard, platformAdministratorGuard],
    loadComponent: () => import('./features/platform/shell/platform-shell.component').then((m) => m.PlatformShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      { path: 'overview', loadComponent: () => import('./features/platform/settings/platform-overview.component').then((m) => m.PlatformOverviewComponent) },
      { path: 'settings', loadComponent: () => import('./features/platform/settings/general-settings.component').then((m) => m.GeneralSettingsComponent) },
      placeholder('organizations', 'nav.platform.organizations', '2'),
      placeholder('administrators', 'nav.platform.administrators', '3'),
      placeholder('health', 'nav.platform.health', '3'),
      placeholder('support', 'nav.platform.support', '4'),
      placeholder('support-access', 'nav.platform.supportAccess', '5'),
      placeholder('role-templates', 'nav.platform.roleTemplates', '2'),
      placeholder('insights', 'nav.platform.insights', '7'),
      placeholder('audit', 'nav.platform.audit', '3'),
    ],
  },
  { path: '**', redirectTo: '' },
];
