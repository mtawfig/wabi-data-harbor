import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { concatMap, from, lastValueFrom } from 'rxjs';
import { problemMessage } from '../../core/problem-message';
import { ALLOWED_ATTACHMENT_EXTENSIONS, MAX_ATTACHMENT_BYTES, TICKET_IMPACTS, TICKET_TYPES, TicketDetail, TicketImpact, TicketType } from '../../core/support/support.models';
import { SupportService } from '../../core/support/support.service';

/** New ticket (specification §15.6). Reference, organization, requester and status are set by the server. */
@Component({
  selector: 'app-ticket-form',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, RouterLink, TranslocoModule],
  templateUrl: './ticket-form.component.html',
  styleUrl: './support.scss',
})
export class TicketFormComponent {
  private readonly support = inject(SupportService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly types = TICKET_TYPES;
  readonly impacts = TICKET_IMPACTS;
  readonly accept = ALLOWED_ATTACHMENT_EXTENSIONS.map((e) => '.' + e).join(',');
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly files = signal<File[]>([]);
  readonly form = this.fb.nonNullable.group({
    ticketType: ['Request' as TicketType, Validators.required],
    impact: ['Medium' as TicketImpact, Validators.required],
    category: ['', Validators.maxLength(100)],
    subject: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(200)]],
    description: ['', [Validators.required, Validators.maxLength(8000)]],
    requiresSupportAccess: [false],
  });

  pick(event: Event): void {
    const input = event.target as HTMLInputElement;
    const chosen = Array.from(input.files ?? []);
    const rejected = chosen.filter((f) => f.size > MAX_ATTACHMENT_BYTES || !ALLOWED_ATTACHMENT_EXTENSIONS.includes(f.name.split('.').pop()?.toLowerCase() ?? ''));
    if (rejected.length > 0) {
      this.error.set(this.transloco.translate('admin.errors.' + (rejected.some((f) => f.size > MAX_ATTACHMENT_BYTES) ? 'attachment_too_large' : 'attachment_type_not_allowed')));
    }
    this.files.update((current) => [...current, ...chosen.filter((f) => !rejected.includes(f))]);
    input.value = '';
  }

  remove(file: File): void {
    this.files.update((current) => current.filter((f) => f !== file));
  }

  async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const v = this.form.getRawValue();
    let ticket: TicketDetail;
    try {
      ticket = await lastValueFrom(this.support.create({ ...v, category: v.category || undefined }));
    } catch (response) {
      this.error.set(problemMessage(response as HttpErrorResponse, this.transloco));
      this.busy.set(false);
      return;
    }

    // The ticket exists from here on: whatever happens to the attachments, open it, so submitting again can never
    // create a second ticket (F4). A file that could not be added is reported there and can be added again.
    const id = ticket.summary.ticketId;
    let uploadError: string | null = null;
    try {
      if (this.files().length > 0) {
        await lastValueFrom(from(this.files()).pipe(concatMap((file) => this.support.upload('app', id, file))), { defaultValue: null });
      }
    } catch (response) {
      uploadError = problemMessage(response as HttpErrorResponse, this.transloco);
    }
    const created = this.transloco.translate('support.created', { reference: ticket.summary.referenceNumber });
    if (uploadError) {
      this.snack.open(`${created} ${this.transloco.translate('support.attachmentFailed', { reason: uploadError })}`, this.transloco.translate('common.close'), { duration: 15000 });
    } else {
      this.snack.open(created, undefined, { duration: 6000 });
    }
    // Stays busy while leaving the form, so a second click cannot submit it again.
    if (!(await this.router.navigate(['/app/support', id]))) {
      this.busy.set(false);
    }
  }
}
