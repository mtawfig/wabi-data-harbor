import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { AiProvider, OrganizationAiSettings, SaveAiProviderRequest } from '../../../core/platform/ai.models';
import { AiService } from '../../../core/platform/ai.service';
import { problemMessage } from '../../../core/problem-message';
import { AiProviderFormComponent } from '../../platform/ai-providers/ai-provider-form.component';

/** Organization AI provider (specification §18): the platform default, an approved platform provider, or the organization's own key on an allow-listed type. */
@Component({
  selector: 'app-organization-ai-settings',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatProgressBarModule, MatSelectModule, TranslocoModule, AiProviderFormComponent],
  template: `
    <div class="wi-card" data-testid="org-ai">
      <h3>{{ 'aiProviders.orgTitle' | transloco }}</h3>
      <p class="wi-muted small">{{ 'aiProviders.orgHint' | transloco }}</p>
      @if (error(); as e) { <div class="catalog-form__error" role="alert"><mat-icon>error_outline</mat-icon><span>{{ e }}</span></div> }
      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      @if (settings(); as s) {
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="select">
          <mat-label>{{ 'aiProviders.selected' | transloco }}</mat-label>
          <mat-select [value]="s.selectedProviderId ?? null" (valueChange)="select($event)" data-testid="org-ai-select">
            <mat-option [value]="null">{{ 'aiProviders.platformDefault' | transloco }}</mat-option>
            @for (p of s.platformProviders; track p.providerId) { <mat-option [value]="p.providerId">{{ p.name }} <span class="wi-muted wi-ltr">({{ p.model }})</span></mat-option> }
            @for (p of s.ownProviders; track p.providerId) { <mat-option [value]="p.providerId">{{ 'aiProviders.own' | transloco }}: {{ p.name }} <span class="wi-muted wi-ltr">({{ p.model }})</span></mat-option> }
          </mat-select>
        </mat-form-field>

        <div class="own-head"><h4>{{ 'aiProviders.ownProviders' | transloco }}</h4>@if (s.allowedTypes.length > 0) { <button mat-stroked-button type="button" (click)="startCreate()" data-testid="org-ai-new"><mat-icon>add</mat-icon>{{ 'aiProviders.new' | transloco }}</button> }</div>
        @if (s.allowedTypes.length === 0) { <p class="wi-muted small">{{ 'aiProviders.noneAllowed' | transloco }}</p> }
        @if (showForm()) { <app-ai-provider-form mode="organization" [allowedTypes]="s.allowedTypes" [initial]="editing()" [busy]="busy()" (saved)="save($event)" (cancelled)="showForm.set(false)" /> }
        <ul class="own">
          @for (p of s.ownProviders; track p.providerId) {
            <li [attr.data-testid]="'org-ai-' + p.name">
              <div><strong>{{ p.name }}</strong> <span class="wi-muted small wi-ltr">{{ 'aiProviders.types.' + p.providerType | transloco }} · {{ p.model }} · {{ p.apiKeyConfigured ? ('aiProviders.keyConfigured' | transloco: { hint: p.apiKeyHint }) : ('aiProviders.keyMissing' | transloco) }}</span>@if (testResults()[p.providerId]; as r) { <span class="small result">{{ r }}</span> }</div>
              <div class="actions"><button mat-button type="button" (click)="test(p)" [disabled]="busy()">{{ 'aiProviders.test' | transloco }}</button><button mat-icon-button type="button" (click)="startEdit(p)" [attr.aria-label]="'aiProviders.edit' | transloco"><mat-icon>edit</mat-icon></button><button mat-icon-button type="button" (click)="remove(p)" [disabled]="busy()" [attr.aria-label]="'aiProviders.delete' | transloco"><mat-icon>delete</mat-icon></button></div>
            </li>
          }
        </ul>
      }
    </div>
  `,
  styles: [
    '.small { font-size: 12px; } .select { width: 100%; max-width: 520px; margin: 8px 0 16px; } .own-head { display: flex; justify-content: space-between; align-items: center; gap: 8px; h4 { margin: 0; font-size: 14px; } } .own { list-style: none; margin: 8px 0 0; padding: 0; li { display: flex; justify-content: space-between; align-items: center; gap: 12px; padding: 8px 0; border-top: 1px solid var(--wi-border); } .actions { display: flex; align-items: center; } .result { display: block; color: #1e3a8a; } }',
  ],
  styleUrl: '../admin/admin-catalog.scss',
})
export class OrganizationAiSettingsComponent {
  private readonly ai = inject(AiService);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly settings = signal<OrganizationAiSettings | null>(null);
  readonly editing = signal<AiProvider | null>(null);
  readonly showForm = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly testResults = signal<Record<string, string>>({});

  constructor() {
    this.load();
  }

  load(): void {
    this.ai.organizationSettings().subscribe({ next: (s) => this.settings.set(s), error: (e: HttpErrorResponse) => this.fail(e) });
  }

  select(providerId: string | null): void {
    this.busy.set(true);
    this.ai.selectProvider(providerId).subscribe({
      next: (s) => {
        this.settings.set(s);
        this.busy.set(false);
        this.snack.open(this.transloco.translate('aiProviders.selectionSaved'), undefined, { duration: 2500 });
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  startCreate(): void {
    this.editing.set(null);
    this.showForm.set(true);
  }

  startEdit(p: AiProvider): void {
    this.editing.set(p);
    this.showForm.set(true);
  }

  save(body: SaveAiProviderRequest): void {
    const editing = this.editing();
    this.busy.set(true);
    (editing ? this.ai.updateOrganizationProvider(editing.providerId, body) : this.ai.createOrganizationProvider(body)).subscribe({
      next: () => {
        this.busy.set(false);
        this.showForm.set(false);
        this.snack.open(this.transloco.translate('aiProviders.saved'), undefined, { duration: 2500 });
        this.load();
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  test(p: AiProvider): void {
    this.busy.set(true);
    this.ai.testOrganizationProvider(p.providerId).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.testResults.update((m) => ({ ...m, [p.providerId]: r.succeeded ? this.transloco.translate('aiProviders.testOk', { model: r.model, ms: Math.round(r.durationMs) }) : this.transloco.translate('admin.errors.' + (r.error ?? 'unknown')) }));
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  remove(p: AiProvider): void {
    this.busy.set(true);
    this.ai.deleteOrganizationProvider(p.providerId).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: (e: HttpErrorResponse) => this.fail(e) });
  }

  private fail(e: HttpErrorResponse): void {
    this.error.set(problemMessage(e, this.transloco));
    this.busy.set(false);
  }
}
