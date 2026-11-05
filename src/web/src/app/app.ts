import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { PlatformInfo } from './core/platform/platform-info';
import { PlatformInfoService } from './core/platform/platform-info.service';

/**
 * Temporary application shell for P1.1. It proves the Angular app builds, calls the API
 * and renders. The real design system, layouts, routing and i18n arrive in P1.4.
 */
@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly platformInfoService = inject(PlatformInfoService);

  protected readonly productName = signal('Wabi Insight');
  protected readonly tagline = signal('Ask Your Data. Trust the Answer.');
  protected readonly info = signal<PlatformInfo | null>(null);
  protected readonly apiStatus = signal<'checking' | 'online' | 'offline'>('checking');

  constructor() {
    this.platformInfoService.getInfo().subscribe({
      next: (info) => {
        this.info.set(info);
        this.productName.set(info.productName);
        this.tagline.set(info.tagline);
        this.apiStatus.set('online');
      },
      error: () => this.apiStatus.set('offline'),
    });
  }
}
