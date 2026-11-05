import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { AiService } from './ai.service';

describe('AiService', () => {
  let service: AiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends API keys only on save and never requests them back', () => {
    service.createPlatformProvider({ providerType: 'Anthropic', name: 'A', model: 'claude-opus-5-5', apiKey: 'sk-x', inputPricePerMillion: 4, outputPricePerMillion: 20, cachedInputPricePerMillion: 0.2, maxOutputTokens: 4096, effort: 'medium', isEnabled: true, isApproved: true }).subscribe();
    const create = http.expectOne({ method: 'POST', url: '/api/platform/ai-providers' });
    expect(create.request.body.apiKey).toBe('sk-x');
    create.flush({});

    service.platformProviders().subscribe();
    const list = http.expectOne({ method: 'GET', url: '/api/platform/ai-providers' });
    expect(list.request.body).toBeNull();
    list.flush([]);

    service.selectProvider(null).subscribe();
    const select = http.expectOne({ method: 'PUT', url: '/api/ai-providers/selection' });
    expect(select.request.body).toEqual({ providerId: null });
    select.flush({});
  });

  it('plans with the application id, question and language only', () => {
    service.plan('app1', 'How many tickets?', 'ar').subscribe();
    const plan = http.expectOne({ method: 'POST', url: '/api/ai/plan' });
    expect(plan.request.body).toEqual({ applicationId: 'app1', question: 'How many tickets?', language: 'ar' });
    expect(JSON.stringify(plan.request.body)).not.toContain('organizationId');
    plan.flush({});
  });

  it('asks with the application id, question, language and interpretation flag', () => {
    service.ask('app1', 'Tickets by status', 'en', false).subscribe();
    const ask = http.expectOne({ method: 'POST', url: '/api/ai/ask' });
    expect(ask.request.body).toEqual({ applicationId: 'app1', question: 'Tickets by status', language: 'en', interpret: false });
    ask.flush({});
  });
});
