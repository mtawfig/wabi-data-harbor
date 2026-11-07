import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/i18n/language.service';
import {
  AlertSeverity,
  JobRecurrenceKind,
  MonitoringRule,
  MonitoringSettings,
  OwnerOption,
  RuleDefinition,
  RuleTemplate,
  RuleTestResult,
  SaveMonitoringRule,
  TemplateCandidate,
  TemplateColumnOption,
} from '../../core/monitoring/monitoring.models';
import { MonitoringService } from '../../core/monitoring/monitoring.service';
import { OrganizationUser, Role } from '../../core/organizations/admin.models';
import { OrganizationAdminService } from '../../core/organizations/admin.service';
import { problemMessage } from '../../core/problem-message';

interface RuleForm {
  ruleId: string | null;
  template: RuleTemplate | null;
  /** Definition kept as-is for rules created without a template (through the API). */
  customDefinition: RuleDefinition | null;
  kind: 'Data' | 'Access';
  ownerUserId: string | null;
  objectId: string | null;
  bindings: Record<string, string>;
  name: string;
  description: string;
  severity: AlertSeverity;
  recurrenceKind: JobRecurrenceKind;
  intervalMinutes: number;
  time: string;
  dayOfWeek: number;
  cooldownHours: number;
  isActive: boolean;
  windowHours: number;
  conditionOperator: string;
  conditionValue: number;
  thresholdValue: number;
  recipientUserIds: string[];
  recipientRoleIds: string[];
}

const DAYS = [1, 2, 3, 4, 5, 6, 7];

/**
 * Monitoring Rules (specification §35.1): templates offered from the organization's own metadata, bound to the
 * owner's permitted columns; every rule runs under its owner's permissions. Administrators choose the owner and
 * recipients and manage access rules; Management users create data rules for themselves.
 */
@Component({
  selector: 'app-rules',
  imports: [DatePipe, DecimalPipe, FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatSlideToggleModule, RouterLink, TranslocoModule],
  templateUrl: './rules.component.html',
  styleUrl: './monitoring.scss',
})
export class RulesComponent {
  private readonly monitoring = inject(MonitoringService);
  private readonly admin = inject(OrganizationAdminService);
  private readonly transloco = inject(TranslocoService);
  private readonly auth = inject(AuthService);
  readonly language = inject(LanguageService);

  readonly days = DAYS;
  readonly severities: AlertSeverity[] = ['High', 'Medium', 'Info'];
  readonly operators = ['gt', 'gte', 'lt', 'lte', 'eq', 'neq'];
  readonly isAdmin = this.auth.isOrganizationAdmin;
  readonly rules = signal<MonitoringRule[]>([]);
  readonly templates = signal<RuleTemplate[]>([]);
  readonly owners = signal<OwnerOption[]>([]);
  readonly users = signal<OrganizationUser[]>([]);
  readonly roles = signal<Role[]>([]);
  readonly form = signal<RuleForm | null>(null);
  readonly picking = signal(false);
  /** Owner chosen while picking a template (administrators); templates follow the owner's permissions. */
  readonly ownerDraft = signal<string | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly test = signal<{ ruleId: string | null; result: RuleTestResult } | null>(null);
  readonly settings = signal<MonitoringSettings | null>(null);
  readonly settingsOpen = signal(false);
  readonly settingsSaved = signal(false);

  readonly dataTemplates = computed(() => this.templates().filter((t) => t.kind === 'Data'));
  readonly accessTemplates = computed(() => this.templates().filter((t) => t.kind === 'Access'));
  readonly candidate = computed<TemplateCandidate | null>(() => {
    const f = this.form();
    return f?.template?.candidates.find((c) => c.objectId === f.objectId) ?? null;
  });

  constructor() {
    this.load();
    this.loadTemplates(null);
    if (this.isAdmin()) {
      this.monitoring.owners().subscribe((o) => this.owners.set(o));
      this.admin.listUsers(1, 200, undefined, 'Active').subscribe((p) => this.users.set(p.items));
      this.admin.listRoles().subscribe((r) => this.roles.set(r.filter((x) => x.isActive)));
      this.monitoring.settings().subscribe((s) => this.settings.set(s));
    }
  }

