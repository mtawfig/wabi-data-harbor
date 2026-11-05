import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { map } from 'rxjs';
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
  imports: [MatButtonModule, MatDividerModule, MatIconModule, MatListModule, MatMenuModule, MatSidenavModule, MatToolbarModule, RouterLink, RouterLinkActive, RouterOutlet, TranslocoModule, BrandLogoComponent, LanguageSwitcherComponent],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent {
  readonly auth = inject(AuthService);
  private readonly breakpoints = inject(BreakpointObserver);

  readonly sections = input.required<NavSection[]>();
  readonly contextLabelKey = input('common.organization');
  readonly accent = input<'organization' | 'platform'>('organization');

  readonly isHandset = toSignal(this.breakpoints.observe('(max-width: 960px)').pipe(map((r) => r.matches)), { initialValue: false });
  readonly opened = signal(true);

  constructor() {
    // Collapse the sidebar automatically on small screens; it then opens as an overlay from the menu button.
    effect(() => this.opened.set(!this.isHandset()));
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
