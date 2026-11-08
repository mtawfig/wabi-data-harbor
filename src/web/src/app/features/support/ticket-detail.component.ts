import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Observable } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/i18n/language.service';
import { Administrator } from '../../core/platform/platform.models';
import { PlatformService } from '../../core/platform/platform.service';
import { problemMessage } from '../../core/problem-message';
import { ALLOWED_ATTACHMENT_EXTENSIONS, SupportArea, TicketAttachment, TicketComment, TicketDetail, TicketEvent, TicketStatus } from '../../core/support/support.models';
import { SupportService } from '../../core/support/support.service';
import { TicketImpactComponent, TicketStatusComponent, TicketTypeComponent } from './ticket-chips.component';
import { SupportAccessTabComponent } from './support-access-tab.component';
import { WiDatePipe, WiNumberPipe } from '../../shared/format/locale-pipes';

const EVENT_ICONS: Record<string, string> = {
  TicketCreated: 'add_comment',
  TicketViewed: 'visibility',
  StatusChanged: 'swap_horiz',
  TicketReopened: 'replay',
  TicketAssigned: 'assignment_ind',
  TicketUnassigned: 'person_off',
  CommentAdded: 'forum',
  AttachmentAdded: 'attach_file',
  SupportAccessFlagChanged: 'key',
  SupportAccessProposed: 'key',
  SupportAccessApproved: 'verified',
  SupportAccessDeclined: 'block',
  SupportAccessRevoked: 'cancel',
  SupportAccessExpired: 'timer_off',
  SupportAccessCompleted: 'task_alt',
  SupportSessionStarted: 'login',
  SupportSessionEnded: 'logout',
  SupportOperationExecuted: 'terminal',
};

/** Ticket details (specification §15.8): header plus Overview, Conversation, Activity Timeline and Attachments tabs. */
@Component({
  selector: 'app-ticket-detail',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatTabsModule, RouterLink, TranslocoModule, TicketStatusComponent, TicketImpactComponent, TicketTypeComponent, SupportAccessTabComponent, WiDatePipe, WiNumberPipe],
  templateUrl: './ticket-detail.component.html',
  styleUrl: './ticket-detail.component.scss',
})
export class TicketDetailComponent {
  private readonly support = inject(SupportService);
  private readonly platform = inject(PlatformService);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  readonly auth = inject(AuthService);
  readonly language = inject(LanguageService);

  readonly area = input<SupportArea>('app');
  readonly id = input.required<string>();
  /** Deep link from the Support Access lists: ?tab=access opens tab 5. */
  readonly tab = input<string | undefined>(undefined);
  readonly isPlatform = computed(() => this.area() === 'platform');
  readonly accept = ALLOWED_ATTACHMENT_EXTENSIONS.map((e) => '.' + e).join(',');