  load(): void {
    this.loading.set(true);
    this.monitoring.rules().subscribe({
      next: (r) => {
        this.rules.set(r);
        this.loading.set(false);
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  loadTemplates(ownerUserId: string | null, then?: () => void): void {
    this.monitoring.templates(ownerUserId).subscribe((t) => {
      this.templates.set(t);
      then?.();
    });
  }

  startNew(): void {
    this.error.set(null);
    this.test.set(null);
    this.form.set(null);
    this.ownerDraft.set(null);
    this.loadTemplates(null);
    this.picking.set(true);
  }

  ownerName(ownerUserId: string | null): string {
    return ownerUserId ? (this.owners().find((o) => o.userId === ownerUserId)?.fullName ?? '') : this.transloco.translate('monitoring.rules.ownerSelf');
  }

  pickOwner(ownerUserId: string | null): void {
    this.ownerDraft.set(ownerUserId);
    this.loadTemplates(ownerUserId);
  }

  pick(template: RuleTemplate): void {
    if (!template.available) {
      return;
    }
    const data = template.defaults.data;
    const access = template.defaults.access;
    const candidate = template.candidates[0] ?? null;
    this.form.set({
      ruleId: null,
      template,
      customDefinition: null,
      kind: template.kind,
      ownerUserId: this.ownerDraft(),
      objectId: candidate?.objectId ?? null,
      bindings: candidate ? this.defaultBindings(candidate) : {},
      name: this.transloco.translate(`monitoring.templates.${template.code}.name`),
      description: '',
      severity: template.severity,
      recurrenceKind: template.recurrenceKind,
      intervalMinutes: template.intervalMinutes || 60,
      time: template.timeOfDay.slice(0, 5),
      dayOfWeek: 1,
      cooldownHours: 24,
      isActive: true,
      windowHours: data?.windowHours ?? access?.windowHours ?? 24,
      conditionOperator: data?.condition.operator ?? 'gt',
      conditionValue: data?.condition.value ?? 0,
      thresholdValue: access?.threshold.value ?? 1,
      recipientUserIds: [],
      recipientRoleIds: [],
    });
    this.picking.set(false);
  }

  edit(rule: MonitoringRule): void {
    this.error.set(null);
    this.test.set(null);
    this.picking.set(false);
    this.loadTemplates(this.isAdmin() ? rule.ownerUserId : null, () => {
      const template = this.templates().find((t) => t.code === rule.templateCode) ?? null;
      const d = rule.definition.data;
      const a = rule.definition.access;
      this.form.set({
        ruleId: rule.ruleId,
        template,
        customDefinition: template ? null : rule.definition,
        kind: rule.kind,
        ownerUserId: rule.ownerUserId,
        objectId: d?.objectId ?? null,
        bindings: d && template ? this.bindingsFrom(template.code, rule.definition) : {},
        name: rule.name,
        description: rule.description ?? '',
        severity: rule.severity,
        recurrenceKind: rule.recurrenceKind,
        intervalMinutes: rule.intervalMinutes || 60,
        time: rule.timeOfDay.slice(0, 5),
        dayOfWeek: rule.dayOfWeek ?? 1,
        cooldownHours: rule.cooldownHours,
        isActive: rule.isActive,
        windowHours: d?.windowHours ?? a?.windowHours ?? 24,
        conditionOperator: d?.condition.operator ?? 'gt',
        conditionValue: d?.condition.value ?? 0,
        thresholdValue: a?.threshold.value ?? 1,
        recipientUserIds: rule.recipients.filter((r) => r.userId).map((r) => r.userId!),
        recipientRoleIds: rule.recipients.filter((r) => r.roleId).map((r) => r.roleId!),
      });
    });
  }

  cancel(): void {
    this.form.set(null);
    this.picking.set(false);
    this.test.set(null);
    this.error.set(null);
  }

  patch(changes: Partial<RuleForm>): void {
    this.form.update((f) => (f ? { ...f, ...changes } : f));
  }

  changeOwner(ownerUserId: string | null): void {
    this.patch({ ownerUserId });
    const code = this.form()?.template?.code;
    this.loadTemplates(ownerUserId, () => {
      const template = this.templates().find((t) => t.code === code) ?? null;
      const candidate = template?.candidates[0] ?? null;
      this.patch({ template, objectId: candidate?.objectId ?? null, bindings: candidate ? this.defaultBindings(candidate) : {} });
    });
  }

  changeObject(objectId: string): void {
    const candidate = this.form()?.template?.candidates.find((c) => c.objectId === objectId);
    this.patch({ objectId, bindings: candidate ? this.defaultBindings(candidate) : {} });
  }

  bind(slot: string, columnId: string): void {
    this.form.update((f) => (f ? { ...f, bindings: { ...f.bindings, [slot]: columnId } } : f));
  }

  columnLabel(c: TemplateColumnOption): string {
    const name = this.language.current() === 'ar' ? c.displayNameAr || c.displayNameEn : c.displayNameEn;
    return name && name !== c.columnName ? `${name} (${c.columnName})` : c.columnName;
  }

  objectLabel(c: TemplateCandidate): string {
    const name = this.language.current() === 'ar' ? c.displayNameAr || c.displayNameEn : c.displayNameEn;
    return `${c.applicationCode} · ${name ?? c.objectName} (${c.schemaName}.${c.objectName})`;
  }

  save(): void {
    const request = this.request();
    if (!request || this.busy()) {
      return;
    }
    const f = this.form()!;
    this.busy.set(true);
    this.error.set(null);
    const call = f.ruleId ? this.monitoring.updateRule(f.ruleId, request) : this.monitoring.createRule(request);
    call.subscribe({
      next: () => {
        this.busy.set(false);
        this.form.set(null);
        this.test.set(null);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  testDraft(): void {
    const request = this.request();
    if (!request || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.monitoring.testDraft(request).subscribe({
      next: (result) => {
        this.test.set({ ruleId: null, result });
        this.busy.set(false);
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  testSaved(rule: MonitoringRule): void {
    this.busy.set(true);
    this.error.set(null);
    this.monitoring.testRule(rule.ruleId).subscribe({
      next: (result) => {
        this.test.set({ ruleId: rule.ruleId, result });
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  toggle(rule: MonitoringRule): void {
    this.busy.set(true);
    this.error.set(null);
    this.monitoring.setActive(rule.ruleId, !rule.isActive).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  remove(rule: MonitoringRule): void {
    if (!confirm(this.transloco.translate('monitoring.rules.deleteConfirm', { name: rule.name }))) {
      return;
    }
    this.busy.set(true);
    this.monitoring.deleteRule(rule.ruleId).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  schedule(rule: { recurrenceKind: JobRecurrenceKind; intervalMinutes: number; timeOfDay: string; dayOfWeek?: number }): string {
    const time = rule.timeOfDay.slice(0, 5);
    switch (rule.recurrenceKind) {
      case 'Interval':
        return this.transloco.translate('monitoring.rules.everyMinutes', { minutes: rule.intervalMinutes });
      case 'Weekly':
        return this.transloco.translate('monitoring.rules.weeklyAt', { day: this.transloco.translate(`monitoring.days.${rule.dayOfWeek ?? 1}`), time });
      default:
        return this.transloco.translate('monitoring.rules.dailyAt', { time });
    }
  }

  // Monitoring settings (administrators): digest time, working hours and days, retention.
  setSetting<K extends keyof MonitoringSettings>(key: K, value: MonitoringSettings[K]): void {
    this.settings.update((s) => (s ? { ...s, [key]: value } : s));
    this.settingsSaved.set(false);
  }

  toggleDay(day: number, on: boolean): void {
    const s = this.settings();
    if (!s) {
      return;
    }
    const days = new Set(s.workingDays.split(''));
    if (on) {
      days.add(String(day));
    } else {
      days.delete(String(day));
    }
    this.setSetting('workingDays', [...days].sort().join(''));
  }

  saveSettings(): void {
    const s = this.settings();
    if (!s || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.monitoring.saveSettings({ digestTime: this.withSeconds(s.digestTime), workingHoursStart: this.withSeconds(s.workingHoursStart), workingHoursEnd: this.withSeconds(s.workingHoursEnd), workingDays: s.workingDays, retentionMonths: s.retentionMonths }).subscribe({
      next: (saved) => {
        this.settings.set(saved);
        this.settingsSaved.set(true);
        this.busy.set(false);
      },
      error: (response: HttpErrorResponse) => this.fail(response),
    });
  }

  private request(): SaveMonitoringRule | null {
    const f = this.form();
    if (!f) {
      return null;
    }
    const definition = this.definition(f);
    if (!definition) {
      this.error.set(this.transloco.translate('monitoring.rules.bindAll'));
      return null;
    }
    return {
      name: f.name.trim(),
      description: f.description.trim() || null,
      kind: f.kind,
      templateCode: f.template?.code ?? null,
      definition,
      severity: f.severity,
      recurrenceKind: f.recurrenceKind,
      intervalMinutes: f.recurrenceKind === 'Interval' ? Number(f.intervalMinutes) : 0,
      timeOfDay: this.withSeconds(f.time || '06:00'),
      dayOfWeek: f.recurrenceKind === 'Weekly' ? f.dayOfWeek : null,
      cooldownHours: Number(f.cooldownHours),
      isActive: f.isActive,
      ownerUserId: this.isAdmin() ? f.ownerUserId : null,
      recipients: this.isAdmin() ? [...f.recipientUserIds.map((userId) => ({ userId })), ...f.recipientRoleIds.map((roleId) => ({ roleId }))] : [],
    };
  }

  /** Mirrors the server's template binding (RuleTemplates.BindData); the server validates everything again. */
  private definition(f: RuleForm): RuleDefinition | null {
    if (!f.template) {
      return f.customDefinition;
    }
    if (f.template.kind === 'Access') {
      const access = structuredClone(f.template.defaults.access!);
      access.windowHours = Number(f.windowHours);
      access.threshold = { ...access.threshold, value: Number(f.thresholdValue) };
      return { access };
    }
    const defaults = f.template.defaults.data!;
    const b = f.bindings;
    if (!f.objectId || f.template.slots.some((s) => !b[s.slot])) {
      return null;
    }
    const data = {
      objectId: f.objectId,
      timeColumnId: b['time'],
      windowHours: Number(f.windowHours),
      groupByColumnIds: [] as string[],
      measure: { aggregate: defaults.measure.aggregate, columnId: null as string | null },
      filters: [],
      condition: { operator: f.conditionOperator, value: Number(f.conditionValue) },
      comparison: defaults.comparison,
      maxGroups: 200,
    };
    switch (f.template.code) {
      case 'multi_device_same_account':
        data.groupByColumnIds = [b['account']];
        data.measure.columnId = b['device'];
        break;
      case 'many_events_per_entity':
        data.groupByColumnIds = [b['entity']];
        break;
      case 'amount_above_threshold':
        data.groupByColumnIds = [b['entity']];
        data.measure.columnId = b['amount'];
        break;
      case 'change_vs_previous_period':
        data.measure.columnId = b['measure'];
        break;
    }
    return { data };
  }

  private defaultBindings(candidate: TemplateCandidate): Record<string, string> {
    return Object.fromEntries(Object.entries(candidate.slots).map(([slot, options]) => [slot, options[0]?.columnId ?? '']));
  }

  private bindingsFrom(code: string, definition: RuleDefinition): Record<string, string> {
    const d = definition.data!;
    const bindings: Record<string, string> = {};
    if (d.timeColumnId) {
      bindings['time'] = d.timeColumnId;
    }
    if (code === 'multi_device_same_account') {
      bindings['account'] = d.groupByColumnIds[0];
      bindings['device'] = d.measure.columnId ?? '';
    } else if (code === 'many_events_per_entity') {
      bindings['entity'] = d.groupByColumnIds[0];
    } else if (code === 'amount_above_threshold') {
      bindings['entity'] = d.groupByColumnIds[0];
      bindings['amount'] = d.measure.columnId ?? '';
    } else if (code === 'change_vs_previous_period') {
      bindings['measure'] = d.measure.columnId ?? '';
    }
    return bindings;
  }

  private withSeconds(time: string): string {
    return time.length === 5 ? `${time}:00` : time;
  }

  private fail(response: HttpErrorResponse): void {
    this.error.set(problemMessage(response, this.transloco));
    this.busy.set(false);
    this.loading.set(false);
  }
}
