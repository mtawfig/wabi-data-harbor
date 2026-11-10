import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Observable } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/i18n/language.service';
import { ROW_SCOPES } from '../../core/organizations/permissions.models';
import { problemMessage } from '../../core/problem-message';
import { GrantAction, ROLE_ASSIGNING_OPERATIONS, SupportAccessGrant, SupportOperationCode, SupportOperationDefinition, SupportOperationEntry, SupportTargets } from '../../core/support/support-access.models';
import { SupportAccessService } from '../../core/support/support-access.service';
import { SupportArea, TicketDetail } from '../../core/support/support.models';
import { GrantStatusComponent } from './grant-status.component';
import { WiDatePipe } from '../../shared/format/locale-pipes';

interface ProposalRow {
  definition: SupportOperationDefinition;
  selected: boolean;
  targets: string[];
  manual: string;
}

/**
 * Ticket tab 5 (specification §15.8 / §16): the platform side proposes operations with specific targets, enters support
 * mode and runs permitted operations; the organization administrator approves, declines or revokes.
 */
@Component({
  selector: 'app-support-access-tab',
  imports: [FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, TranslocoModule, GrantStatusComponent, WiDatePipe],
  templateUrl: './support-access-tab.component.html',
  styleUrl: './support-access-tab.component.scss',
})
export class SupportAccessTabComponent {
  private readonly access = inject(SupportAccessService);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  readonly auth = inject(AuthService);
  readonly language = inject(LanguageService);

  readonly area = input.required<SupportArea>();
  readonly ticket = input.required<TicketDetail>();
  /** Emitted after any change so the ticket page can reload its timeline. */
  readonly changed = output<void>();

  readonly grants = signal<SupportAccessGrant[]>([]);
  readonly catalog = signal<SupportOperationDefinition[]>([]);
  readonly targets = signal<SupportTargets | null>(null);
  readonly rows = signal<ProposalRow[]>([]);
  readonly reason = signal('');
  readonly expiryHours = signal(4);
  readonly note = signal('');
  readonly approveHours = signal<number | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly proposing = signal(false);

  // operation panel
  readonly runOperation = signal<SupportOperationCode | null>(null);
  readonly runTarget = signal<string | null>(null);
  readonly runPayload = signal<Record<string, string | boolean>>({});
  readonly rowScopes = ROW_SCOPES;
  readonly runRoleCodes = signal<string[]>([]);
  readonly lastResult = signal<string | null>(null);

  readonly isPlatform = computed(() => this.area() === 'platform');
  readonly openGrant = computed(() => this.grants().find((g) => g.status === 'Proposed' || g.status === 'Approved' || g.status === 'Active') ?? null);
  readonly session = computed(() => this.access.session());
  readonly activeHere = computed(() => {
    const s = this.session();
    const g = this.openGrant();
    return s !== null && g !== null && s.grant.grantId === g.grantId;
  });
  readonly runnable = computed(() => {
    const s = this.session();
    if (!s || !this.activeHere()) {
      return [] as SupportOperationEntry[];
    }
    const executable = new Set(this.catalog().filter((d) => d.executable).map((d) => d.code));
    return s.operations.filter((o) => executable.has(o.operation));
  });
  readonly runDefinition = computed(() => this.catalog().find((d) => d.code === this.runOperation()) ?? null);
  readonly runTargets = computed(() => this.runnable().find((o) => o.operation === this.runOperation())?.targetReferences ?? []);
  readonly canPropose = computed(() => this.isPlatform() && this.openGrant() === null && this.ticket().summary.status !== 'Resolved' && this.ticket().summary.status !== 'Cancelled');

  constructor() {
    queueMicrotask(() => this.load());
  }

  load(): void {
    this.access.forTicket(this.area(), this.ticket().summary.ticketId).subscribe((g) => this.grants.set(g));
    if (this.isPlatform()) {
      if (this.catalog().length === 0) {
        this.access.catalog().subscribe((c) => {
          this.catalog.set(c);
          this.rows.set(c.map((definition) => ({ definition, selected: false, targets: [], manual: '' })));
        });
      }
      this.access.refreshSession().subscribe();
    }
  }

  startProposal(): void {
    this.proposing.set(true);
    this.error.set(null);
    if (!this.targets()) {
      this.access.targets(this.ticket().summary.ticketId).subscribe((t) => this.targets.set(t));
    }
  }

  toggleRow(row: ProposalRow, selected: boolean): void {
    this.rows.update((rows) => rows.map((r) => (r === row ? { ...r, selected } : r)));
  }

  setTargets(row: ProposalRow, targets: string[]): void {
    this.rows.update((rows) => rows.map((r) => (r === row ? { ...r, targets } : r)));
  }

  setManual(row: ProposalRow, manual: string): void {
    this.rows.update((rows) => rows.map((r) => (r === row ? { ...r, manual } : r)));
  }

