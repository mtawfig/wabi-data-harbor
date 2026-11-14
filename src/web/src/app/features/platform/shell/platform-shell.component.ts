import { Component } from '@angular/core';
import { AppShellComponent, NavSection } from '../../../shared/layout/app-shell.component';

const PLATFORM_NAV: NavSection[] = [
  {
    items: [
      { labelKey: 'nav.platform.overview', icon: 'space_dashboard', link: '/platform/overview' },
      { labelKey: 'nav.platform.organizations', icon: 'corporate_fare', link: '/platform/organizations' },
      { labelKey: 'nav.platform.administrators', icon: 'manage_accounts', link: '/platform/administrators' },
      { labelKey: 'nav.platform.health', icon: 'monitor_heart', link: '/platform/health' },
      { labelKey: 'nav.platform.support', icon: 'support_agent', link: '/platform/support' },
      { labelKey: 'nav.platform.supportAccess', icon: 'key', link: '/platform/support-access' },
      { labelKey: 'nav.platform.roleTemplates', icon: 'rule', link: '/platform/role-templates' },
      { labelKey: 'nav.platform.plans', icon: 'workspace_premium', link: '/platform/plans' },
      { labelKey: 'nav.platform.insights', icon: 'query_stats', link: '/platform/insights' },
      { labelKey: 'nav.platform.aiProviders', icon: 'smart_toy', link: '/platform/ai-providers' },
      { labelKey: 'nav.platform.settings', icon: 'settings', link: '/platform/settings' },
      { labelKey: 'nav.platform.audit', icon: 'policy', link: '/platform/audit' },
    ],
  },
];

@Component({
  selector: 'app-platform-shell',
  imports: [AppShellComponent],
  template: '<app-shell [sections]="sections" contextLabelKey="common.platform" accent="platform" />',
})
export class PlatformShellComponent {
  readonly sections = PLATFORM_NAV;
}
