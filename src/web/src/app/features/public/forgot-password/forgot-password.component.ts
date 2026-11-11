import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { AuthService } from '../../../core/auth/auth.service';
import { BrandLogoComponent } from '../../../shared/ui/brand-logo.component';
import { LanguageSwitcherComponent } from '../../../shared/ui/language-switcher.component';

/**
 * Self-service password reset, step 1: the person enters the sign-in e-mail and is told, whatever the address, that a
 * link was sent if an account exists. The page never says whether it does.
 */
@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, RouterLink, TranslocoModule, BrandLogoComponent, LanguageSwitcherComponent],
  templateUrl: './forgot-password.component.html',
  styleUrl: '../login/login.component.scss',
})
export class ForgotPasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({ email: ['', [Validators.required, Validators.email]] });
  readonly busy = signal(false);
  readonly sent = signal(false);
  readonly error = signal<'rate_limited' | 'unknown' | null>(null);

  async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.requestPasswordReset(this.form.getRawValue().email.trim());
      this.sent.set(true);
    } catch (failure) {
      this.error.set(failure instanceof HttpErrorResponse && failure.status === 429 ? 'rate_limited' : 'unknown');
    } finally {
      this.busy.set(false);
    }
  }
}
