import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Observable } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { Administrator } from '../../../core/platform/platform.models';
import { PlatformService } from '../../../core/platform/platform.service';
import { problemMessage } from '../../../core/problem-message';

/** Platform Administrator management (specification §5.1). Temporary passwords are emailed, never shown. */
@Component({
  selector: 'app-platform-administrators',
  imports: [DatePipe, ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatTableModule, TranslocoModule],
  templateUrl: './platform-administrators.component.html',
  styleUrl: '../../organization/admin/admin-catalog.scss',
})
export class PlatformAdministratorsComponent {
  private readonly platform = inject(PlatformService);
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly columns = ['name', 'email', 'kind', 'status', 'lastLogin', 'actions'];
  readonly items = signal<Administrator[]>([]);
  readonly showForm = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    preferredLanguage: ['en' as 'en' | 'ar', Validators.required],
  });

  constructor() {
    this.load();
  }

  isSelf(a: Administrator): boolean {
    return this.auth.session()?.id === a.administratorId;
  }

  load(): void {
    this.platform.administrators().subscribe((items) => this.items.set(items));
  }

  startCreate(): void {
    this.form.reset({ email: '', fullName: '', preferredLanguage: 'en' });
    this.showForm.set(true);
    this.error.set(null);
  }

  submit(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    this.run(this.platform.createAdministrator(v.email, v.fullName, v.preferredLanguage), (r) => this.transloco.translate('platformAdmins.created', { email: r.deliveredTo }), () => this.showForm.set(false));
  }

  reset(a: Administrator): void {
    this.run(this.platform.resetAdministratorPassword(a.administratorId), (r) => this.transloco.translate('platformAdmins.resetSent', { email: r.deliveredTo }));
  }

  setStatus(a: Administrator, isActive: boolean): void {
    this.run(this.platform.setAdministratorStatus(a.administratorId, isActive), () => this.transloco.translate(isActive ? 'platformAdmins.activated' : 'platformAdmins.deactivated'));
  }

  private run<T>(call: Observable<T>, message: (r: T) => string, after?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    call.subscribe({
      next: (r) => {
        this.snack.open(message(r), undefined, { duration: 6000 });
        this.busy.set(false);
        after?.();
        this.load();
      },
      error: (response: HttpErrorResponse) => {
        this.error.set(problemMessage(response, this.transloco));
        this.busy.set(false);
      },
    });
  }
}
