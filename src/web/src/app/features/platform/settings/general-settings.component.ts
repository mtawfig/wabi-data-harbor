import { Component } from '@angular/core';
import { TranslocoModule } from '@jsverse/transloco';
import { BootstrapStatusComponent } from './bootstrap-status.component';

@Component({
  selector: 'app-general-settings',
  imports: [TranslocoModule, BootstrapStatusComponent],
  template: `
    <section class="wi-page">
      <header class="wi-page__header">
        <div>
          <p class="wi-eyebrow">{{ 'common.platform' | transloco }}</p>
          <h1>{{ 'platform.settings.title' | transloco }}</h1>
        </div>
      </header>
      <app-bootstrap-status />
    </section>
  `,
})
export class GeneralSettingsComponent {}
