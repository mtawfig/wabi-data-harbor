import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { VerifiedAnswerDetail } from '../../../../core/verified-answers/verified-answers.models';
import { provideTestTransloco } from '../../../../testing/transloco-testing';
import { VerifiedAnswerDetailComponent } from './verified-answer-detail.component';

function detail(overrides: Partial<VerifiedAnswerDetail> = {}): VerifiedAnswerDetail {
  return {
    verifiedAnswerId: 'v1', applicationId: 'a1', applicationName: 'Support', applicationNameAr: 'الدعم', historyId: 'h1', versionNumber: 1,
    question: 'Tickets per status this year', language: 'en', askedByName: 'Khalid', status: 'Pending', latestVerdict: 'Correct',
    latestFeedbackAt: '2026-11-16T09:00:00Z', useCount: 0, resultHidden: true, feedback: [{ verdict: 'Correct', userName: 'Khalid', createdAt: '2026-11-16T09:00:00Z' }],
    plan: {
      objective: 'Tickets by status', assumptions: ['This year means since 1 January.'],
      queries: [{ title: '', objective: 'Tickets by status', assumptions: [], source: { schema: 'dbo', object: 'SupportTickets', alias: 't' }, select: [{ alias: 'status', column: 't.Status' }, { alias: 'tickets', column: '', aggregate: 'count' }], groupBy: ['status'] }],
    },
    ...overrides,
  } as VerifiedAnswerDetail;
}

describe('VerifiedAnswerDetailComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [VerifiedAnswerDetailComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTestTransloco(), provideRouter([]), { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'v1' }) } } }],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('explains how a hidden result was computed and approves with the note', () => {
    const fixture = TestBed.createComponent(VerifiedAnswerDetailComponent);
    fixture.detectChanges();
    http.expectOne('/api/verified-answers/v1').flush(detail());
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('[data-testid="verified-question"]')?.textContent).toContain('Tickets per status this year');
    expect(element.querySelector('[data-testid="verified-result-hidden"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="verified-how"]')?.textContent).toContain('This year means since 1 January.');
    expect(element.querySelector('[data-testid="verified-disable"]')).toBeNull();

    fixture.componentInstance.note.set('Checked');
    (element.querySelector('[data-testid="verified-approve"]') as HTMLButtonElement).click();
    const review = http.expectOne('/api/verified-answers/v1/review');
    expect(review.request.body).toEqual({ action: 'approve', note: 'Checked' });
    review.flush(detail({ status: 'Approved', reviewedByName: 'Faisal', reviewedAt: '2026-11-16T10:00:00Z' }));
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="verified-status"]')?.textContent).toContain('Approved');
    expect(element.querySelector('[data-testid="verified-done"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="verified-disable"]')).not.toBeNull();
  });

  it('shows why an answer was set aside, a flag after a later "not correct", and the limit message', () => {
    const fixture = TestBed.createComponent(VerifiedAnswerDetailComponent);
    fixture.detectChanges();
    http.expectOne('/api/verified-answers/v1').flush(detail({ status: 'SetAside', setAsideReason: 'dbo.SupportTickets.Impact' }));
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('[data-testid="verified-set-aside"]')?.textContent).toContain('dbo.SupportTickets.Impact');

    (element.querySelector('[data-testid="verified-enable"]') as HTMLButtonElement).click();
    http.expectOne('/api/verified-answers/v1/review').flush({ detail: 'verified_answer_limit_reached' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="verified-error"]')?.textContent).toContain('200 approved answers');
  });
});
