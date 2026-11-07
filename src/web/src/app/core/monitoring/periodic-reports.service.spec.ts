import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { PeriodicReportsService } from './periodic-reports.service';

describe('PeriodicReportsService', () => {
  let service: PeriodicReportsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PeriodicReportsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('pauses, resumes and runs subscriptions by name and pages the delivery log', () => {
    service.setActive('s1', false).subscribe();
    http.expectOne({ method: 'POST', url: '/api/subscriptions/s1/pause' }).flush({});
    service.setActive('s1', true).subscribe();
    http.expectOne({ method: 'POST', url: '/api/subscriptions/s1/resume' }).flush({});
    service.runNow('s1').subscribe();
    http.expectOne({ method: 'POST', url: '/api/subscriptions/s1/run-now' }).flush({ runId: 'r', delivered: 1, noData: 0, failed: 0, deliveries: [] });
    service.deliveries('s1', 2, 10).subscribe();
    const log = http.expectOne((r) => r.url === '/api/subscriptions/s1/deliveries');
    expect(log.request.params.get('page')).toBe('2');
    log.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
  });

  it('opens received files only through the recipient endpoint', () => {
    expect(service.receivedFileUrl('d1')).toBe('/api/subscriptions/received/d1/file');
    service.received().subscribe();
    http.expectOne((r) => r.url === '/api/subscriptions/received').flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  });
});
