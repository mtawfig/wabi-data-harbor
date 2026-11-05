import { Component, input } from '@angular/core';

/**
 * Interim Wabi Insight mark: a lighthouse over data waves. The final logo set (light/dark, icon,
 * wordmark, favicon) is produced in P11.1; this keeps the identity consistent until then.
 */
@Component({
  selector: 'app-brand-logo',
  template: `
    <span class="logo" [class.logo--light]="variant() === 'light'">
      <svg viewBox="0 0 48 48" [attr.width]="size()" [attr.height]="size()" aria-hidden="true">
        <defs>
          <linearGradient id="wiWave" x1="0" x2="1" y1="0" y2="0">
            <stop offset="0" stop-color="#087DF2" />
            <stop offset="1" stop-color="#22B8CF" />
          </linearGradient>
        </defs>
        <path d="M20 10h8l3 22H17z" class="logo__tower" />
        <rect x="18" y="6" width="12" height="5" rx="1.5" class="logo__lamp" />
        <path d="M24 2l9 4H15z" class="logo__roof" />
        <path d="M31 9l10-4M17 9L7 5" class="logo__beam" stroke-width="2" stroke-linecap="round" />
        <path d="M4 36c5-4 10-4 15 0s10 4 15 0 10-4 10 0" fill="none" stroke="url(#wiWave)" stroke-width="3" stroke-linecap="round" />
        <path d="M4 42c5-4 10-4 15 0s10 4 15 0 10-4 10 0" fill="none" stroke="url(#wiWave)" stroke-width="3" stroke-linecap="round" opacity="0.6" />
      </svg>
      @if (showName()) {
        <span class="logo__name">Wabi Insight</span>
      }
    </span>
  `,
  styles: `
    .logo { display: inline-flex; align-items: center; gap: 10px; color: var(--wi-navy); }
    .logo--light { color: var(--wi-white); }
    .logo__tower, .logo__roof { fill: currentColor; }
    .logo__lamp { fill: #22b8cf; }
    .logo__beam { stroke: #22b8cf; }
    .logo__name { font-weight: 700; font-size: 18px; letter-spacing: -0.01em; white-space: nowrap; }
  `,
})
export class BrandLogoComponent {
  readonly size = input(36);
  readonly showName = input(true);
  readonly variant = input<'dark' | 'light'>('dark');
}
