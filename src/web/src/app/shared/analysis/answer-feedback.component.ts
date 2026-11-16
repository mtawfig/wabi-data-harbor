import { HttpErrorResponse } from '@angular/common/http';
import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { problemMessage } from '../../core/problem-message';
import { AnswerVerdict } from '../../core/verified-answers/verified-answers.models';
import { VerifiedAnswersService } from '../../core/verified-answers/verified-answers.service';

/**
 * "Is this answer correct?" under the asker's own answer (P13.53). The verdict goes to the Organization Administrators'
 * review; it never changes how the platform answers by itself. A change of mind is recorded as a new verdict.
 */
@Component({
  selector: 'app-answer-feedback',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslocoModule],
  template: `
    <section class="feedback" data-testid="answer-feedback" [attr.aria-label]="'feedback.question' | transloco">
      @if (verdict() && !editing()) {
        <p class="feedback__done" data-testid="answer-feedback-done">
          <mat-icon>{{ verdict() === 'Correct' ? 'task_alt' : 'report' }}</mat-icon>
          <span>{{ (verdict() === 'Correct' ? 'feedback.thanksCorrect' : 'feedback.thanksNotCorrect') | transloco }}</span>
          <button mat-button type="button" (click)="editing.set(true)" data-testid="answer-feedback-change">{{ 'feedback.change' | transloco }}</button>
        </p>
      } @else {
        <div class="feedback__ask">
          <span class="feedback__label">{{ 'feedback.question' | transloco }}</span>
          <div class="feedback__buttons">
            <button mat-stroked-button type="button" [disabled]="saving()" (click)="send('Correct')" data-testid="answer-feedback-correct"><mat-icon>thumb_up</mat-icon>{{ 'feedback.correct' | transloco }}</button>
            <button mat-stroked-button type="button" [disabled]="saving()" [class.is-active]="reporting()" (click)="reporting.set(true)" data-testid="answer-feedback-not-correct"><mat-icon>thumb_down</mat-icon>{{ 'feedback.notCorrect' | transloco }}</button>
          </div>
        </div>
        @if (reporting()) {
          <div class="feedback__reason">
            <mat-form-field appearance="outline" subscriptSizing="dynamic">
              <mat-label>{{ 'feedback.reason' | transloco }}</mat-label>
              <textarea matInput rows="2" maxlength="500" [ngModel]="reason()" (ngModelChange)="reason.set($event)" data-testid="answer-feedback-reason"></textarea>
              <mat-hint>{{ 'feedback.reasonHint' | transloco }}</mat-hint>
            </mat-form-field>
            <button mat-flat-button color="primary" type="button" [disabled]="saving()" (click)="send('NotCorrect')" data-testid="answer-feedback-send">{{ 'feedback.send' | transloco }}</button>
          </div>
        }
        <p class="feedback__note">{{ 'feedback.note' | transloco }}</p>
      }
      @if (error(); as message) { <p class="feedback__error" role="alert">{{ message }}</p> }
    </section>
  `,
  styles: `
    .feedback { margin-top: 16px; padding: 12px 16px; border: 1px solid #e2e8f0; border-radius: var(--wi-radius-sm, 10px); background: #f8fafc; }
    .feedback__ask { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 16px; }
    .feedback__label { font-weight: 600; }
    .feedback__buttons { display: flex; flex-wrap: wrap; gap: 8px; }
    .feedback__buttons mat-icon { margin-inline-end: 6px; }
    .feedback__buttons .is-active { border-color: #b91c1c; color: #b91c1c; }
    .feedback__reason { display: flex; flex-wrap: wrap; gap: 12px; align-items: flex-start; margin-top: 12px; }
    .feedback__reason mat-form-field { flex: 1 1 320px; }
    .feedback__reason button { margin-top: 6px; }
    .feedback__note { margin: 8px 0 0; font-size: 12px; color: #64748b; }
    .feedback__done { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin: 0; }
    .feedback__done mat-icon { color: var(--wi-ocean, #087df2); }
    .feedback__error { margin: 8px 0 0; color: #b91c1c; font-size: 13px; }
  `,
})
export class AnswerFeedbackComponent {
  private readonly answers = inject(VerifiedAnswersService);
  private readonly transloco = inject(TranslocoService);

  readonly historyId = input.required<string>();
  readonly versionNumber = input.required<number>();

  readonly verdict = signal<AnswerVerdict | null>(null);
  readonly reason = signal('');
  readonly reporting = signal(false);
  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const historyId = this.historyId();
      const versionNumber = this.versionNumber();
      this.verdict.set(null);
      this.reporting.set(false);
      this.editing.set(false);
      this.answers.feedback(historyId, versionNumber).subscribe({
        next: (state) => {
          this.verdict.set(state.verdict ?? null);
          this.reason.set(state.reason ?? '');
        },
        error: () => this.verdict.set(null),
      });
    });
  }

  send(verdict: AnswerVerdict): void {
    this.saving.set(true);
    this.error.set(null);
    const reason = verdict === 'NotCorrect' ? this.reason().trim() || null : null;
    this.answers.giveFeedback(this.historyId(), this.versionNumber(), verdict, reason).subscribe({
      next: (state) => {
        this.verdict.set(state.verdict ?? verdict);
        this.reporting.set(false);
        this.editing.set(false);
        this.saving.set(false);
      },
      error: (failure: HttpErrorResponse) => {
        this.error.set(problemMessage(failure, this.transloco));
        this.saving.set(false);
      },
    });
  }
}
