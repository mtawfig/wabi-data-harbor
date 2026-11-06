import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule } from '@jsverse/transloco';
import { AuthService } from '../../../core/auth/auth.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { BrandLogoComponent } from '../../../shared/ui/brand-logo.component';
import { LanguageSwitcherComponent } from '../../../shared/ui/language-switcher.component';

/**
 * Public landing page (P11.1, specification §21): header, hero with the two illustrated scenes, capabilities,
 * product demonstration (fictional labelled data), use cases, how it works, security, multi-organization
 * benefits, call to action and footer. Everything is translated; the demo numbers are static and fictional.
 */
@Component({
  selector: 'app-landing',
  imports: [RouterLink, MatButtonModule, MatIconModule, TranslocoModule, BrandLogoComponent, LanguageSwitcherComponent],
  templateUrl: './landing.component.html',
  styleUrl: './landing.component.scss',
})
export class LandingComponent {
  readonly auth = inject(AuthService);
  readonly language = inject(LanguageService);
  readonly year = new Date().getFullYear();
  readonly nav = ['capabilities', 'demo', 'useCases', 'how', 'security', 'organizations'];
  readonly capabilities = [
    { key: 'ask', icon: 'forum' },
    { key: 'trust', icon: 'verified' },
    { key: 'dashboards', icon: 'dashboard' },
    { key: 'reports', icon: 'picture_as_pdf' },
    { key: 'secure', icon: 'shield' },
    { key: 'share', icon: 'share' },
  ];
  /** Fictional demonstration data (labelled as such in the UI). */
  readonly demo = {
    kpis: [
      { key: 'onTime', value: '94.2%', delta: '+1.8', up: true },
      { key: 'shipments', value: '12,480', delta: '+6.4', up: true },
      { key: 'delay', value: '18 min', delta: '-3.5', up: false },
    ],
    bars: [
      { key: 'north', value: 96 },
      { key: 'coast', value: 91 },
      { key: 'central', value: 88 },
      { key: 'east', value: 97 },
      { key: 'west', value: 84 },
    ],
  };
  readonly useCases = [
    { key: 'finance', icon: 'account_balance' },
    { key: 'operations', icon: 'precision_manufacturing' },
    { key: 'hr', icon: 'groups' },
    { key: 'sales', icon: 'trending_up' },
    { key: 'executive', icon: 'insights' },
    { key: 'support', icon: 'support_agent' },
  ];
  readonly steps = ['step1', 'step2', 'step3', 'step4'];
  readonly securityPoints = ['isolation', 'permissions', 'plan', 'secrets', 'audit', 'support'];
  readonly organizationBenefits = [
    { key: 'isolation', icon: 'domain' },
    { key: 'branding', icon: 'palette' },
    { key: 'support', icon: 'verified_user' },
    { key: 'operations', icon: 'monitor_heart' },
  ];
}
