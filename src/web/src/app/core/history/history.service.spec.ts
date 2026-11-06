import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { HistoryService } from './history.service';

describe('HistoryService', () => {
  let service: HistoryService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(HistoryService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists with only the filters that are set and never an organization id', () => {
    service.list({ page: 2, pageSize: 10, search: 'flights', favorite: true, archived: false }).subscribe();
    const req = http.expectOne((r) => r.url === '/api/history');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('search')).toBe('flights');
    expect(req.request.params.get('favorite')).toBe('true');
    expect(req.request.params.has('applicationId')).toBe(false);
    expect(req.request.params.has('organizationId')).toBe(false);
    req.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
  });

  it('reopens a stored version through the result endpoint and re-runs through rerun', () => {
    service.versionResult('h1', 2).subscribe();
    http.expectOne({ method: 'GET', url: '/api/history/h1/versions/2/result' }).flush({});
    service.rerun('h1').subscribe();
    http.expectOne({ method: 'POST', url: '/api/history/h1/rerun' }).flush({});
    expect(service.versionReportUrl('h1', 3)).toBe('/api/history/h1/versions/3/report');
  });
});