  readonly ticket = signal<TicketDetail | null>(null);
  readonly comments = signal<TicketComment[]>([]);
  readonly events = signal<TicketEvent[]>([]);
  readonly attachments = signal<TicketAttachment[]>([]);
  readonly administrators = signal<Administrator[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly reply = signal('');
  readonly targetStatus = signal<TicketStatus | null>(null);
  readonly note = signal('');
  readonly assignee = signal<string | null>(null);
  readonly selectedTab = signal(0);

  readonly summary = computed(() => this.ticket()?.summary ?? null);
  readonly listLink = computed(() => (this.isPlatform() ? '/platform/support' : '/app/support'));
  readonly organizationName = computed(() => {
    const s = this.summary();
    return s ? (this.language.current() === 'ar' ? s.organizationNameAr : s.organizationName) : '';
  });
  readonly noteRequired = computed(() => {
    const target = this.targetStatus();
    const current = this.summary()?.status;
    const isRequester = this.summary()?.createdByUserId === this.auth.session()?.id;
    return target === 'Resolved' || (target === 'Cancelled' && isRequester && (current === 'Started' || current === 'PendingUserResponse'));
  });

  constructor() {
    queueMicrotask(() => this.load());
  }

  load(): void {
    this.support.get(this.area(), this.id()).subscribe({
      next: (t) => {
        this.ticket.set(t);
        if (this.tab() === 'access' && t.summary.requiresSupportAccess && this.selectedTab() === 0) {
          this.selectedTab.set(4);
        }
        this.assignee.set(t.summary.assignedAdministratorId ?? null);
        this.targetStatus.set(t.allowedTransitions[0] ?? null);
        if (t.canAssign && this.administrators().length === 0) {
          this.platform.administrators().subscribe((a) => this.administrators.set(a.filter((x) => x.isActive)));
        }
      },
      error: (response: HttpErrorResponse) => this.error.set(problemMessage(response, this.transloco)),
    });
    this.support.comments(this.area(), this.id()).subscribe((c) => this.comments.set(c));
    this.support.events(this.area(), this.id()).subscribe((e) => this.events.set(e));
    this.support.attachments(this.area(), this.id()).subscribe((a) => this.attachments.set(a));
  }

  actionLabel(status: TicketStatus): string {
    const current = this.summary()?.status;
    return this.transloco.translate(status === 'Started' && current === 'Resolved' ? 'support.actions.Reopen' : `support.actions.${status}`);
  }

  changeStatus(): void {
    const target = this.targetStatus();
    if (!target) {
      return;
    }
    this.run(this.support.changeStatus(this.area(), this.id(), target, this.note() || undefined), 'support.statusChanged', () => this.note.set(''));
  }

  assign(): void {
    this.run(this.support.assign(this.id(), this.assignee()), 'support.assigned');
  }

  sendReply(): void {
    const body = this.reply().trim();
    if (!body || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.support.addComment(this.area(), this.id(), body).subscribe({
      next: () => {
        this.reply.set('');
        this.snack.open(this.transloco.translate('support.replySent'), undefined, { duration: 3000 });
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.support.upload(this.area(), this.id(), file).subscribe({
      next: () => {
        this.snack.open(this.transloco.translate('support.attachments.uploaded'), undefined, { duration: 3000 });
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  downloadUrl(attachment: TicketAttachment): string {
    return this.support.downloadUrl(this.area(), this.id(), attachment.attachmentId);
  }

  icon(eventType: string): string {
    return EVENT_ICONS[eventType] ?? 'circle';
  }

  isMine(comment: TicketComment): boolean {
    return comment.authorId === this.auth.session()?.id;
  }

  details(event: TicketEvent): string | null {
    if (!event.detailsJson) {
      return null;
    }
    try {
      const d = JSON.parse(event.detailsJson) as Record<string, unknown>;
      if (event.eventType === 'TicketAssigned' && typeof d['assignedToName'] === 'string') {
        return d['assignedToName'];
      }
      if (event.eventType === 'AttachmentAdded' && typeof d['fileName'] === 'string') {
        return d['fileName'];
      }
      if (event.eventType === 'SupportOperationExecuted' && typeof d['operation'] === 'string') {
        return `${this.transloco.translate('supportAccess.operations.' + d['operation'])} · ${String(d['outcome'] ?? '')}`;
      }
      if (event.eventType === 'SupportAccessProposed' && Array.isArray(d['operations'])) {
        return (d['operations'] as { operation: string }[]).map((o) => this.transloco.translate('supportAccess.operations.' + o.operation)).join(', ');
      }
      if (event.eventType === 'SupportSessionEnded' && typeof d['reason'] === 'string') {
        return this.transloco.translate('supportAccess.endReasons.' + d['reason']);
      }
      return null;
    } catch {
      return null;
    }
  }

  private run(call: Observable<TicketDetail>, messageKey: string, after?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    call.subscribe({
      next: () => {
        this.snack.open(this.transloco.translate(messageKey), undefined, { duration: 3000 });
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
