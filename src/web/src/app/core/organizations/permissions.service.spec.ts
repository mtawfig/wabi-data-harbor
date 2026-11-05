import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { PermissionsService } from './permissions.service';

describe('PermissionsService', () => {
  let service: PermissionsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PermissionsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('writes grants and mappings against the role/object routes and never sends an organization id', () => {
    service.setGrant('r1', 'o1', { granted: true, allColumns: false, columnIds: ['c1'], rowScope: 'Department' }).subscribe();
    const grant = http.expectOne({ method: 'PUT', url: '/api/permissions/roles/r1/objects/o1' });
    expect(grant.request.body).toEqual({ granted: true, allColumns: false, columnIds: ['c1'], rowScope: 'Department' });
    expect(JSON.stringify(grant.request.body)).not.toContain('organizationId');
    grant.flush({});

    service.setMapping('o1', { departmentColumnId: 'c9', employeeColumnId: null, sectionColumnId: null }).subscribe();
    const mapping = http.expectOne({ method: 'PUT', url: '/api/permissions/objects/o1/row-mapping' });
    expect(mapping.request.body.departmentColumnId).toBe('c9');
    mapping.flush({});
  });

  it('reads the matrix, own permissions and an administrator preview', () => {
    service.matrix('r1', 'a1').subscribe();
    http.expectOne({ method: 'GET', url: '/api/permissions/roles/r1/applications/a1' }).flush({ objects: [] });
    service.mine().subscribe();
    http.expectOne({ method: 'GET', url: '/api/permissions/me' }).flush({ objects: [] });
    service.effective('u1').subscribe();
    http.expectOne({ method: 'GET', url: '/api/permissions/users/u1/effective' }).flush({ objects: [] });
  });
});
