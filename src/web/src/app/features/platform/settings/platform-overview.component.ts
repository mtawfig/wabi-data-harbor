import { Component, inject } from '@angular/core';
import { TranslocoModule } from '@jsverse/transloco';
import { AuthService } from '../../../core/auth/auth.service';
import { BootstrapStatusComponent } from './bootstrap-status.component';

/** Platform Overview placeholder with real initialization status; KPI cards, charts and health arrive in P3.2. */
@Component({
  selector: 'app-platform-overview',
  imports: [TranslocoModule, BootstrapStatusComponent],
  template: `
    <section class="wi-page">
      <header class="wi-page__header">
        <div>
          <p class="wi-eyebrow">{{ 'common.platform' | transloco }}</p>
          <h1>{{ 'platform.overview.title' | transloco }}</h1>
          <p class="wi-muted">{{ 'platform.overview.subtitle' | transloco }}</p>
        </div>
      </header>
      <app-bootstrap-status />
    </section>
  `,
})
export class PlatformOverviewComponent {
  readonly auth = inject(AuthService);
}
