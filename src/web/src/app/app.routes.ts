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
      { path: 'support', loadComponent: () => import('./features/support/ticket-list.component').then((m) => m.TicketListComponent), data: { area: 'app' } },
      { path: 'support/new', loadComponent: () => import('./features/support/ticket-form.component').then((m) => m.TicketFormComponent) },
      { path: 'support/:id', loadComponent: () => import('./features/support/ticket-detail.component').then((m) => m.TicketDetailComponent), data: { area: 'app' } },
      { path: 'notifications', loadComponent: () => import('./features/support/notification-center.component').then((m) => m.NotificationCenterComponent), data: { area: 'app' } },
      placeholder('profile', 'nav.org.profile', '2'),
      {
        path: 'admin',
        canActivate: [organizationAdminGuard],
        children: [
          placeholder('dashboard', 'nav.org.admin.dashboard', '7'),
          { path: 'users', loadComponent: () => import('./features/organization/admin/users-list.component').then((m) => m.UsersListComponent) },
          { path: 'users/new', loadComponent: () => import('./features/organization/admin/user-form.component').then((m) => m.UserFormComponent) },
          { path: 'users/:id', loadComponent: () => import('./features/organization/admin/user-form.component').then((m) => m.UserFormComponent) },
          { path: 'departments', loadComponent: () => import('./features/organization/admin/departments.component').then((m) => m.DepartmentsComponent) },
          { path: 'sections', loadComponent: () => import('./features/organization/admin/sections.component').then((m) => m.SectionsComponent) },
          { path: 'roles', loadComponent: () => import('./features/organization/admin/roles.component').then((m) => m.RolesComponent) },
          placeholder('applications', 'nav.org.admin.applications', '6'),
          placeholder('metadata', 'nav.org.admin.metadata', '6'),
          placeholder('permissions', 'nav.org.admin.permissions', '6'),
          placeholder('insights', 'nav.org.admin.insights', '7'),
          placeholder('integrations', 'nav.org.admin.integrations', '10'),
          { path: 'settings', loadComponent: () => import('./features/organization/settings/organization-settings.component').then((m) => m.OrganizationSettingsComponent) },
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
      { path: 'overview', loadComponent: () => import('./features/platform/dashboard/platform-dashboard.component').then((m) => m.PlatformDashboardComponent) },
      { path: 'settings', loadComponent: () => import('./features/platform/settings/general-settings.component').then((m) => m.GeneralSettingsComponent) },
      { path: 'organizations', loadComponent: () => import('./features/platform/organizations/organizations-list.component').then((m) => m.OrganizationsListComponent) },
      { path: 'organizations/new', loadComponent: () => import('./features/platform/organizations/organization-create.component').then((m) => m.OrganizationCreateComponent) },
      { path: 'organizations/:id', loadComponent: () => import('./features/platform/organizations/organization-detail.component').then((m) => m.OrganizationDetailComponent) },
      { path: 'administrators', loadComponent: () => import('./features/platform/administrators/platform-administrators.component').then((m) => m.PlatformAdministratorsComponent) },
      { path: 'health', loadComponent: () => import('./features/platform/health/system-health.component').then((m) => m.SystemHealthComponent) },
      { path: 'support', loadComponent: () => import('./features/support/ticket-list.component').then((m) => m.TicketListComponent), data: { area: 'platform' } },
      { path: 'support/:id', loadComponent: () => import('./features/support/ticket-detail.component').then((m) => m.TicketDetailComponent), data: { area: 'platform' } },
      { path: 'notifications', loadComponent: () => import('./features/support/notification-center.component').then((m) => m.NotificationCenterComponent), data: { area: 'platform' } },
      placeholder('support-access', 'nav.platform.supportAccess', '5'),
      { path: 'role-templates', loadComponent: () => import('./features/platform/role-templates/role-templates.component').then((m) => m.RoleTemplatesComponent) },
      placeholder('insights', 'nav.platform.insights', '7'),
      { path: 'audit', loadComponent: () => import('./features/platform/audit/audit-logs.component').then((m) => m.AuditLogsComponent) },
    ],
  },
  { path: '**', redirectTo: '' },
];
