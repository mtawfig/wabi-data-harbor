import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { countryName } from '../../../core/demo/countries';
import { DEMO_IMPRESSIONS, DEMO_STATUSES, DemoAssignee, DemoImpression, DemoRequestDetail, DemoRequestStatus } from '../../../core/demo/demo-requests.models';
import { DemoRequestsService } from '../../../core/demo/demo-requests.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { problemMessage } from '../../../core/problem-message';
import { WiDatePipe } from '../../../shared/format/locale-pipes';

/** The value of a date-and-time field (the administrator's own clock) for an instant, and back. */
export function toLocalInput(iso: string | null | undefined): string {
  if (!iso) {
    return '';
  }
  const date = new Date(iso);
  const pad = (n: number): string => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function fromLocalInput(value: string): string | null {
  return value ? new Date(value).toISOString() : null;
}

/**
 * One demo request (P13.52): what the visitor sent, the follow-up (status, who handles it, the schedule), the demo as
 * it was performed with the key person who attended, and the history of every change.
 */
@Component({
  selector: 'app-demo-request-detail',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, RouterLink, TranslocoModule, WiDatePipe],
  templateUrl: './demo-request-detail.component.html',
  styleUrl: './demo-requests.scss',
})
export class DemoRequestDetailComponent {
  private readonly demoRequests = inject(DemoRequestsService);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);
  private readonly transloco = inject(TranslocoService);
  private readonly language = inject(LanguageService);

  readonly statuses = DEMO_STATUSES;
  readonly impressions = DEMO_IMPRESSIONS;
  readonly request = signal<DemoRequestDetail | null>(null);
  readonly assignees = signal<DemoAssignee[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly saved = signal(false);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    status: ['New' as DemoRequestStatus, [Validators.required]],
    assignedAdministratorId: [''],
    remark: ['', [Validators.maxLength(2000)]],
    scheduledAt: [''],
    meetingLocation: ['', [Validators.maxLength(400)]],
    performedAt: [''],
    presentedBy: ['', [Validators.maxLength(150)]],
    attendeeName: ['', [Validators.maxLength(150)]],
    attendeePosition: ['', [Validators.maxLength(150)]],
    attendeeEmail: ['', [Validators.email, Validators.maxLength(254)]],
    attendeePhone: ['', [Validators.maxLength(40)]],
    attendeeMobile: ['', [Validators.maxLength(40)]],
    impression: ['' as DemoImpression | ''],
    performedRemark: ['', [Validators.maxLength(2000)]],
    nextStep: ['', [Validators.maxLength(500)]],
    followUpDate: [''],
    note: ['', [Validators.maxLength(2000)]],
  });

  private readonly id = this.route.snapshot.paramMap.get('id') ?? '';

  constructor() {
    this.demoRequests.assignees().subscribe({ next: (assignees) => this.assignees.set(assignees) });
    this.demoRequests.get(this.id).subscribe({
      next: (request) => {
        this.show(request);
        this.loading.set(false);
      },
      error: () => {
        this.notFound.set(true);
        this.loading.set(false);
      },
    });
  }

  countryLabel(code: string): string {
    return countryName(code, this.language.current());
  }

  save(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const text = (v: string): string | null => v.trim() || null;
    this.saving.set(true);
    this.saved.set(false);
    this.error.set(null);
    this.demoRequests
      .update(this.id, {
        status: value.status,
        assignedAdministratorId: value.assignedAdministratorId || null,
        remark: text(value.remark),
        scheduledAt: fromLocalInput(value.scheduledAt),
        meetingLocation: text(value.meetingLocation),
        performedAt: fromLocalInput(value.performedAt),
        presentedBy: text(value.presentedBy),
        attendeeName: text(value.attendeeName),
        attendeePosition: text(value.attendeePosition),
        attendeeEmail: text(value.attendeeEmail),
        attendeePhone: text(value.attendeePhone),
        attendeeMobile: text(value.attendeeMobile),
        impression: value.impression || null,
        performedRemark: text(value.performedRemark),
        nextStep: text(value.nextStep),
        followUpDate: value.followUpDate || null,
        note: text(value.note),
      })
      .subscribe({
        next: (request) => {
          this.show(request);
          this.saving.set(false);
          this.saved.set(true);
        },
        error: (response: HttpErrorResponse) => {
          this.error.set(problemMessage(response, this.transloco));
          this.saving.set(false);
        },
      });
  }

  private show(request: DemoRequestDetail): void {
    this.request.set(request);
    this.form.reset({
      status: request.status,
      assignedAdministratorId: request.assignedAdministratorId ?? '',
      remark: request.remark ?? '',
      scheduledAt: toLocalInput(request.scheduledAt),
      meetingLocation: request.meetingLocation ?? '',
      performedAt: toLocalInput(request.performedAt),
      presentedBy: request.presentedBy ?? '',
      attendeeName: request.attendeeName ?? '',
      attendeePosition: request.attendeePosition ?? '',
      attendeeEmail: request.attendeeEmail ?? '',
      attendeePhone: request.attendeePhone ?? '',
      attendeeMobile: request.attendeeMobile ?? '',
      impression: request.impression ?? '',
      performedRemark: request.performedRemark ?? '',
      nextStep: request.nextStep ?? '',
      followUpDate: request.followUpDate ?? '',
      note: '',
    });
    if (request.erasedAt) {
      this.form.disable();
    }
  }
}
