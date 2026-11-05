import { Component, computed, effect, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule } from '@jsverse/transloco';
import { AI_PROVIDER_TYPES, AiEffort, AiProvider, AiProviderType, SaveAiProviderRequest } from '../../../core/platform/ai.models';

/** Shared create/edit form for a provider configuration. The key field is write-only: blank keeps the stored key. */
@Component({
  selector: 'app-ai-provider-form',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslocoModule],
  template: `
    <form class="wi-card catalog-form" [formGroup]="form" (ngSubmit)="submit()" novalidate data-testid="ai-provider-form">
      <h3>{{ (initial() ? 'aiProviders.edit' : 'aiProviders.new') | transloco }}</h3>
      <div class="catalog-form__grid">
        <mat-form-field appearance="outline">
          <mat-label>{{ 'aiProviders.fields.type' | transloco }}</mat-label>
          <mat-select formControlName="providerType" data-testid="ai-type">@for (t of types(); track t) { <mat-option [value]="t">{{ 'aiProviders.types.' + t | transloco }}</mat-option> }</mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline"><mat-label>{{ 'aiProviders.fields.name' | transloco }}</mat-label><input matInput formControlName="name" data-testid="ai-name" /></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>{{ 'aiProviders.fields.model' | transloco }}</mat-label><input matInput formControlName="model" data-testid="ai-model" /><mat-hint>{{ 'aiProviders.modelHint.' + form.controls.providerType.value | transloco }}</mat-hint></mat-form-field>
        <mat-form-field appearance="outline" style="grid-column: 1 / span 2"><mat-label>{{ 'aiProviders.fields.endpoint' | transloco }}</mat-label><input matInput formControlName="endpoint" placeholder="https://" data-testid="ai-endpoint" /><mat-hint>{{ 'aiProviders.endpointHint' | transloco }}</mat-hint></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>{{ 'aiProviders.fields.apiKey' | transloco }}</mat-label><input matInput type="password" autocomplete="off" formControlName="apiKey" data-testid="ai-key" /><mat-hint>{{ (initial()?.apiKeyConfigured ? 'aiProviders.keyKeepHint' : 'aiProviders.keyHint') | transloco: { hint: initial()?.apiKeyHint ?? '' } }}</mat-hint></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>{{ 'aiProviders.fields.inputPrice' | transloco }}</mat-label><input matInput type="number" step="0.01" formControlName="inputPricePerMillion" /></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>{{ 'aiProviders.fields.outputPrice' | transloco }}</mat-label><input matInput type="number" step="0.01" formControlName="outputPricePerMillion" /></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>{{ 'aiProviders.fields.cachedPrice' | transloco }}</mat-label><input matInput type="number" step="0.01" formControlName="cachedInputPricePerMillion" /></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>{{ 'aiProviders.fields.maxOutputTokens' | transloco }}</mat-label><input matInput type="number" formControlName="maxOutputTokens" /></mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>{{ 'aiProviders.fields.effort' | transloco }}</mat-label>
          <mat-select formControlName="effort"><mat-option [value]="null">—</mat-option><mat-option value="low">{{ 'aiProviders.efforts.low' | transloco }}</mat-option><mat-option value="medium">{{ 'aiProviders.efforts.medium' | transloco }}</mat-option><mat-option value="high">{{ 'aiProviders.efforts.high' | transloco }}</mat-option></mat-select>
        </mat-form-field>
        <div class="checks">
          <mat-checkbox formControlName="isEnabled">{{ 'admin.fields.active' | transloco }}</mat-checkbox>
          @if (mode() === 'platform') { <mat-checkbox formControlName="isApproved" data-testid="ai-approved">{{ 'aiProviders.fields.approved' | transloco }}</mat-checkbox> }
        </div>
      </div>
      <div class="catalog-form__actions">
        <button mat-button type="button" (click)="cancelled.emit()">{{ 'common.cancel' | transloco }}</button>
        <button mat-flat-button type="submit" [disabled]="busy()" data-testid="ai-save">{{ 'common.save' | transloco }}</button>
      </div>
    </form>
  `,
  styles: ['.checks { display: flex; flex-direction: column; gap: 4px; padding-bottom: 16px; }'],
  styleUrl: '../../organization/admin/admin-catalog.scss',
})
export class AiProviderFormComponent {
  private readonly fb = inject(FormBuilder);

  readonly mode = input<'platform' | 'organization'>('platform');
  readonly allowedTypes = input<AiProviderType[]>(AI_PROVIDER_TYPES);
  readonly initial = input<AiProvider | null>(null);
  readonly busy = input(false);
  readonly saved = output<SaveAiProviderRequest>();
  readonly cancelled = output<void>();

  readonly types = computed(() => (this.mode() === 'platform' ? AI_PROVIDER_TYPES : this.allowedTypes()));

  readonly form = this.fb.nonNullable.group({
    providerType: ['Anthropic' as AiProviderType, Validators.required],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    model: ['claude-opus-5-5', [Validators.required, Validators.maxLength(200)]],
    endpoint: [''],
    apiKey: [''],
    inputPricePerMillion: [4],
    outputPricePerMillion: [20],
    cachedInputPricePerMillion: [0.2],
    maxOutputTokens: [4096, [Validators.min(256), Validators.max(65536)]],
    effort: ['medium' as AiEffort | null],
    isEnabled: [true],
    isApproved: [false],
  });

  constructor() {
    effect(() => {
      const p = this.initial();
      if (p) {
        this.form.reset({ providerType: p.providerType, name: p.name, model: p.model, endpoint: p.endpoint ?? '', apiKey: '', inputPricePerMillion: p.inputPricePerMillion, outputPricePerMillion: p.outputPricePerMillion, cachedInputPricePerMillion: p.cachedInputPricePerMillion, maxOutputTokens: p.maxOutputTokens, effort: p.effort ?? null, isEnabled: p.isEnabled, isApproved: p.isApproved });
      } else {
        const type = this.types()[0] ?? 'Anthropic';
        this.form.reset({ providerType: type, name: '', model: type === 'Anthropic' ? 'claude-opus-5-5' : '', endpoint: '', apiKey: '', inputPricePerMillion: type === 'Anthropic' ? 4 : 0, outputPricePerMillion: type === 'Anthropic' ? 20 : 0, cachedInputPricePerMillion: type === 'Anthropic' ? 0.2 : 0, maxOutputTokens: 4096, effort: 'medium', isEnabled: true, isApproved: false });
      }
    });
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    this.saved.emit({ ...v, endpoint: v.endpoint || null, apiKey: v.apiKey || null, effort: v.effort || null });
  }
}
