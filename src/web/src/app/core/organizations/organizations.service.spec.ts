import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OrganizationProfileService, OrganizationsService } from './organizations.service';

describe('OrganizationsService', () => {
  let service: OrganizationsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(OrganizationsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists with paging, search and status filters', () => {
    service.list(2, 10, 'acme', 'Suspended').subscribe();
    const request = http.expectOne((r) => r.url === '/api/platform/organizations');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('10');
    expect(request.request.params.get('search')).toBe('acme');
    expect(request.request.params.get('status')).toBe('Suspended');
    request.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
  });

  it('changes status with a reason', () => {
    service.setStatus('org-1', 'Suspended', 'Unpaid').subscribe();
    const request = http.expectOne('/api/platform/organizations/org-1/status');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ status: 'Suspended', reason: 'Unpaid' });
    request.flush({});
  });

  it('reads and updates the current organization profile', () => {
    const profile = TestBed.inject(OrganizationProfileService);
    profile.get().subscribe();
    http.expectOne('/api/organization').flush({});
    profile.update({ organizationName: 'A', organizationNameAr: 'أ', timeZone: 'UTC', defaultLanguage: 'en' }).subscribe();
    const request = http.expectOne('/api/organization');
    expect(request.request.method).toBe('PUT');
    request.flush({});
  });
});
