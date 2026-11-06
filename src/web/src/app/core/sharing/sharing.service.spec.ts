import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { SharingService } from './sharing.service';

describe('SharingService', () => {
  let service: SharingService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(SharingService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shares and revokes through the analysis endpoints without ever sending an organization id', () => {
    service.share('h1', ['u1', 'u2'], 'Please review', null).subscribe();
    const share = http.expectOne({ method: 'POST', url: '/api/history/h1/shares' });
    expect(share.request.body).toEqual({ recipientUserIds: ['u1', 'u2'], message: 'Please review', expiresAt: null });
    expect(JSON.stringify(share.request.body)).not.toContain('organizationId');
    share.flush([]);
    service.revoke('h1', 's1').subscribe();
    http.expectOne({ method: 'DELETE', url: '/api/history/h1/shares/s1' }).flush(null);
  });

  it('sends the email command with the disclosure flag and reads shared analyses through with-me', () => {
    service.sendEmail('h1', { versionNumber: 2, recipients: ['a@b.example'], subject: 'S', body: 'B', attachReport: true, disclosureApproved: true }).subscribe();
    const email = http.expectOne({ method: 'POST', url: '/api/history/h1/email' });
    expect(email.request.body.disclosureApproved).toBe(true);
    expect(email.request.body.versionNumber).toBe(2);
    email.flush({ recipients: 1, internalRecipients: 0, externalRecipients: 1, attachedReport: true, inAppSharesCreated: 0 });
    service.withMe(1, 25, 'flights').subscribe();
    const list = http.expectOne((r) => r.url === '/api/shares/with-me');
    expect(list.request.params.get('search')).toBe('flights');
    list.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    service.sharedResult('h1', 1).subscribe();
    http.expectOne({ method: 'GET', url: '/api/shares/with-me/h1/versions/1/result' }).flush({});
    expect(service.sharedReportUrl('h1', 3)).toBe('/api/shares/with-me/h1/versions/3/report');
  });
});
