import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PlatformService } from './platform.service';

describe('PlatformService', () => {
  let service: PlatformService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PlatformService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requests the dashboard with ISO bounds, bucket and organization', () => {
    service.dashboard(new Date('2026-10-01T00:00:00Z'), new Date('2026-10-31T00:00:00Z'), 'Week', 'org-1').subscribe();
    const request = http.expectOne((r) => r.url === '/api/platform/dashboard');
    expect(request.request.params.get('from')).toBe('2026-10-01T00:00:00.000Z');
    expect(request.request.params.get('bucket')).toBe('Week');
    expect(request.request.params.get('organizationId')).toBe('org-1');
    request.flush({ period: {}, kpis: [], series: [], organizationUsage: [], recentErrors: [] });
  });

  it('omits empty audit filters', () => {
    service.audit({ page: 1, pageSize: 50, eventType: null, search: '' }).subscribe();
    const request = http.expectOne((r) => r.url === '/api/platform/audit');
    expect(request.request.params.keys()).toEqual(['page', 'pageSize']);
    request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
  });

  it('changes administrator status', () => {
    service.setAdministratorStatus('a1', false).subscribe();
    const request = http.expectOne('/api/platform/administrators/a1/status');
    expect(request.request.body).toEqual({ isActive: false });
    request.flush({ id: 'a1' });
  });
});
