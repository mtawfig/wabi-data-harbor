import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { InsightsService } from './insights.service';

describe('InsightsService', () => {
  let service: InsightsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(InsightsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uses the platform catalog routes for maintenance and never sends an organization id in a query', () => {
    service.sync().subscribe();
    http.expectOne({ method: 'POST', url: '/api/platform/insights-catalog/sync' }).flush({});

    service.previewSql('m1', { bucket: 'Month' }).subscribe();
    const preview = http.expectOne({ method: 'POST', url: '/api/platform/insights-catalog/metrics/m1/sql' });
    expect(preview.request.body).toEqual({ bucket: 'Month' });
    preview.flush({ sql: 'SELECT', parameters: {} });

    service.queryMetric('users.total', { groupBy: 'Status', filters: [{ column: 'Status', operator: 'eq', values: ['Active'] }] }).subscribe();
    const query = http.expectOne({ method: 'POST', url: '/api/insights/metrics/users.total/query' });
    expect(JSON.stringify(query.request.body)).not.toContain('organizationId');
    query.flush({ rows: [] });
  });

  it('reads the organization dashboard with the period and bucket, and writes role grants by id list', () => {
    service.organizationDashboard(new Date('2026-10-01T00:00:00Z'), new Date('2026-11-01T00:00:00Z'), 'Week').subscribe();
    const dash = http.expectOne((r) => r.url === '/api/insights/dashboard');
    expect(dash.request.params.get('bucket')).toBe('Week');
    expect(dash.request.params.get('from')).toBe('2026-10-01T00:00:00.000Z');
    dash.flush({ kpis: [], series: [] });

    service.setRoleMetrics('r1', ['m1', 'm2']).subscribe();
    const put = http.expectOne({ method: 'PUT', url: '/api/insights/roles/r1/metrics' });
    expect(put.request.body).toEqual({ metricIds: ['m1', 'm2'] });
    put.flush({});
  });
});
