import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { map, startWith, switchMap, timer } from 'rxjs';
import { NotificationsService } from '../../core/notifications/notifications.service';
import { SupportAccessService } from '../../core/support/support-access.service';
import { SupportModeBannerComponent } from '../../features/support/support-mode-banner.component';
import { AuthService } from '../../core/auth/auth.service';
import { BrandLogoComponent } from '../ui/brand-logo.component';
import { LanguageSwitcherComponent } from '../ui/language-switcher.component';

export interface NavItem {
  labelKey: string;
  icon: string;
  link: string;
}

export interface NavSection {
  titleKey?: string;
  items: NavItem[];
}

/**
 * Shared authenticated shell: 76 px header, 248 px sidebar (collapsible on small screens), user menu.
 * The organization and platform shells differ only in navigation and accent.
 */
@Component({
  selector: 'app-shell',
  imports: [MatBadgeModule, MatButtonModule, MatDividerModule, MatIconModule, MatListModule, MatMenuModule, MatSidenavModule, MatToolbarModule, RouterLink, RouterLinkActive, RouterOutlet, TranslocoModule, BrandLogoComponent, LanguageSwitcherComponent, SupportModeBannerComponent],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent {
  readonly auth = inject(AuthService);
  readonly notifications = inject(NotificationsService);
  readonly supportAccess = inject(SupportAccessService);
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly destroyRef = inject(DestroyRef);

  readonly sections = input.required<NavSection[]>();
  readonly contextLabelKey = input('common.organization');
  /** Overrides the translated context label with a concrete name (e.g. the organization's name). */
  readonly contextText = input<string | null>(null);
  readonly accent = input<'organization' | 'platform'>('organization');

  readonly isHandset = toSignal(this.breakpoints.observe('(max-width: 960px)').pipe(map((r) => r.matches)), { initialValue: false });
  readonly opened = signal(true);

  constructor() {
    // Collapse the sidebar automatically on small screens; it then opens as an overlay from the menu button.
    effect(() => this.opened.set(!this.isHandset()));
    // Unread badge: refreshed on load and every minute while the shell is open.
    timer(0, 60_000)
      .pipe(startWith(0), switchMap(() => this.notifications.refreshUnread()), takeUntilDestroyed(this.destroyRef))
      .subscribe({ error: () => undefined });
    // Support-mode session (platform shell only): the banner must reflect expiry and revocation without a reload.
    timer(0, 60_000)
      .pipe(switchMap(() => (this.accent() === 'platform' ? this.supportAccess.refreshSession() : [])), takeUntilDestroyed(this.destroyRef))
      .subscribe({ error: () => undefined });
  }

  notificationsLink(): string {
    return this.accent() === 'platform' ? '/platform/notifications' : '/app/notifications';
  }

  toggle(): void {
    this.opened.set(!this.opened());
  }

  closeOnHandset(): void {
    if (this.isHandset()) {
      this.opened.set(false);
    }
  }
}
