import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PlansService } from './plans.service';

describe('PlansService', () => {
  let service: PlansService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PlansService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('gives an organization a plan, or none, through the platform route', () => {
    service.assign('org-1', 'plan-1').subscribe();
    const set = http.expectOne('/api/platform/plans/organizations/org-1');
    expect(set.request.method).toBe('PUT');
    expect(set.request.body).toEqual({ planId: 'plan-1' });
    set.flush({ organizationId: 'org-1', planId: 'plan-1', monthSpend: 0, state: 'within' });

    service.assign('org-1', null).subscribe();
    const cleared = http.expectOne('/api/platform/plans/organizations/org-1');
    expect(cleared.request.body).toEqual({ planId: null });
    cleared.flush({ organizationId: 'org-1', monthSpend: 0, state: 'unlimited' });
  });

  it('never names an organization when a user asks for their own spending state', () => {
    let state = '';
    service.spending().subscribe((s) => (state = s.state));
    const request = http.expectOne('/api/ai/spending');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ state: 'near' });
    expect(state).toBe('near');
  });
});
