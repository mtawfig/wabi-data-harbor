import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { AI_PROVIDER_TYPES, AiProvider, AiProviderType, SaveAiProviderRequest } from '../../../core/platform/ai.models';
import { AiService } from '../../../core/platform/ai.service';
import { problemMessage } from '../../../core/problem-message';
import { AiProviderFormComponent } from './ai-provider-form.component';

/** Platform AI providers (specification §18): approved endpoints, the platform default and the allow-list of types organizations may configure. */
@Component({
  selector: 'app-ai-providers',
  imports: [DatePipe, MatButtonModule, MatCheckboxModule, MatIconModule, MatProgressBarModule, TranslocoModule, AiProviderFormComponent],
  templateUrl: './ai-providers.component.html',
  styleUrl: './ai-providers.component.scss',
})
export class AiProvidersComponent {
  private readonly ai = inject(AiService);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly allTypes = AI_PROVIDER_TYPES;
  readonly items = signal<AiProvider[]>([]);
  readonly allowed = signal<AiProviderType[]>([]);
  readonly editing = signal<AiProvider | null>(null);
  readonly showForm = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly testResults = signal<Record<string, string>>({});

  constructor() {
    this.load();
  }

  load(): void {
    this.ai.platformProviders().subscribe({ next: (items) => this.items.set(items), error: (e: HttpErrorResponse) => this.fail(e) });
    this.ai.allowedTypes().subscribe((types) => this.allowed.set(types));
  }

  startCreate(): void {
    this.editing.set(null);
    this.showForm.set(true);
    this.error.set(null);
  }

  startEdit(p: AiProvider): void {
    this.editing.set(p);
    this.showForm.set(true);
    this.error.set(null);
  }

  save(body: SaveAiProviderRequest): void {
    const editing = this.editing();
    this.busy.set(true);
    (editing ? this.ai.updatePlatformProvider(editing.providerId, body) : this.ai.createPlatformProvider(body)).subscribe({
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
    this.ai.testPlatformProvider(p.providerId).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.testResults.update((m) => ({ ...m, [p.providerId]: r.succeeded ? this.transloco.translate('aiProviders.testOk', { model: r.model, ms: Math.round(r.durationMs) }) : this.transloco.translate('admin.errors.' + (r.error ?? 'unknown')) }));
        this.load();
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  setDefault(p: AiProvider): void {
    this.busy.set(true);
    this.ai.setDefault(p.providerId).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: (e: HttpErrorResponse) => this.fail(e) });
  }

  remove(p: AiProvider): void {
    this.busy.set(true);
    this.ai.deletePlatformProvider(p.providerId).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: (e: HttpErrorResponse) => this.fail(e) });
  }

  toggleAllowed(type: AiProviderType, checked: boolean): void {
    const next = checked ? [...new Set([...this.allowed(), type])] : this.allowed().filter((t) => t !== type);
    this.ai.setAllowedTypes(next).subscribe({ next: (types) => this.allowed.set(types), error: (e: HttpErrorResponse) => this.fail(e) });
  }

  private fail(e: HttpErrorResponse): void {
    this.error.set(problemMessage(e, this.transloco));
    this.busy.set(false);
  }
}
