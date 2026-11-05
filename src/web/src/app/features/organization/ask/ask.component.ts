import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * "Ask Wabi Insight" workspace, first version (P1.4): application selector, answer language, prompt and
 * example questions. Submitting shows an honest notice: the secure AI engine is delivered in Phase 8 and
 * nothing is sent anywhere until then. P9.1 turns this into the full workspace with results and charts.
 */
@Component({
  selector: 'app-ask',
  imports: [FormsModule, MatButtonModule, MatChipsModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, TranslocoModule],
  templateUrl: './ask.component.html',
  styleUrl: './ask.component.scss',
})
export class AskComponent {
  readonly auth = inject(AuthService);
  private readonly transloco = inject(TranslocoService);

  readonly applications: { id: string; name: string }[] = [];
  readonly selectedApplication = signal<string | null>(null);
  readonly outputLanguage = signal<'same' | 'en' | 'ar'>('same');
  readonly prompt = signal('');
  readonly submitted = signal(false);
  readonly examples = ['e1', 'e2', 'e3'];

  useExample(key: string): void {
    this.prompt.set(this.transloco.translate(`ask.examples.${key}`));
    this.submitted.set(false);
  }

  submit(): void {
    if (!this.prompt().trim()) {
      return;
    }
    this.submitted.set(true);
  }
}
