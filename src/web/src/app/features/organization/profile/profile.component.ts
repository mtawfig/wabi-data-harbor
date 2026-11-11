import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { AuthService } from '../../../core/auth/auth.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { MyRole, OrganizationProfile } from '../../../core/organizations/organizations.models';
import { OrganizationProfileService } from '../../../core/organizations/organizations.service';
import { LanguageSwitcherComponent } from '../../../shared/ui/language-switcher.component';

/**
 * Profile (P11.2): the signed-in user's account as the session knows it — name, email, organization, roles and
 * language — with the password change action. Name, email and roles are managed by the organization administrator.
 */
@Component({
  selector: 'app-profile',
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoModule, LanguageSwitcherComponent],
  template: `
    <section class="wi-page profile">
      <header class="wi-page__header">
        <div>
          <p class="wi-eyebrow">{{ 'common.organization' | transloco }}</p>
          <h1>{{ 'profile.title' | transloco }}</h1>
          <p class="wi-muted">{{ 'profile.subtitle' | transloco }}</p>
        </div>
        <a mat-flat-button routerLink="/change-password" data-testid="profile-change-password"><mat-icon>password</mat-icon>{{ 'common.changePassword' | transloco }}</a>
      </header>
      @if (auth.session(); as s) {
        <div class="profile__grid">
          <div class="wi-card profile__card" data-testid="profile-card">
            <div class="profile__avatar" aria-hidden="true">{{ initials(s.displayName) }}</div>
            <h2>{{ s.displayName }}</h2>
            <p class="wi-muted wi-ltr">{{ s.email }}</p>
            <dl>
              <dt>{{ 'profile.organization' | transloco }}</dt><dd>{{ organizationName() || '—' }}</dd>
              <dt>{{ 'profile.roles' | transloco }}</dt><dd><span class="chips">@for (r of roleNames(s.roles); track r) { <span class="chip">{{ r }}</span> } @empty { <span class="wi-muted">—</span> }</span></dd>
              <dt>{{ 'profile.accountType' | transloco }}</dt><dd>{{ 'profile.types.' + s.principalType | transloco }}</dd>
              <dt>{{ 'profile.preferredLanguage' | transloco }}</dt><dd>{{ (s.preferredLanguage === 'ar' ? 'common.arabic' : 'common.english') | transloco }}</dd>
            </dl>
          </div>
          <div class="profile__side">
            <div class="wi-card">
              <h3>{{ 'profile.language.title' | transloco }}</h3>
              <p class="wi-muted">{{ 'profile.language.text' | transloco }}</p>
              <app-language-switcher />
            </div>
            <div class="wi-card">
              <h3>{{ 'profile.security.title' | transloco }}</h3>
              <p class="wi-muted">{{ 'profile.security.text' | transloco }}</p>
              <a mat-stroked-button routerLink="/change-password"><mat-icon>password</mat-icon>{{ 'common.changePassword' | transloco }}</a>
            </div>
            <div class="wi-card">
              <h3>{{ 'profile.managed.title' | transloco }}</h3>
              <p class="wi-muted">{{ 'profile.managed.text' | transloco }}</p>
              <a mat-stroked-button routerLink="/app/support/new"><mat-icon>support_agent</mat-icon>{{ 'support.new' | transloco }}</a>
            </div>
          </div>
        </div>
      }
    </section>
  `,
  styles: `
    .profile__grid { display: grid; grid-template-columns: minmax(0, 1.2fr) minmax(0, 1fr); gap: 20px; align-items: start; }
    .profile__card { h2 { margin: 12px 0 2px; } dl { display: grid; grid-template-columns: max-content 1fr; gap: 10px 20px; margin: 20px 0 0; } dt { color: var(--wi-slate); font-size: 13px; } dd { margin: 0; font-weight: 500; } }
    .profile__avatar { width: 64px; height: 64px; border-radius: 50%; display: grid; place-items: center; font-weight: 700; font-size: 22px; color: var(--wi-white); background: linear-gradient(160deg, var(--wi-navy), var(--wi-ocean)); }
    .profile__side { display: grid; gap: 16px; h3 { margin: 0 0 6px; } p { font-size: 14px; } }
    .chips { display: inline-flex; flex-wrap: wrap; gap: 6px; }
    .chip { display: inline-block; padding: 2px 10px; border-radius: 999px; background: #eef4fb; color: var(--wi-ocean); font-size: 12px; font-weight: 600; }
    @media (max-width: 960px) { .profile__grid { grid-template-columns: 1fr; } }
  `,
})
export class ProfileComponent {
  readonly auth = inject(AuthService);
  readonly language = inject(LanguageService);
  private readonly profiles = inject(OrganizationProfileService);
  readonly profile = signal<OrganizationProfile | null>(null);
  /** The user's roles by name in the interface language (F2); null until loaded, when the codes are shown. */
  readonly roles = signal<MyRole[] | null>(null);

  constructor() {
    this.profiles.get().subscribe({ next: (p) => this.profile.set(p), error: () => this.profile.set(null) });
    this.profiles.myRoles().subscribe({ next: (r) => this.roles.set(r), error: () => this.roles.set(null) });
  }

  roleNames(codes: string[]): string[] {
    const roles = this.roles();
    if (!roles) {
      return codes;
    }
    return roles.map((r) => (this.language.current() === 'ar' ? r.nameAr || r.nameEn : r.nameEn || r.nameAr));
  }

  organizationName(): string | null {
    const p = this.profile();
    return p ? (this.language.current() === 'ar' ? p.organizationNameAr : p.organizationName) : null;
  }

  initials(name: string): string {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]?.toUpperCase() ?? '').join('');
  }
}
