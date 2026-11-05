import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { AuthService } from '../../core/auth/auth.service';
import { ProblemDetails } from '../../core/auth/session.model';
import { BrandLogoComponent } from '../../shared/ui/brand-logo.component';
import { LanguageSwitcherComponent } from '../../shared/ui/language-switcher.component';

function matchingPasswords(group: AbstractControl): ValidationErrors | null {
  const next = group.get('newPassword')?.value;
  const confirm = group.get('confirmPassword')?.value;
  return next && confirm && next !== confirm ? { mismatch: true } : null;
}

@Component({
  selector: 'app-change-password',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, TranslocoModule, BrandLogoComponent, LanguageSwitcherComponent],
  templateUrl: './change-password.component.html',
  styleUrl: '../public/login/login.component.scss',
})
export class ChangePasswordComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: matchingPasswords },
  );
  readonly busy = signal(false);
  readonly errors = signal<string[]>([]);

  async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.errors.set([]);
    try {
      const { currentPassword, newPassword } = this.form.getRawValue();
      await this.auth.changePassword(currentPassword, newPassword);
      await this.router.navigateByUrl(this.auth.homeUrl());
    } catch (error) {
      const problem = (error as HttpErrorResponse).error as ProblemDetails | undefined;
      const codes = Object.values(problem?.errors ?? {}).flat();
      this.errors.set(codes.length > 0 ? codes : ['password_required']);
    } finally {
      this.busy.set(false);
    }
  }
}
