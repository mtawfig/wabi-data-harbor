import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTestTransloco } from '../../../testing/transloco-testing';
import { AskComponent } from './ask.component';

const application = {
  applicationId: 'app1', applicationCode: 'HR', applicationName: 'HR and Payroll', applicationNameAr: 'الموارد البشرية', databaseType: 'SqlServer', isBuiltIn: false, objectCount: 4,
  exampleQuestions: [{ en: 'Payroll cost by department', ar: 'تكلفة الرواتب حسب الإدارة' }],
};
const asking = {
  analysisId: 'a0', versionNumber: 0, objective: '', assumptions: [], panels: [],
  clarification: { question: 'What should the email cover about the employees?', options: ['Headcount by department', 'Payroll cost'] },
};
const answer = { analysisId: 'a1', versionNumber: 0, objective: 'Headcount', assumptions: [], panels: [] };

describe('AskComponent — clarifying question (P13.54)', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<AskComponent>;
  let element: HTMLElement;

  const byId = <T extends HTMLElement>(id: string) => element.querySelector<T>(`[data-testid="${id}"]`);

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AskComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTestTransloco(), provideRouter([])],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AskComponent);
    element = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/ai/applications').flush([application]);
    http.expectOne('/api/ai/spending').flush({ state: 'ok' });
    fixture.componentInstance.onPrompt('write email to manager about employees');
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  function ask(): void {
    byId<HTMLButtonElement>('ask-submit')!.click();
    const request = http.expectOne('/api/ai/ask');
    expect(request.request.body).toEqual({ applicationId: 'app1', question: 'write email to manager about employees', language: 'en', interpret: true });
    request.flush(asking);
    fixture.detectChanges();
  }

  it('puts the question where the sample questions were, hides Ask, and goes on with the chosen answer', () => {
    expect(byId('ask-examples')).not.toBeNull();
    ask();

    expect(byId('ask-clarify-question')!.textContent).toContain('What should the email cover about the employees?');
    expect(byId('ask-examples')).toBeNull();
    expect(byId('ask-submit')).toBeNull();

    byId<HTMLButtonElement>('ask-clarify-option-0')!.click();
    const followUp = http.expectOne('/api/ai/ask');
    expect(followUp.request.body).toEqual({ applicationId: 'app1', question: 'write email to manager about employees', language: 'en', interpret: true, clarification: 'Headcount by department' });
    fixture.detectChanges();
    // While the answer is prepared: the question is gone, the samples are back and the choice is stated.
    expect(byId('ask-clarify')).toBeNull();
    expect(byId('ask-examples')).not.toBeNull();
    expect(byId('ask-clarified')!.textContent).toContain('Headcount by department');
    followUp.flush(answer);
    fixture.detectChanges();
    expect(fixture.componentInstance.answer()?.analysisId).toBe('a1');

    // "Change" brings the same choices back without asking the model again.
    byId<HTMLButtonElement>('ask-clarified-change')!.click();
    fixture.detectChanges();
    expect(byId('ask-clarify-option-1')!.textContent).toContain('Payroll cost');
    expect(byId('ask-clarified')).toBeNull();
  });

  it('takes the asker’s own words, "Answer anyway" and Cancel, and starts over when the request is edited', () => {
    ask();
    expect(byId<HTMLButtonElement>('ask-clarify-continue')!.disabled).toBe(true);
    fixture.componentInstance.ownAnswer.set('  overtime in Ground Handling  ');
    fixture.detectChanges();
    byId<HTMLButtonElement>('ask-clarify-continue')!.click();
    const own = http.expectOne('/api/ai/ask');
    expect(own.request.body.clarification).toBe('overtime in Ground Handling');
    own.flush(asking); // a model that asks again is refused by the server; the page simply shows what it gets
    fixture.detectChanges();

    byId<HTMLButtonElement>('ask-clarify-skip')!.click();
    const skipped = http.expectOne('/api/ai/ask');
    expect(skipped.request.body).toEqual({ applicationId: 'app1', question: 'write email to manager about employees', language: 'en', interpret: true, skipClarification: true });
    skipped.flush(answer);
    fixture.detectChanges();
    expect(byId('ask-clarified')).toBeNull();

    ask();
    byId<HTMLButtonElement>('ask-clarify-cancel')!.click();
    fixture.detectChanges();
    expect(byId('ask-clarify')).toBeNull();
    expect(byId('ask-submit')).not.toBeNull();

    ask();
    fixture.componentInstance.onPrompt('headcount by department this year');
    fixture.detectChanges();
    expect(byId('ask-clarify')).toBeNull();
    expect(byId('ask-examples')).not.toBeNull();
  });
});
