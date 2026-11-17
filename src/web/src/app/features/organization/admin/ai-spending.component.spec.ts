import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTestTransloco } from '../../../testing/transloco-testing';
import { AiSpendingComponent } from './ai-spending.component';

const report = {
  state: 'near', monthSpend: 8.5, from: '2026-11-01', to: '2026-11-17', totalRequests: 12, totalSpend: 8.5,
  rows: [], months: [], monthly: [], monthlyTotals: [], users: [], totalInputTokens: 0, totalOutputTokens: 0,
};

describe('AiSpendingComponent — plan figures shown or not (P13.57)', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AiSpendingComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTestTransloco()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  function render(body: object): HTMLElement {
    const fixture = TestBed.createComponent(AiSpendingComponent);
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/api/ai/spending/report').flush(body);
    fixture.detectChanges();
    return fixture.nativeElement;
  }

  it('not shown: no plan card, no limit and no share used; the usage amounts stay', () => {
    const element = render({ ...report, planShown: false });
    expect(element.querySelector('[data-testid="spend-plan"]')).toBeNull();
    expect(element.querySelector('[data-testid="spend-used"]')).toBeNull();
    expect(element.querySelector('mat-progress-bar[mode="determinate"]')).toBeNull();
    const summary = element.querySelector('[data-testid="spend-summary"]')!;
    expect(summary.children.length).toBe(3);
    expect(summary.textContent).not.toContain('Plan');
    expect(summary.textContent).not.toContain('limit');
    expect(element.querySelector('[data-testid="spend-total"]')!.textContent).toContain('8.50 USD');
  });

  it('shown: the plan card with its name, the limit and the share used', () => {
    const element = render({ ...report, planShown: true, planCode: 'STD', planNameEn: 'Standard', planNameAr: 'قياسية', monthlyAiSpendLimit: 10, percentUsed: 85 });
    expect(element.querySelector('[data-testid="spend-plan"]')!.textContent).toContain('Standard');
    expect(element.querySelector('[data-testid="spend-plan"]')!.textContent).toContain('10.00 USD');
    expect(element.querySelector('[data-testid="spend-used"]')!.textContent).toContain('85');
    expect(element.querySelector('[data-testid="spend-summary"]')!.children.length).toBe(4);
  });
});
