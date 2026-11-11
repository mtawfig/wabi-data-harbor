import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { AuthService } from '../../../core/auth/auth.service';
import { ProblemDetails } from '../../../core/auth/session.model';
import { BrandLogoComponent } from '../../../shared/ui/brand-logo.component';
import { LanguageSwitcherComponent } from '../../../shared/ui/language-switcher.component';

function matchingPasswords(group: AbstractControl): ValidationErrors | null {
  const next = group.get('newPassword')?.value;
  const confirm = group.get('confirmPassword')?.value;
  return next && confirm && next !== confirm ? { mismatch: true } : null;
}

/** What the page shows: checking the link, the form, a link that cannot be used, a check that could not be made, done. */
type ResetState = 'checking' | 'ready' | 'invalid' | 'unavailable' | 'done';

/** A reset link's token: letters, digits, "-" and "_". Anything else in the address is not one. */
const TOKEN = /^[A-Za-z0-9_-]{20,128}$/;

/**
 * Self-service password reset, step 2: the page a reset link opens. The link carries its token after "#", which the
 * browser never sends to a server; the page takes it from there, removes it from the address bar at once (so it is
 * not left in the history or copied with the address) and keeps it in memory only. The form with the two password
 * fields appears only when the API confirms the link can still be used: opened without a link, or with a used or
 * expired one, the page offers to ask for a new link and nothing else.
 */
@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, RouterLink, TranslocoModule, BrandLogoComponent, LanguageSwitcherComponent],
  templateUrl: './reset-password.component.html',
  styleUrl: '../login/login.component.scss',
})
export class ResetPasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly fb = inject(FormBuilder);

  private token: string | null = null;
  private opened = false;

  readonly form = this.fb.nonNullable.group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: matchingPasswords },
  );
  readonly state = signal<ResetState>('checking');
  readonly busy = signal(false);
  readonly errors = signal<string[]>([]);
  readonly showPassword = signal(false);

  constructor() {
    // The fragment is followed, not read once: a reset link opened in a tab where this page is already showing
    // changes only the part after "#", which does not load the page again.
    this.route.fragment.pipe(takeUntilDestroyed()).subscribe((fragment) => void this.accept(fragment));
  }

  private async accept(fragment: string | null): Promise<void> {
    const candidate = new URLSearchParams(fragment ?? '').get('token') ?? '';
    const first = !this.opened;
    this.opened = true;
    if (!TOKEN.test(candidate)) {
      if (first) {
        // Opened without a link.
        this.location.replaceState('/reset-password');
        this.state.set('invalid');
      }

      return;
    }

    this.token = candidate;
    this.form.reset();
    this.errors.set([]);
    // The token leaves the address bar before anything else happens.
    this.location.replaceState('/reset-password');
    await this.check();
  }

  /** Asks the API whether the link still works; also the "try again" action after a failed check. */
  async check(): Promise<void> {
    if (!this.token) {
      this.state.set('invalid');
      return;
    }

    this.state.set('checking');
    try {
      this.state.set((await this.auth.isResetLinkValid(this.token)) ? 'ready' : 'invalid');
    } catch {
      this.state.set('unavailable');
    }
  }

  async submit(): Promise<void> {
    if (this.form.invalid || this.busy() || !this.token) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.errors.set([]);
    try {
      await this.auth.resetPassword(this.token, this.form.getRawValue().newPassword);
      this.token = null;
      this.form.reset();
      this.state.set('done');
    } catch (failure) {
      const response = failure as HttpErrorResponse;
      const codes = Object.values((response.error as ProblemDetails | undefined)?.errors ?? {}).flat();
      if (codes.includes('reset_link_invalid')) {
        // Used elsewhere or expired while the form was open.
        this.token = null;
        this.state.set('invalid');
      } else if (response.status === 429) {
        this.errors.set(['rate_limited']);
      } else {
        this.errors.set(codes.length > 0 ? codes : ['unknown']);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
