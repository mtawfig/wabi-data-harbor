import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { BootstrapStatus } from '../../public/bootstrap/bootstrap.component';

/** Specification §7.3: bootstrap completion, date, administrator email, schema version and status. */
@Component({
  selector: 'app-bootstrap-status',
  imports: [DatePipe, MatIconModule, TranslocoModule],
  template: `
    <div class="wi-card">
      <h3>{{ 'platform.settings.bootstrapSection' | transloco }}</h3>
      @if (status(); as s) {
        <dl class="status">
          <dt>{{ 'platform.settings.status' | transloco }}</dt>
          <dd>
            <mat-icon [class.ok]="s.bootstrapCompleted">{{ s.bootstrapCompleted ? 'check_circle' : 'radio_button_unchecked' }}</mat-icon>
            {{ (s.bootstrapCompleted ? 'platform.settings.completed' : 'platform.settings.notCompleted') | transloco }}
          </dd>
          <dt>{{ 'bootstrap.completedAt' | transloco }}</dt><dd>{{ s.bootstrapCompletedAt | date: 'medium' }}</dd>
          <dt>{{ 'bootstrap.administrator' | transloco }}</dt><dd><span class="wi-ltr">{{ s.bootstrapAdministratorEmail }}</span></dd>
          <dt>{{ 'bootstrap.schema' | transloco }}</dt><dd><span class="wi-ltr">{{ s.schemaVersion }}</span></dd>
          <dt>{{ 'bootstrap.pendingMigrations' | transloco }}</dt><dd>{{ (s.pendingMigrations ? 'bootstrap.yes' : 'bootstrap.no') | transloco }}</dd>
        </dl>
      } @else {
        <p class="wi-muted">{{ 'common.loading' | transloco }}</p>
      }
    </div>
  `,
  styles: `
    .status { display: grid; grid-template-columns: max-content 1fr; gap: 10px 24px; margin: 0; dt { color: var(--wi-slate); } dd { margin: 0; display: flex; align-items: center; gap: 6px; } }
    .ok { color: var(--wi-success); }
  `,
})
export class BootstrapStatusComponent {
  private readonly http = inject(HttpClient);
  readonly status = signal<BootstrapStatus | null>(null);

  constructor() {
    void firstValueFrom(this.http.get<BootstrapStatus>('/api/bootstrap/status')).then((s) => this.status.set(s)).catch(() => this.status.set(null));
  }
}
