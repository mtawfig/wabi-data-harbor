import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OrganizationAdminService, RoleTemplatesService } from './admin.service';

describe('OrganizationAdminService', () => {
  let service: OrganizationAdminService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(OrganizationAdminService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists users with all filters as query parameters', () => {
    service.listUsers(3, 50, 'omar', 'Active', 'dept-1', 'Management').subscribe();
    const request = http.expectOne((r) => r.url === '/api/users');
    expect(request.request.params.get('page')).toBe('3');
    expect(request.request.params.get('pageSize')).toBe('50');
    expect(request.request.params.get('search')).toBe('omar');
    expect(request.request.params.get('status')).toBe('Active');
    expect(request.request.params.get('departmentId')).toBe('dept-1');
    expect(request.request.params.get('roleCode')).toBe('Management');
    request.flush({ items: [], page: 3, pageSize: 50, totalCount: 0 });
  });

  it('creates a user without expecting a password in the response', () => {
    service.createUser({ email: 'a@b.test', fullName: 'A', preferredLanguage: 'en', roleCodes: ['Analyst'] }).subscribe((user) => {
      expect(user.userId).toBe('u1');
      expect(Object.keys(user)).not.toContain('temporaryPassword');
    });
    const request = http.expectOne('/api/users');
    expect(request.request.method).toBe('POST');
    request.flush({ userId: 'u1', email: 'a@b.test', fullName: 'A', preferredLanguage: 'en', status: 'Active', mustChangePassword: true, createdAt: '', roles: ['Analyst'] });
  });

  it('filters sections by department and manages role templates', () => {
    service.listSections('dept-1').subscribe();
    expect(http.expectOne((r) => r.url === '/api/sections').request.params.get('departmentId')).toBe('dept-1');

    const templates = TestBed.inject(RoleTemplatesService);
    templates.update('t1', { roleCode: 'Auditor', nameEn: 'Auditor', nameAr: 'مدقق', isActive: true, sortOrder: 5 }).subscribe();
    const request = http.expectOne('/api/platform/role-templates/t1');
    expect(request.request.method).toBe('PUT');
    request.flush({});
  });
});
