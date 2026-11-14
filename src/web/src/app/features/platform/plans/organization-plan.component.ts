import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { OrganizationPlan, PlansService, ServicePlan } from '../../../core/plans/plans.service';
import { problemMessage } from '../../../core/problem-message';
import { WiNumberPipe } from '../../../shared/format/locale-pipes';

/** An organization's plan on its platform page: choose the plan, see the limit and the month's AI spending (amounts only). */
@Component({
  selector: 'app-organization-plan',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatProgressBarModule, MatSelectModule, TranslocoModule, WiNumberPipe],
  template: `
    <div class="wi-card" data-testid="org-plan">
      <h3>{{ 'plans.organization.title' | transloco }}</h3>
      @if (error(); as e) { <p class="plan__error" role="alert">{{ e }}</p> }
      <mat-form-field appearance="outline" class="wi-full-width" subscriptSizing="dynamic">
        <mat-label>{{ 'plans.organization.plan' | transloco }}</mat-label>
        <mat-select [value]="current()?.planId ?? null" (valueChange)="assign($event)" [disabled]="busy()" data-testid="org-plan-select">
          <mat-option [value]="null">{{ 'plans.organization.none' | transloco }}</mat-option>
          @for (p of choices(); track p.planId) {
            <mat-option [value]="p.planId">{{ name(p) }} @if (p.monthlyAiSpendLimit; as limit) { · {{ limit | wiNumber: '1.2-2' }} USD } @else { · {{ 'plans.noLimit' | transloco }} }</mat-option>
          }
        </mat-select>
      </mat-form-field>
      @if (current(); as c) {
        <dl class="plan__facts">
          <dt>{{ 'plans.organization.spend' | transloco }}</dt>
          <dd><span class="wi-ltr">{{ c.monthSpend | wiNumber: '1.2-2' }} USD</span></dd>
          <dt>{{ 'plans.limit' | transloco }}</dt>
          <dd>@if (c.monthlyAiSpendLimit; as limit) { <span class="wi-ltr">{{ limit | wiNumber: '1.2-2' }} USD</span> } @else { {{ 'plans.noLimit' | transloco }} }</dd>
        </dl>
        @if (c.monthlyAiSpendLimit) {
          <mat-progress-bar mode="determinate" [value]="percent()" [class.plan__bar--near]="c.state === 'near'" [class.plan__bar--reached]="c.state === 'reached'" />
        }
        <p class="plan__state" [attr.data-state]="c.state" data-testid="org-plan-state">{{ 'plans.states.' + c.state | transloco }}</p>
      }
      <p class="wi-muted plan__note">{{ 'plans.organization.note' | transloco }}</p>
    </div>
  `,
  styles: `
    :host { display: block; }
    .plan__facts { display: grid; grid-template-columns: max-content 1fr; gap: 6px 16px; margin: 12px 0; font-size: 14px; dt { color: var(--wi-slate); } dd { margin: 0; font-weight: 600; } }
    .plan__state { margin: 10px 0 0; font-size: 14px; font-weight: 600; color: #166534; &[data-state='near'] { color: #92400e; } &[data-state='reached'] { color: #991b1b; } &[data-state='unlimited'] { color: var(--wi-slate); font-weight: 400; } }
    .plan__bar--near { --mat-progress-bar-active-indicator-color: #f59e0b; }
    .plan__bar--reached { --mat-progress-bar-active-indicator-color: #dc2626; }
    .plan__note { font-size: 12px; margin: 10px 0 0; }
    .plan__error { color: #991b1b; }
  `,
})
export class OrganizationPlanComponent {
  private readonly plans = inject(PlansService);
  private readonly transloco = inject(TranslocoService);
  private readonly language = inject(LanguageService);

  readonly organizationId = input.required<string>();
  readonly all = signal<ServicePlan[]>([]);
  readonly current = signal<OrganizationPlan | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  /** Active plans, plus the organization's own plan when it has been retired. */
  readonly choices = computed(() => this.all().filter((p) => p.isActive || p.planId === this.current()?.planId));
  readonly percent = computed(() => {
    const c = this.current();
    return c?.monthlyAiSpendLimit ? Math.min(100, (c.monthSpend / c.monthlyAiSpendLimit) * 100) : 0;
  });

  constructor() {
    this.plans.list().subscribe((items) => this.all.set(items));
    effect(() => {
      this.plans.organizationPlan(this.organizationId()).subscribe((plan) => this.current.set(plan));
    });
  }

  name(plan: ServicePlan): string {
    return this.language.current() === 'ar' ? plan.nameAr : plan.nameEn;
  }

  assign(planId: string | null): void {
    if (this.busy() || planId === (this.current()?.planId ?? null)) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.plans.assign(this.organizationId(), planId).subscribe({
      next: (plan) => {
        this.current.set(plan);
        this.busy.set(false);
      },
      error: (response: HttpErrorResponse) => {
        this.error.set(problemMessage(response, this.transloco));
        this.busy.set(false);
      },
    });
  }
}
