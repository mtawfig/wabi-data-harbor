import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { AiSpendingFilter, AiSpendingReport, AiSpendingUserRow, PlansService } from '../../../core/plans/plans.service';
import { problemMessage } from '../../../core/problem-message';
import { WiDatePipe, WiNumberPipe } from '../../../shared/format/locale-pipes';

/**
 * AI Spending (P13.47), for Organization Administrators: the plan's position this month, then what each employee's
 * AI requests cost in a period, and the same month by month. Amounts and counts only; the server decides the
 * organization from the signed-in administrator.
 */
@Component({
  selector: 'app-ai-spending',
  imports: [FormsModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatSlideToggleModule, TranslocoModule, WiDatePipe, WiNumberPipe],
  templateUrl: './ai-spending.component.html',
  styleUrl: './ai-spending.component.scss',
})
export class AiSpendingComponent {
  private readonly plans = inject(PlansService);
  private readonly transloco = inject(TranslocoService);
  private readonly language = inject(LanguageService);

  readonly report = signal<AiSpendingReport | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly view = signal<'employee' | 'month'>('employee');

  // The filters as typed; they are sent when Apply is pressed.
  readonly from = signal('');
  readonly to = signal('');
  readonly userId = signal<string | null>(null);
  readonly minSpend = signal<number | null>(null);
  readonly maxSpend = signal<number | null>(null);
  readonly successfulOnly = signal(false);

  readonly planName = computed(() => {
    const r = this.report();
    return r ? (this.language.current() === 'ar' ? r.planNameAr : r.planNameEn) ?? null : null;
  });
  readonly percent = computed(() => Math.min(100, this.report()?.percentUsed ?? 0));
  readonly monthlyTotal = computed(() => (this.report()?.monthlyTotals ?? []).reduce((sum, value) => sum + value, 0));

  constructor() {
    this.load();
  }

  department(row: AiSpendingUserRow): string {
    return (this.language.current() === 'ar' ? row.departmentNameAr : row.departmentNameEn) ?? '—';
  }

  /** A day ("2026-11-14") as local midnight, so it is never shown as the day before. */
  dayDate(day: string): string {
    return `${day}T00:00:00`;
  }

  /** "2026-11" as a date the month can be formatted from. */
  monthDate(month: string): string {
    return `${month}-01T00:00:00`;
  }

  apply(): void {
    this.load();
  }

  reset(): void {
    this.from.set('');
    this.to.set('');
    this.userId.set(null);
    this.minSpend.set(null);
    this.maxSpend.set(null);
    this.successfulOnly.set(false);
    this.load();
  }

  private load(): void {
    const filter: AiSpendingFilter = { from: this.from() || null, to: this.to() || null, userId: this.userId(), minSpend: this.minSpend(), maxSpend: this.maxSpend(), successfulOnly: this.successfulOnly() };
    this.busy.set(true);
    this.error.set(null);
    this.plans.report(filter).subscribe({
      next: (report) => {
        this.report.set(report);
        this.busy.set(false);
      },
      error: (response: HttpErrorResponse) => {
        this.error.set(problemMessage(response, this.transloco));
        this.busy.set(false);
      },
    });
  }
}
