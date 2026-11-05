import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { ProblemDetails } from '../../../core/auth/session.model';
import { BrandLogoComponent } from '../../../shared/ui/brand-logo.component';
import { LanguageSwitcherComponent } from '../../../shared/ui/language-switcher.component';

export interface BootstrapStatus {
  bootstrapCompleted: boolean;
  bootstrapCompletedAt?: string;
  bootstrapAdministratorEmail?: string;
  schemaVersion?: string;
  pendingMigrations: boolean;
  bootstrapEnabled: boolean;
}

/** Protected first-administrator initialization (specification §7): status plus a one-time secret-guarded action. */
@Component({
  selector: 'app-bootstrap',
  imports: [DatePipe, ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, RouterLink, TranslocoModule, BrandLogoComponent, LanguageSwitcherComponent],
  templateUrl: './bootstrap.component.html',
  styleUrl: '../login/login.component.scss',
})
export class BootstrapComponent {
  private readonly http = inject(HttpClient);
  private readonly fb = inject(FormBuilder);

  readonly status = signal<BootstrapStatus | null>(null);
  readonly busy = signal(false);
  readonly error = signal<{ key: string; detail?: string } | null>(null);
  readonly success = signal(false);
  readonly form = this.fb.nonNullable.group({ initializationSecret: ['', Validators.required] });

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    try {
      this.status.set(await firstValueFrom(this.http.get<BootstrapStatus>('/api/bootstrap/status')));
    } catch {
      this.status.set(null);
    }
  }

  async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.http.post('/api/bootstrap/initialize', this.form.getRawValue()));
      this.success.set(true);
      await this.load();
    } catch (error) {
      const response = error as HttpErrorResponse;
      const problem = response.error as ProblemDetails | undefined;
      const key = response.status === 401 ? 'unauthorized' : response.status === 409 ? 'conflict' : response.status === 403 ? 'forbidden' : response.status === 400 ? 'configuration' : 'unknown';
      this.error.set({ key, detail: problem?.detail });
    } finally {
      this.busy.set(false);
    }
  }
}
