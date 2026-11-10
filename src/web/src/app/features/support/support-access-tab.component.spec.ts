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
        operations: [{ operation: 'CreateOrganizationUser', targetReferences: ['r-analyst', 'email:new.user@org.test'] }],
        targetLabels: { 'r-analyst': 'Analyst (Analyst)', 'email:new.user@org.test': 'new.user@org.test' },
        targetLabelsAr: { 'r-analyst': 'محلّل (Analyst)', 'email:new.user@org.test': 'new.user@org.test' },
        allowedActions: ['approve', 'decline'],
      }),
    ]);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const line = () => element.querySelector('[data-testid="grant-op-CreateOrganizationUser"]')?.textContent ?? '';
    expect(line()).toContain('Roles the new user may be given:');
    expect(line()).toContain('Analyst (Analyst)');
    expect(line()).not.toContain('Management');
    // The address the account is for is part of what the approver reads, and it is not listed among the roles.
    expect(line()).toContain('Account for:');
    expect(element.querySelector('[data-testid="grant-new-user-email"]')?.textContent).toBe('new.user@org.test');
    expect(line()).not.toContain('email:');
    expect(element.querySelector('[data-testid="grant-approve"]')).not.toBeNull();

    TestBed.inject(LanguageService).use('ar', false);
    fixture.detectChanges();
    expect(line()).toContain('الأدوار التي يمكن منحها للمستخدم الجديد:');
    expect(line()).toContain('محلّل (Analyst)');
    expect(line()).toContain('حساب لـ:');
  });

  it('tells the approver when a created user can be given no role', async () => {
    const fixture = await create('app');
    http.expectOne('/api/support/tickets/t1/access-grants').flush([grant({ operations: [{ operation: 'CreateOrganizationUser', targetReferences: [] }], allowedActions: ['approve', 'decline'] })]);
    fixture.detectChanges();

    const line = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="grant-op-CreateOrganizationUser"]')?.textContent ?? '';
    expect(line).toContain('No role may be given: the new user is created without roles.');
  });

  it('names the new user and the roles when proposing, and offers only the approved address and roles when running', async () => {
    const fixture = await create('platform');
    const active = grant({ status: 'Active', sessionActive: true, operations: [{ operation: 'CreateOrganizationUser', targetReferences: ['r-analyst', 'email:new.user@org.test'] }], targetLabels: { 'r-analyst': 'Analyst (Analyst)' } });
    http.expectOne('/api/platform/support/tickets/t1/access-grants').flush([active]);
    http.expectOne('/api/platform/support/operations').flush(catalog);
    http.expectOne('/api/platform/support/session').flush({ sessionId: 's1', grant: active, startedAt: '2026-11-10T08:00:00Z', expiresAt: '2026-11-10T12:00:00Z', operations: active.operations });
    const component = fixture.componentInstance;

    // Proposal: the roles and the address of the user to create travel with the proposal, for the organization to approve.
    const createRow = () => component.rows().find((r) => r.definition.code === 'CreateOrganizationUser')!;
    component.toggleRow(createRow(), true);
    component.setTargets(createRow(), ['r-analyst']);
    component.setEmails(createRow(), ' new.user@org.test ');
    component.reason.set('Requested in the ticket.');
    component.submitProposal();
    const proposal = http.expectOne({ method: 'POST', url: '/api/platform/support/tickets/t1/access-grants' });
    expect(proposal.request.body.operations).toEqual([{ operation: 'CreateOrganizationUser', targetReferences: ['r-analyst', 'email:new.user@org.test'] }]);
    proposal.flush(active);
    http.expectOne('/api/platform/support/tickets/t1/access-grants').flush([active]);
    http.expectOne('/api/platform/support/session').flush({ sessionId: 's1', grant: active, startedAt: '2026-11-10T08:00:00Z', expiresAt: '2026-11-10T12:00:00Z', operations: active.operations });

    // Running: no target is named; only the approved address and the approved role can be picked.
    component.selectOperation('CreateOrganizationUser');
    component.ensureTargets();
    http.expectOne('/api/platform/support/tickets/t1/targets').flush(targets);
    expect(component.runTarget()).toBeNull();
    expect(component.roleOptions()).toEqual([{ code: 'Analyst', label: 'Analyst' }]);
    expect(component.runEmails()).toEqual(['new.user@org.test']);
    expect(component.runPayload()['email']).toBe('new.user@org.test'); // one approved address: nothing to choose

    component.setField('fullName', 'New User');
    component.runRoleCodes.set(['Analyst']);
    component.execute();
    const execute = http.expectOne({ method: 'POST', url: '/api/platform/support/session/execute' });
    expect(execute.request.body).toEqual({ operation: 'CreateOrganizationUser', targetReference: null, payload: { email: 'new.user@org.test', fullName: 'New User', roleCodes: ['Analyst'] } });
    execute.flush({ operation: 'CreateOrganizationUser', outcome: 'Succeeded' });
    http.expectOne('/api/platform/support/tickets/t1/access-grants').flush([active]);
    http.expectOne('/api/platform/support/session').flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne('/api/platform/support/tickets/t1/targets').flush(targets); // what may be targeted follows the grant's state
  });

  it('adds a user the ticket names by exact e-mail address instead of listing the organization\'s users', async () => {
    const fixture = await create('platform');
    http.expectOne('/api/platform/support/tickets/t1/access-grants').flush([]);
    http.expectOne('/api/platform/support/operations').flush(catalog);
    http.expectOne('/api/platform/support/session').flush(null, { status: 204, statusText: 'No Content' });
    const component = fixture.componentInstance;
    component.startProposal();
    // Without an approved grant the list holds only the person who opened the ticket.
    http.expectOne('/api/platform/support/tickets/t1/targets').flush({ ...targets, users: [{ userId: 'u1', fullName: 'Alice', email: 'alice@org.test', isActive: true }] });
    const resetRow = () => component.rows().find((r) => r.definition.code === 'ResetOrganizationUserPassword')!;
    component.toggleRow(resetRow(), true);

    component.setLookup(resetRow(), 'nobody@org.test');
    component.lookupTarget(resetRow(), 'user');
    http.expectOne((r) => r.url === '/api/platform/support/tickets/t1/targets/user' && r.params.get('email') === 'nobody@org.test')
      .flush({ type: 'https://wabi-insight.dev/problems/not-found', title: 'Not found', status: 404, detail: 'support_target_not_found' }, { status: 404, statusText: 'Not Found' });
    expect(resetRow().lookupError).toContain('No match in this organization');
    expect(resetRow().targets).toEqual([]);

    component.setLookup(resetRow(), 'sara@org.test');
    component.lookupTarget(resetRow(), 'user');
    http.expectOne((r) => r.url === '/api/platform/support/tickets/t1/targets/user' && r.params.get('email') === 'sara@org.test')
      .flush({ userId: 'u2', fullName: 'Sara', email: 'sara@org.test', isActive: true });
    expect(resetRow().targets).toEqual(['u2']);
    expect(resetRow().lookup).toBe('');
    expect(component.targets()?.users.map((u) => u.userId)).toEqual(['u1', 'u2']);
  });

  afterEach(() => {
    TestBed.inject(LanguageService).use('en', false);
    http.verify();
  });
});