  submitProposal(): void {
    const operations: SupportOperationEntry[] = this.rows()
      .filter((r) => r.selected)
      .map((r) => ({
        operation: r.definition.code,
        targetReferences: r.definition.targetType === 'None' ? [] : r.definition.targetType === 'Application' ? r.manual.split(/[\s,]+/).filter((x) => x.length > 0) : r.targets,
      }));
    this.run(this.access.propose(this.ticket().summary.ticketId, operations, this.reason(), this.expiryHours()), 'supportAccess.proposed', () => {
      this.proposing.set(false);
      this.reason.set('');
      this.rows.update((rows) => rows.map((r) => ({ ...r, selected: false, targets: [], manual: '' })));
    });
  }

  act(grant: SupportAccessGrant, action: GrantAction): void {
    const note = this.note().trim() || null;
    const call: Observable<unknown> =
      action === 'approve' ? this.access.approve(grant.grantId, note, this.approveHours())
      : action === 'decline' ? this.access.decline(grant.grantId, note)
      : action === 'revoke' ? this.access.revoke(this.area(), grant.grantId, note)
      : action === 'complete' ? this.access.complete(grant.grantId, note)
      : action === 'enter' ? this.access.enter(grant.grantId)
      : this.access.exit();
    this.run(call, `supportAccess.done.${action}`, () => this.note.set(''));
  }

  selectOperation(code: SupportOperationCode | null): void {
    this.runOperation.set(code);
    this.runTarget.set(this.runDefinition()?.requiresTarget && this.runTargets().length === 1 ? this.runTargets()[0] : null);
    this.runPayload.set(code === 'UpdateTablePermissions' ? { granted: true, allColumns: true, rowScope: 'All' } : {});
    this.runRoleCodes.set([]);
    this.lastResult.set(null);
    if (code === 'UpdateTablePermissions' && !this.targets()) {
      this.access.targets(this.ticket().summary.ticketId).subscribe((t) => this.targets.set(t));
    }
  }

  setField(name: string, value: string | boolean): void {
    this.runPayload.update((p) => ({ ...p, [name]: value }));
  }

  execute(): void {
    const code = this.runOperation();
    if (!code) {
      return;
    }
    const payload: Record<string, unknown> = { ...this.runPayload() };
    if (code === 'CreateOrganizationUser') {
      payload['roleCodes'] = this.runRoleCodes();
    }
    this.busy.set(true);
    this.error.set(null);
    this.access.execute(code, this.runDefinition()?.requiresTarget ? this.runTarget() : null, payload).subscribe({
      next: (r) => {
        this.lastResult.set(r.deliveredTo ? this.transloco.translate('supportAccess.executedDelivered', { email: r.deliveredTo }) : this.transloco.translate('supportAccess.executed'));
        this.snack.open(this.transloco.translate('supportAccess.executed'), undefined, { duration: 4000 });
        this.busy.set(false);
        this.runPayload.set({});
        this.runRoleCodes.set([]);
        this.load();
        this.changed.emit();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  targetLabel(reference: string, grant?: SupportAccessGrant): string {
    if (reference.toLowerCase() === 'new') {
      return this.transloco.translate('supportAccess.newRole');
    }
    const fromGrant = (this.language.current() === 'ar' ? grant?.targetLabelsAr?.[reference] : undefined) ?? grant?.targetLabels?.[reference];
    if (fromGrant && fromGrant.toLowerCase() !== reference.toLowerCase()) {
      return fromGrant;
    }
    const t = this.targets();
    const user = t?.users.find((u) => u.userId === reference);
    if (user) {
      return `${user.fullName} (${user.email})`;
    }
    const role = t?.roles.find((r) => r.roleId === reference);
    if (role) {
      return this.language.current() === 'ar' ? role.nameAr : role.nameEn;
    }
    return reference;
  }

  operationLabel(code: string): string {
    return this.transloco.translate(`supportAccess.operations.${code}`);
  }

  organizationName(grant: SupportAccessGrant): string {
    return this.language.current() === 'ar' ? grant.organizationNameAr : grant.organizationName;
  }

  /** True for an operation whose grant targets are the roles it may give (shown as such to the approver). */
  assignsRoles(operation: SupportOperationCode): boolean {
    return ROLE_ASSIGNING_OPERATIONS.includes(operation);
  }

  /** Roles the running operation may give: only the ones the organization approved on this grant. */
  roleOptions(): { code: string; label: string }[] {
    const granted = new Set(this.runTargets().map((t) => t.toLowerCase()));
    return (this.targets()?.roles ?? [])
      .filter((r) => r.isActive && granted.has(r.roleId.toLowerCase()))
      .map((r) => ({ code: r.roleCode, label: this.language.current() === 'ar' ? r.nameAr : r.nameEn }));
  }

  ensureTargets(): void {
    if (!this.targets()) {
      this.access.targets(this.ticket().summary.ticketId).subscribe((t) => this.targets.set(t));
    }
  }

  private run(call: Observable<unknown>, messageKey: string, after?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    call.subscribe({
      next: () => {
        this.snack.open(this.transloco.translate(messageKey), undefined, { duration: 4000 });
        this.busy.set(false);
        after?.();
        this.load();
        this.changed.emit();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  private fail(response: HttpErrorResponse): void {
    this.error.set(problemMessage(response, this.transloco));
    this.busy.set(false);
  }
}
