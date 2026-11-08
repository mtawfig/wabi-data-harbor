import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { MonitoringService } from './monitoring.service';

describe('MonitoringService', () => {
  let service: MonitoringService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(MonitoringService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for templates as the chosen owner and pauses or resumes rules by name', () => {
    service.templates('owner-1').subscribe();
    const templates = http.expectOne((r) => r.url === '/api/monitoring/templates');
    expect(templates.request.params.get('ownerUserId')).toBe('owner-1');
    templates.flush([]);

    service.templates(null).subscribe();
    expect(
      http
        .expectOne((r) => r.url === '/api/monitoring/templates')
        .request.params.has('ownerUserId'),
    ).toBe(false);

    service.setActive('r1', false).subscribe();
    http.expectOne({ method: 'POST', url: '/api/monitoring/rules/r1/pause' }).flush({});
    service.setActive('r1', true).subscribe();
    http.expectOne({ method: 'POST', url: '/api/monitoring/rules/r1/resume' }).flush({});
  });

  it('filters alerts only by what is set and sends the note with every action', () => {
    service.alerts({ status: 'Open', severity: null, page: 2 }).subscribe();
    const list = http.expectOne((r) => r.url === '/api/monitoring/alerts');
    expect(list.request.params.get('status')).toBe('Open');
    expect(list.request.params.has('severity')).toBe(false);
    expect(list.request.params.get('page')).toBe('2');
    list.flush({ items: [], total: 0, page: 2, pageSize: 25, openCount: 0, openHighCount: 0 });

    service.act('a1', 'resolve', 'Checked with the owner').subscribe();
    const resolve = http.expectOne({ method: 'POST', url: '/api/monitoring/alerts/a1/resolve' });
    expect(resolve.request.body).toEqual({ note: 'Checked with the owner' });
    resolve.flush({});

    service.act('a1', 'suspend-user').subscribe();
    expect(
      http.expectOne({ method: 'POST', url: '/api/monitoring/alerts/a1/suspend-user' }).request
        .body,
    ).toEqual({ note: null });
  });

  it('drafts a rule from a description without saving it, and sends the AI budget with the settings', () => {
    let drafted: string | undefined;
    service
      .draft({
        description: 'نبهني عندما يُستخدم نفس الحساب من أكثر من جهاز',
        language: 'ar',
        ownerUserId: null,
      })
      .subscribe((d) => (drafted = d.rule.name));
    const draft = http.expectOne({ method: 'POST', url: '/api/monitoring/rules/draft' });
    expect(draft.request.body).toEqual({
      description: 'نبهني عندما يُستخدم نفس الحساب من أكثر من جهاز',
      language: 'ar',
      ownerUserId: null,
    });
    draft.flush({
      rule: { name: 'نفس الحساب من أكثر من جهاز' },
      objectLabel: 'GAS · dbo.SignIns',
      bindings: [],
      explanation: '',
      assumptions: [],
    });
    expect(drafted).toBe('نفس الحساب من أكثر من جهاز');
    // Drafting never creates a rule: only the draft endpoint was called.
    http.expectNone({ method: 'POST', url: '/api/monitoring/rules' });

    service
      .saveSettings({
        digestTime: '07:00:00',
        workingHoursStart: '08:00:00',
        workingHoursEnd: '17:00:00',
        workingDays: '12345',
        retentionMonths: 24,
        monthlyAiBudget: 250,
      })
      .subscribe();
    const settings = http.expectOne({ method: 'PUT', url: '/api/monitoring/settings' });
    expect(settings.request.body.monthlyAiBudget).toBe(250);
    settings.flush({});
  });
});
