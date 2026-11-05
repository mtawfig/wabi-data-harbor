import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { SupportService } from './support.service';

describe('SupportService', () => {
  let service: SupportService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(SupportService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the organization area with only the filters that are set', () => {
    service.list('app', { page: 2, pageSize: 10, status: 'Started', open: null, search: '', descending: true }).subscribe();
    const req = http.expectOne((r) => r.url === '/api/support/tickets');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('status')).toBe('Started');
    expect(req.request.params.has('open')).toBe(false);
    expect(req.request.params.has('search')).toBe(false);
    expect(req.request.params.get('descending')).toBe('true');
    req.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
  });

  it('targets the platform queue for platform pages and sends assignment there', () => {
    service.assign('t1', 'a1').subscribe();
    http.expectOne({ method: 'POST', url: '/api/platform/support/tickets/t1/assign' }).flush({});
    service.get('platform', 't1').subscribe();
    http.expectOne({ method: 'GET', url: '/api/platform/support/tickets/t1' }).flush({});
  });

  it('posts status changes with a null note when none is given and uploads as multipart', () => {
    service.changeStatus('app', 't1', 'Cancelled').subscribe();
    const status = http.expectOne({ method: 'POST', url: '/api/support/tickets/t1/status' });
    expect(status.request.body).toEqual({ status: 'Cancelled', note: null });
    status.flush({});

    service.upload('app', 't1', new File(['x'], 'log.txt', { type: 'text/plain' })).subscribe();
    const upload = http.expectOne({ method: 'POST', url: '/api/support/tickets/t1/attachments' });
    expect(upload.request.body).toBeInstanceOf(FormData);
    upload.flush({});
    expect(service.downloadUrl('platform', 't1', 'a1')).toBe('/api/platform/support/tickets/t1/attachments/a1');
  });
});
