import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { TranslocoModule } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { OrganizationDetail } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { AuditLogRow } from '../../../core/platform/platform.models';
import { PlatformService } from '../../../core/platform/platform.service';
import { WiDatePipe } from '../../../shared/format/locale-pipes';

@Component({
  selector: 'app-audit-logs',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatTableModule, TranslocoModule, WiDatePipe],
  templateUrl: './audit-logs.component.html',
  styles: `
    .filters { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; margin-bottom: 12px; mat-form-field { min-width: 180px; } .search { min-width: 320px; } }
    table { width: 100%; }
    .details { font-size: 12px; max-width: 420px; white-space: pre-wrap; word-break: break-word; direction: ltr; text-align: left; display: block; }
    .actor { font-size: 12px; color: var(--wi-slate); }
    .reduced { display: block; max-width: 420px; margin-top: 4px; font-size: 12px; color: var(--wi-slate); mat-icon { font-size: 14px; width: 14px; height: 14px; vertical-align: -2px; margin-inline-end: 4px; overflow: visible; } }
  `,
})
export class AuditLogsComponent {
  private readonly platform = inject(PlatformService);
  private readonly organizations = inject(OrganizationsService);
  readonly language = inject(LanguageService);

  readonly columns = ['createdAt', 'eventType', 'actor', 'organization', 'entity', 'details'];
  readonly items = signal<AuditLogRow[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(50);
  readonly loading = signal(false);
  readonly eventTypes = signal<string[]>([]);
  readonly organizationOptions = signal<OrganizationDetail[]>([]);
  readonly eventType = signal<string | null>(null);
  readonly organizationId = signal<string | null>(null);
  readonly actorType = signal<string | null>(null);
  readonly search = signal('');
  readonly from = signal('');
  readonly to = signal('');
  readonly organizationNames = computed(() => new Map(this.organizationOptions().map((o) => [o.organizationId, this.language.current() === 'ar' ? o.organizationNameAr : o.organizationName])));

  constructor() {
    this.platform.auditEventTypes().subscribe((t) => this.eventTypes.set(t));
    this.organizations.list(1, 200).subscribe((r) => this.organizationOptions.set(r.items));
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.platform.audit({
      page: this.page(), pageSize: this.pageSize(), eventType: this.eventType(), organizationId: this.organizationId(), actorType: this.actorType(), search: this.search() || undefined,
      from: this.from() ? new Date(this.from()).toISOString() : undefined, to: this.to() ? new Date(new Date(this.to()).getTime() + 86399999).toISOString() : undefined,
    }).subscribe({ next: (r) => { this.items.set(r.items); this.total.set(r.totalCount); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  apply(): void {
    this.page.set(1);
    this.load();
  }

  onPage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  organizationLabel(id?: string): string {
    return (id && this.organizationNames().get(id)) || (id ? id.substring(0, 8) : '—');
  }

  pretty(json?: string): string {
    if (!json) {
      return '';
    }
    try {
      return JSON.stringify(JSON.parse(json), null, 1);
    } catch {
      return json;
    }
  }
}
