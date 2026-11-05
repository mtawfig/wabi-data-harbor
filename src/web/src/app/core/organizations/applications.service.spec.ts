import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { ApplicationsService } from './applications.service';

describe('ApplicationsService', () => {
  let service: ApplicationsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ApplicationsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends connection strings only on create, replace and test, never requests them back', () => {
    service.create({ applicationCode: 'ERP', applicationName: 'ERP', applicationNameAr: 'ERP', databaseType: 'SqlServer', connectionString: 'Server=x;Database=y' }).subscribe();
    const create = http.expectOne({ method: 'POST', url: '/api/applications' });
    expect(create.request.body.connectionString).toBe('Server=x;Database=y');
    create.flush({});

    service.replaceConnection('a1', 'Server=z;Database=y').subscribe();
    const replace = http.expectOne({ method: 'PUT', url: '/api/applications/a1/connection' });
    expect(replace.request.body).toEqual({ connectionString: 'Server=z;Database=y', connectionHint: null });
    replace.flush({});

    service.testConnection('Oracle', 'Data Source=o').subscribe();
    http.expectOne({ method: 'POST', url: '/api/applications/test-connection' }).flush({ succeeded: true, warnings: [] });

    service.get('a1').subscribe();
    const get = http.expectOne({ method: 'GET', url: '/api/applications/a1' });
    expect(get.request.body).toBeNull();
    get.flush({});
  });

  it('builds metadata queries with only the filters that are set', () => {
    service.objects('a1', 2, 25, '', null, true).subscribe();
    const req = http.expectOne((r) => r.url === '/api/metadata/applications/a1/objects');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.has('search')).toBe(false);
    expect(req.request.params.has('approved')).toBe(false);
    expect(req.request.params.get('includeRemoved')).toBe('true');
    req.flush({ items: [], page: 2, pageSize: 25, totalCount: 0 });

    service.objects('a1', 1, 50, 'ticket', false).subscribe();
    const filtered = http.expectOne((r) => r.url === '/api/metadata/applications/a1/objects');
    expect(filtered.request.params.get('approved')).toBe('false');
    expect(filtered.request.params.get('search')).toBe('ticket');
    filtered.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
  });
});
