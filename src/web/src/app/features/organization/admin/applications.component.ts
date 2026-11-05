import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Observable } from 'rxjs';
import { LanguageService } from '../../../core/i18n/language.service';
import { Application, ConnectionTestResult, DATABASE_TYPES, DatabaseType } from '../../../core/organizations/applications.models';
import { ApplicationsService } from '../../../core/organizations/applications.service';
import { problemMessage } from '../../../core/problem-message';

/**
 * Applications (specification §11): the organization's data sources. The connection string is typed once,
 * sent, encrypted on the server and never shown again; the page shows only "configured" plus the hint.
 */
@Component({
  selector: 'app-applications',
  imports: [DatePipe, DecimalPipe, NgTemplateOutlet, ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatTableModule, RouterLink, TranslocoModule],
  templateUrl: './applications.component.html',
  styleUrl: './applications.component.scss',
})
export class ApplicationsComponent {
  private readonly applications = inject(ApplicationsService);
  private readonly fb = inject(FormBuilder);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  readonly language = inject(LanguageService);

  readonly types = DATABASE_TYPES;
  readonly columns = ['name', 'type', 'connection', 'metadata', 'status', 'actions'];
  readonly items = signal<Application[]>([]);
  readonly editing = signal<Application | null>(null);
  readonly showForm = signal(false);
  readonly connectionFor = signal<Application | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly testResult = signal<ConnectionTestResult | null>(null);

  readonly form = this.fb.nonNullable.group({
    applicationCode: ['', [Validators.required, Validators.pattern(/^[A-Za-z][A-Za-z0-9_-]{1,63}$/)]],
    applicationName: ['', [Validators.required, Validators.maxLength(200)]],
    applicationNameAr: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', Validators.maxLength(1000)],
    databaseType: ['SqlServer' as DatabaseType, Validators.required],
    connectionString: [''],
    connectionHint: ['', Validators.maxLength(300)],
    isActive: [true],
  });

  readonly connectionForm = this.fb.nonNullable.group({
    connectionString: ['', Validators.required],
    connectionHint: ['', Validators.maxLength(300)],
  });

  readonly custom = computed(() => this.items().filter((a) => !a.isBuiltIn));
  readonly builtIn = computed(() => this.items().filter((a) => a.isBuiltIn));

  constructor() {
    this.load();
  }

  load(): void {
    this.applications.list().subscribe((items) => this.items.set(items));
  }

  name(a: Application): string {
    return this.language.current() === 'ar' ? a.applicationNameAr : a.applicationName;
  }

  startCreate(): void {
    this.editing.set(null);
    this.form.reset({ applicationCode: '', applicationName: '', applicationNameAr: '', description: '', databaseType: 'SqlServer', connectionString: '', connectionHint: '', isActive: true });
    this.form.controls.applicationCode.enable();
    this.form.controls.databaseType.enable();
    this.testResult.set(null);
    this.connectionFor.set(null);
    this.showForm.set(true);
    this.error.set(null);
  }

  startEdit(a: Application): void {
    this.editing.set(a);
    this.form.reset({ applicationCode: a.applicationCode, applicationName: a.applicationName, applicationNameAr: a.applicationNameAr, description: a.description ?? '', databaseType: a.databaseType, connectionString: '', connectionHint: '', isActive: a.isActive });
    this.form.controls.applicationCode.disable();
    this.form.controls.databaseType.disable();
    this.testResult.set(null);
    this.connectionFor.set(null);
    this.showForm.set(true);
    this.error.set(null);
  }

  startConnection(a: Application): void {
    this.connectionFor.set(a);
    this.connectionForm.reset({ connectionString: '', connectionHint: a.connectionHint ?? '' });
    this.testResult.set(null);
    this.showForm.set(false);
    this.error.set(null);
  }

  cancel(): void {
    this.showForm.set(false);
    this.connectionFor.set(null);
  }

  submit(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const editing = this.editing();
    const call = editing
      ? this.applications.update(editing.applicationId, { applicationName: v.applicationName, applicationNameAr: v.applicationNameAr, description: v.description || undefined, isActive: v.isActive })
      : this.applications.create({ applicationCode: v.applicationCode, applicationName: v.applicationName, applicationNameAr: v.applicationNameAr, description: v.description || undefined, databaseType: v.databaseType, connectionString: v.connectionString || undefined, connectionHint: v.connectionHint || undefined });
    this.run(call, 'admin.applications.saved', () => this.showForm.set(false));
  }

  submitConnection(): void {
    const target = this.connectionFor();
    if (!target || this.connectionForm.invalid || this.busy()) {
      this.connectionForm.markAllAsTouched();
      return;
    }
    const v = this.connectionForm.getRawValue();
    this.run(this.applications.replaceConnection(target.applicationId, v.connectionString, v.connectionHint || undefined), 'admin.applications.connectionReplaced', () => this.connectionFor.set(null));
  }

  /** Tests the string typed in whichever form is open; the string never leaves the browser except to this call. */
  testTyped(): void {
    const target = this.connectionFor();
    const type = target ? target.databaseType : this.form.getRawValue().databaseType;
    const value = target ? this.connectionForm.getRawValue().connectionString : this.form.getRawValue().connectionString;
    if (!value.trim()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.applications.testConnection(type, value).subscribe({
      next: (r) => {
        this.testResult.set(r);
        this.busy.set(false);
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  testStored(a: Application): void {
    this.busy.set(true);
    this.error.set(null);
    this.applications.testStored(a.applicationId).subscribe({
      next: (r) => {
        this.testResult.set(r);
        this.snack.open(this.transloco.translate(r.succeeded ? 'admin.applications.testOk' : 'admin.applications.testFailed'), undefined, { duration: 4000 });
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  setApproval(a: Application, isApproved: boolean): void {
    this.run(this.applications.setApproval(a.applicationId, isApproved), isApproved ? 'admin.applications.approved' : 'admin.applications.unapproved');
  }

  errorLabel(code: string): string {
    const key = `admin.errors.${code}`;
    const label = this.transloco.translate(key);
    return label === key ? code : label;
  }

  private run(call: Observable<Application>, messageKey: string, after?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    call.subscribe({
      next: () => {
        this.snack.open(this.transloco.translate(messageKey), undefined, { duration: 4000 });
        this.busy.set(false);
        after?.();
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  private fail(response: HttpErrorResponse): void {
    this.error.set(problemMessage(response, this.transloco));
    this.busy.set(false);
  }
}
