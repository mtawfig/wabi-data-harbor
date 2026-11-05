import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { SupportAccessService } from './support-access.service';

describe('SupportAccessService', () => {
  let service: SupportAccessService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(SupportAccessService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('tracks the support-mode session through enter, refresh and exit', () => {
    service.enter('g1').subscribe();
    http.expectOne({ method: 'POST', url: '/api/platform/support/access-grants/g1/enter' }).flush({ sessionId: 's1', grant: { grantId: 'g1' }, operations: [] });
    expect(service.session()?.sessionId).toBe('s1');

    service.refreshSession().subscribe();
    http.expectOne('/api/platform/support/session').flush(null, { status: 204, statusText: 'No Content' });
    expect(service.session()).toBeNull();

    service.refreshSession().subscribe();
    http.expectOne('/api/platform/support/session').flush({ sessionId: 's2', grant: { grantId: 'g1' }, operations: [] });
    expect(service.session()?.sessionId).toBe('s2');

    service.exit().subscribe();
    http.expectOne({ method: 'POST', url: '/api/platform/support/session/exit' }).flush(null);
    expect(service.session()).toBeNull();
  });

  it('routes decisions to the organization API and proposals, execution to the platform API', () => {
    service.approve('g1', 'ok', 2).subscribe();
    const approve = http.expectOne({ method: 'POST', url: '/api/support/access-grants/g1/approve' });
    expect(approve.request.body).toEqual({ note: 'ok', expiryHours: 2 });
    approve.flush({});

    service.propose('t1', [{ operation: 'CreateOrganizationUser', targetReferences: [] }], 'why', null).subscribe();
    http.expectOne({ method: 'POST', url: '/api/platform/support/tickets/t1/access-grants' }).flush({});

    service.execute('CreateOrganizationUser', null, { email: 'x@y.z' }).subscribe();
    const execute = http.expectOne({ method: 'POST', url: '/api/platform/support/session/execute' });
    expect(execute.request.body).toEqual({ operation: 'CreateOrganizationUser', targetReference: null, payload: { email: 'x@y.z' } });
    execute.flush({ outcome: 'Succeeded' });

    service.list('app', 1, 10, null, true).subscribe();
    const list = http.expectOne((r) => r.url === '/api/support/access-grants');
    expect(list.request.params.get('pending')).toBe('true');
    list.flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
  });
});
