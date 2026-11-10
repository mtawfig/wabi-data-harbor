import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LanguageService } from '../../core/i18n/language.service';
import { SupportAccessGrant, SupportOperationDefinition, SupportTargets } from '../../core/support/support-access.models';
import { SupportArea, TicketDetail } from '../../core/support/support.models';
import { provideTestTransloco } from '../../testing/transloco-testing';
import { SupportAccessTabComponent } from './support-access-tab.component';

const ticket = { summary: { ticketId: 't1', status: 'Started' } } as unknown as TicketDetail;

function grant(overrides: Partial<SupportAccessGrant>): SupportAccessGrant {
  return {
    grantId: 'g1', ticketId: 't1', ticketReference: 'WI-1', ticketSubject: 'Subject', organizationId: 'o1', organizationName: 'Org', organizationNameAr: 'مؤسسة',
    status: 'Proposed', operations: [], targetLabels: {}, targetLabelsAr: {}, reason: 'Requested in the ticket.',
    requestedByUserId: 'u1', requestedByName: 'Alice', proposedByAdministratorId: 'a1', proposedByName: 'Platform Admin',
    assignedPlatformAdministratorId: 'a1', assignedAdministratorName: 'Platform Admin', createdAt: '2026-11-10T08:00:00Z',
    sessionActive: false, operationsExecuted: 0, allowedActions: [], ...overrides,
  };
}

const catalog: SupportOperationDefinition[] = [
  { code: 'CreateOrganizationUser', targetType: 'AssignableRole', requiresTarget: false, executable: true, description: '' },
  { code: 'ResetOrganizationUserPassword', targetType: 'OrganizationUser', requiresTarget: true, executable: true, description: '' },
];

const targets: SupportTargets = {
  users: [],
  roles: [
    { roleId: 'r-analyst', roleCode: 'Analyst', nameEn: 'Analyst', nameAr: 'محلّل', isSystem: true, isActive: true },
    { roleId: 'r-management', roleCode: 'Management', nameEn: 'Management', nameAr: 'الإدارة', isSystem: true, isActive: true },
  ],
  objects: [],
};

describe('SupportAccessTabComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SupportAccessTabComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTestTransloco()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  async function create(area: SupportArea): Promise<ComponentFixture<SupportAccessTabComponent>> {
    const fixture = TestBed.createComponent(SupportAccessTabComponent);
    fixture.componentRef.setInput('area', area);
    fixture.componentRef.setInput('ticket', ticket);
    fixture.detectChanges();
    await Promise.resolve(); // the component loads in a microtask, once its inputs are set
    return fixture;
  }

  it('shows the approver which roles support may give to a created user, in English and Arabic', async () => {
    const fixture = await create('app');
    http.expectOne('/api/support/tickets/t1/access-grants').flush([
      grant({
        operations: [{ operation: 'CreateOrganizationUser', targetReferences: ['r-analyst'] }],
        targetLabels: { 'r-analyst': 'Analyst (Analyst)' },
        targetLabelsAr: { 'r-analyst': 'محلّل (Analyst)' },
        allowedActions: ['approve', 'decline'],
      }),
    ]);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const line = () => element.querySelector('[data-testid="grant-op-CreateOrganizationUser"]')?.textContent ?? '';
    expect(line()).toContain('Roles the new user may be given:');
    expect(line()).toContain('Analyst (Analyst)');
    expect(line()).not.toContain('Management');
    expect(element.querySelector('[data-testid="grant-approve"]')).not.toBeNull();

    TestBed.inject(LanguageService).use('ar', false);
    fixture.detectChanges();
    expect(line()).toContain('الأدوار التي يمكن منحها للمستخدم الجديد:');
    expect(line()).toContain('محلّل (Analyst)');
  });

  it('tells the approver when a created user can be given no role', async () => {
    const fixture = await create('app');
    http.expectOne('/api/support/tickets/t1/access-grants').flush([grant({ operations: [{ operation: 'CreateOrganizationUser', targetReferences: [] }], allowedActions: ['approve', 'decline'] })]);
    fixture.detectChanges();

    const line = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="grant-op-CreateOrganizationUser"]')?.textContent ?? '';
    expect(line).toContain('No role may be given: the new user is created without roles.');
  });

  it('lets support choose the roles when proposing and offers only the approved roles when running', async () => {
    const fixture = await create('platform');
    const active = grant({ status: 'Active', sessionActive: true, operations: [{ operation: 'CreateOrganizationUser', targetReferences: ['r-analyst'] }], targetLabels: { 'r-analyst': 'Analyst (Analyst)' } });
    http.expectOne('/api/platform/support/tickets/t1/access-grants').flush([active]);
    http.expectOne('/api/platform/support/operations').flush(catalog);
    http.expectOne('/api/platform/support/session').flush({ sessionId: 's1', grant: active, startedAt: '2026-11-10T08:00:00Z', expiresAt: '2026-11-10T12:00:00Z', operations: active.operations });
    const component = fixture.componentInstance;

    // Proposal: the role targets of the operation travel with the proposal; an empty list is allowed.
    const row = component.rows().find((r) => r.definition.code === 'CreateOrganizationUser')!;
    component.toggleRow(row, true);
    component.setTargets(component.rows().find((r) => r.definition.code === 'CreateOrganizationUser')!, ['r-analyst']);
    component.reason.set('Requested in the ticket.');
    component.submitProposal();
    const proposal = http.expectOne({ method: 'POST', url: '/api/platform/support/tickets/t1/access-grants' });
    expect(proposal.request.body.operations).toEqual([{ operation: 'CreateOrganizationUser', targetReferences: ['r-analyst'] }]);
    proposal.flush(active);
    http.expectOne('/api/platform/support/tickets/t1/access-grants').flush([active]);
    http.expectOne('/api/platform/support/session').flush({ sessionId: 's1', grant: active, startedAt: '2026-11-10T08:00:00Z', expiresAt: '2026-11-10T12:00:00Z', operations: active.operations });

    // Running: no target is named, and only the approved role can be picked.
    component.selectOperation('CreateOrganizationUser');
    component.ensureTargets();
    http.expectOne('/api/platform/support/tickets/t1/targets').flush(targets);
    expect(component.runTarget()).toBeNull();
    expect(component.roleOptions()).toEqual([{ code: 'Analyst', label: 'Analyst' }]);

    component.setField('email', 'new.user@org.test');
    component.setField('fullName', 'New User');
    component.runRoleCodes.set(['Analyst']);
    component.execute();
    const execute = http.expectOne({ method: 'POST', url: '/api/platform/support/session/execute' });
    expect(execute.request.body).toEqual({ operation: 'CreateOrganizationUser', targetReference: null, payload: { email: 'new.user@org.test', fullName: 'New User', roleCodes: ['Analyst'] } });
    execute.flush({ operation: 'CreateOrganizationUser', outcome: 'Succeeded' });
    http.expectOne('/api/platform/support/tickets/t1/access-grants').flush([active]);
    http.expectOne('/api/platform/support/session').flush(null, { status: 204, statusText: 'No Content' });
  });

  afterEach(() => {
    TestBed.inject(LanguageService).use('en', false);
    http.verify();
  });
});
