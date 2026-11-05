import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule } from '@jsverse/transloco';
import { AuthService } from '../../../core/auth/auth.service';
import { BrandLogoComponent } from '../../../shared/ui/brand-logo.component';
import { LanguageSwitcherComponent } from '../../../shared/ui/language-switcher.component';

/**
 * Public landing page, first version (P1.4): hero, capabilities, how it works, security, CTA, footer.
 * P11.1 adds the product demonstration, use cases, multi-organization benefits and the illustrated scenes.
 */
@Component({
  selector: 'app-landing',
  imports: [RouterLink, MatButtonModule, MatIconModule, TranslocoModule, BrandLogoComponent, LanguageSwitcherComponent],
  templateUrl: './landing.component.html',
  styleUrl: './landing.component.scss',
})
export class LandingComponent {
  readonly auth = inject(AuthService);
  readonly year = new Date().getFullYear();
  readonly capabilities = [
    { key: 'ask', icon: 'forum' },
    { key: 'trust', icon: 'verified' },
    { key: 'secure', icon: 'shield' },
    { key: 'share', icon: 'share' },
  ];
  readonly steps = ['step1', 'step2', 'step3'];
  readonly securityPoints = ['isolation', 'permissions', 'audit'];
}
