import { Component, computed, inject, signal } from '@angular/core';
import { LanguageService } from '../../../core/i18n/language.service';
import { OrganizationProfile } from '../../../core/organizations/organizations.models';
import { OrganizationProfileService } from '../../../core/organizations/organizations.service';
import { AuthService } from '../../../core/auth/auth.service';
import { AppShellComponent, NavSection } from '../../../shared/layout/app-shell.component';

const USER_NAV: NavSection = {
  items: [
    { labelKey: 'nav.org.dashboard', icon: 'dashboard', link: '/app/dashboard' },
    { labelKey: 'nav.org.ask', icon: 'auto_awesome', link: '/app/ask' },
    { labelKey: 'nav.org.history', icon: 'history', link: '/app/history' },
    { labelKey: 'nav.org.reports', icon: 'description', link: '/app/reports' },
    { labelKey: 'nav.org.favorites', icon: 'star', link: '/app/favorites' },
    { labelKey: 'nav.org.sharedWithMe', icon: 'inbox', link: '/app/shared-with-me' },
    { labelKey: 'nav.org.sharedByMe', icon: 'outbox', link: '/app/shared-by-me' },
    { labelKey: 'nav.org.support', icon: 'support_agent', link: '/app/support' },
    { labelKey: 'nav.org.notifications', icon: 'notifications', link: '/app/notifications' },
    { labelKey: 'nav.org.profile', icon: 'person', link: '/app/profile' },
  ],
};

const ADMIN_NAV: NavSection = {
  titleKey: 'nav.org.admin.title',
  items: [
    { labelKey: 'nav.org.admin.dashboard', icon: 'insights', link: '/app/admin/dashboard' },
    { labelKey: 'nav.org.admin.users', icon: 'group', link: '/app/admin/users' },
    { labelKey: 'nav.org.admin.departments', icon: 'account_tree', link: '/app/admin/departments' },
    { labelKey: 'nav.org.admin.sections', icon: 'segment', link: '/app/admin/sections' },
    { labelKey: 'nav.org.admin.roles', icon: 'admin_panel_settings', link: '/app/admin/roles' },
    { labelKey: 'nav.org.admin.applications', icon: 'storage', link: '/app/admin/applications' },
    { labelKey: 'nav.org.admin.metadata', icon: 'schema', link: '/app/admin/metadata' },
    { labelKey: 'nav.org.admin.permissions', icon: 'lock', link: '/app/admin/permissions' },
    { labelKey: 'nav.org.admin.insights', icon: 'query_stats', link: '/app/admin/insights' },
    { labelKey: 'nav.org.admin.integrations', icon: 'hub', link: '/app/admin/integrations' },
    { labelKey: 'nav.org.admin.settings', icon: 'settings', link: '/app/admin/settings' },
  ],
};

@Component({
  selector: 'app-organization-shell',
  imports: [AppShellComponent],
  template: '<app-shell [sections]="sections()" contextLabelKey="common.organization" [contextText]="organizationName()" accent="organization" />',
})
export class OrganizationShellComponent {
  private readonly auth = inject(AuthService);
  private readonly language = inject(LanguageService);
  private readonly profiles = inject(OrganizationProfileService);

  readonly profile = signal<OrganizationProfile | null>(null);
  readonly sections = computed<NavSection[]>(() => (this.auth.isOrganizationAdmin() ? [USER_NAV, ADMIN_NAV] : [USER_NAV]));
  readonly organizationName = computed(() => {
    const p = this.profile();
    return p ? (this.language.current() === 'ar' ? p.organizationNameAr : p.organizationName) : null;
  });

  constructor() {
    this.profiles.get().subscribe({ next: (p) => this.profile.set(p), error: () => this.profile.set(null) });
  }
}
