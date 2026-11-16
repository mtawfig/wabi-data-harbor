import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTestTransloco } from '../../testing/transloco-testing';
import { AnswerFeedbackComponent } from './answer-feedback.component';

describe('AnswerFeedbackComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AnswerFeedbackComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTestTransloco()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function create() {
    const fixture = TestBed.createComponent(AnswerFeedbackComponent);
    fixture.componentRef.setInput('historyId', 'h1');
    fixture.componentRef.setInput('versionNumber', 2);
    fixture.detectChanges();
    return fixture;
  }

  it('asks the question when no verdict was given, and records "correct" with one click', () => {
    const fixture = create();
    http.expectOne('/api/history/h1/versions/2/feedback').flush({ historyId: 'h1', versionNumber: 2 });
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('Is this answer correct?');

    (element.querySelector('[data-testid="answer-feedback-correct"]') as HTMLButtonElement).click();
    const post = http.expectOne((r) => r.method === 'POST' && r.url === '/api/history/h1/versions/2/feedback');
    expect(post.request.body).toEqual({ verdict: 'Correct', reason: null });
    post.flush({ historyId: 'h1', versionNumber: 2, verdict: 'Correct', at: '2026-11-16T10:00:00Z' });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="answer-feedback-done"]')?.textContent).toContain('You marked this answer as correct.');
  });

  it('asks for an optional reason before reporting "not correct", and shows an earlier verdict that can be changed', () => {
    const fixture = create();
    http.expectOne('/api/history/h1/versions/2/feedback').flush({ historyId: 'h1', versionNumber: 2, verdict: 'Correct' });
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('[data-testid="answer-feedback-correct"]')).toBeNull();

    (element.querySelector('[data-testid="answer-feedback-change"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (element.querySelector('[data-testid="answer-feedback-not-correct"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    fixture.componentInstance.reason.set('  The totals miss December  ');
    (element.querySelector('[data-testid="answer-feedback-send"]') as HTMLButtonElement).click();
    const post = http.expectOne((r) => r.method === 'POST');
    expect(post.request.body).toEqual({ verdict: 'NotCorrect', reason: 'The totals miss December' });
    post.flush({ historyId: 'h1', versionNumber: 2, verdict: 'NotCorrect', reason: 'The totals miss December' });
    fixture.detectChanges();
    expect(element.textContent).toContain('You reported this answer as not correct.');
  });
});
