import { Component, computed, inject, input } from '@angular/core';

import { LanguageService } from '../../core/i18n/language.service';

/**
 * Wabi Insight brand mark (P11.1, specification §21): a lighthouse over data waves with connectivity marks.
 * The same drawing ships as static files under `public/brand/` (icon, light icon, dark and light wordmarks,
 * favicon); keep both in sync. `variant` 'dark' is for light backgrounds, 'light' for the navy/ocean gradient.
 *
 * The name and tagline follow the interface language: in Arabic the mark sits on the right with the Arabic name and
 * tagline on its left (the page direction does the mirroring), as in `wabi-insight-wordmark-{dark,light}_ar.svg`.
 */
@Component({
  selector: 'app-brand-logo',
  template: `
    <span class="logo" [class.logo--light]="variant() === 'light'" [class.logo--ar]="arabic()" [attr.lang]="arabic() ? 'ar' : 'en'">
      <svg viewBox="0 0 64 64" [attr.width]="size()" [attr.height]="size()" aria-hidden="true" focusable="false">
        <defs>
          <linearGradient id="wiWave" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#087DF2" /><stop offset="1" stop-color="#22B8CF" /></linearGradient>
          <linearGradient id="wiBeamR" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#22B8CF" stop-opacity=".85" /><stop offset="1" stop-color="#22B8CF" stop-opacity="0" /></linearGradient>
          <linearGradient id="wiBeamL" x1="1" y1="0" x2="0" y2="0"><stop offset="0" stop-color="#22B8CF" stop-opacity=".85" /><stop offset="1" stop-color="#22B8CF" stop-opacity="0" /></linearGradient>
        </defs>
        <path d="M38 13 L62 6 L62 20 Z" fill="url(#wiBeamR)" />
        <path d="M26 13 L2 6 L2 20 Z" fill="url(#wiBeamL)" />
        <g class="logo__node"><circle cx="56" cy="9" r="2.2" /><circle cx="60.5" cy="17" r="1.6" /><circle cx="8" cy="9" r="2.2" /><circle cx="3.5" cy="17" r="1.6" /></g>
        <g class="logo__link" stroke-width="1.2" stroke-linecap="round"><path d="M56 9 L60.5 17" /><path d="M8 9 L3.5 17" /><path d="M38.5 14 L56 9" /><path d="M25.5 14 L8 9" /></g>
        <path d="M27 18 h10 l3 26 H24 Z" class="logo__tower" />
        <rect x="24.6" y="27" width="14.8" height="2.4" class="logo__band" />
        <rect x="23.2" y="35.5" width="17.6" height="2.4" class="logo__band" />
        <rect x="26" y="11" width="12" height="7" rx="1.5" class="logo__tower" />
        <rect x="28" y="12.6" width="8" height="3.8" rx="1" class="logo__lamp" />
        <path d="M32 4 L40 11 H24 Z" class="logo__tower" />
        <circle cx="32" cy="3.2" r="1.5" class="logo__lamp" />
        <path d="M4 48c5-4 9-4 14 0s9 4 14 0 9-4 14 0 9 4 14 0" fill="none" stroke="url(#wiWave)" stroke-width="3.2" stroke-linecap="round" />
        <path d="M4 55c5-4 9-4 14 0s9 4 14 0 9-4 14 0 9 4 14 0" fill="none" stroke="url(#wiWave)" stroke-width="3.2" stroke-linecap="round" opacity=".62" />
        <path d="M4 62c5-4 9-4 14 0s9 4 14 0 9-4 14 0 9 4 14 0" fill="none" stroke="url(#wiWave)" stroke-width="3.2" stroke-linecap="round" opacity=".32" />
      </svg>
      @if (showName()) {
        <span class="logo__text"><span class="logo__name" data-testid="brand-name">{{ name() }}</span>@if (showTagline()) { <span class="logo__tagline" data-testid="brand-tagline">{{ tagline() }}</span> }</span>
      }
    </span>
  `,
  styles: `
    .logo { display: inline-flex; align-items: center; gap: 10px; color: var(--wi-navy); }
    .logo--light { color: var(--wi-white); }
    .logo__tower { fill: currentColor; }
    .logo__band { fill: #087df2; }
    .logo--light .logo__band { fill: #22b8cf; }
    .logo__lamp, .logo__node { fill: #22b8cf; }
    .logo__link { stroke: #22b8cf; opacity: .85; }
    .logo__text { display: inline-flex; flex-direction: column; line-height: 1.1; }
    .logo__name { font-weight: 700; font-size: 18px; letter-spacing: -0.01em; white-space: nowrap; }
    .logo__tagline { font-size: 10px; font-weight: 500; letter-spacing: .06em; text-transform: uppercase; color: #087df2; white-space: nowrap; }
    .logo--light .logo__tagline { color: #22b8cf; }
    .logo--ar .logo__text { line-height: 1.25; }
    .logo--ar .logo__name { letter-spacing: 0; }
    .logo--ar .logo__tagline { font-size: 11px; letter-spacing: 0; text-transform: none; }
  `,
})
export class BrandLogoComponent {
  readonly size = input(36);
  readonly showName = input(true);
  readonly showTagline = input(false);
  readonly variant = input<'dark' | 'light'>('dark');

  private readonly language = inject(LanguageService);
  protected readonly arabic = computed(() => this.language.current() === 'ar');
  protected readonly name = computed(() => (this.arabic() ? 'وابي تكس انسايت' : 'Wabi Insight'));
  protected readonly tagline = computed(() => (this.arabic() ? 'اسأل بياناتك، وثق بالإجابة' : 'Ask Your Data. Trust the Answer.'));
}
