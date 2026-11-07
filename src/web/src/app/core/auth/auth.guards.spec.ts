import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTestTransloco } from '../../testing/transloco-testing';
import { authGuard, guestGuard, organizationAdminGuard, platformAdministratorGuard, ruleAuthorGuard } from './auth.guards';
import { AuthService } from './auth.service';
import { Session } from './session.model';

const base: Session = { id: 'u', principalType: 'OrganizationUser', email: 'a@b.test', displayName: 'A', organizationId: 'o', roles: [], mustChangePassword: false, preferredLanguage: 'en' };

function run(guard: typeof authGuard, url = '/app/ask') {
  return TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot));
}

describe('auth guards', () => {
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTestTransloco()] });
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });

  it('redirects anonymous users to login with a return url', () => {
    const result = run(authGuard, '/app/history') as UrlTree;
    expect(router.serializeUrl(result)).toBe('/login?returnUrl=%2Fapp%2Fhistory');
  });

  it('forces the password change before anything else', () => {
    auth.session.set({ ...base, mustChangePassword: true });
    expect(router.serializeUrl(run(authGuard) as UrlTree)).toBe('/change-password');
    expect(run(authGuard, '/change-password')).toBe(true);
  });

  it('keeps organization users out of the platform area and vice versa', () => {
    auth.session.set(base);
    expect(router.serializeUrl(run(platformAdministratorGuard) as UrlTree)).toBe('/app/ask');
    expect(router.serializeUrl(run(organizationAdminGuard) as UrlTree)).toBe('/app/ask');

    auth.session.set({ ...base, roles: ['OrganizationAdmin'] });
    expect(run(organizationAdminGuard)).toBe(true);

    auth.session.set({ ...base, principalType: 'PlatformAdministrator', organizationId: undefined });
    expect(run(platformAdministratorGuard)).toBe(true);
  });

  it('opens Monitoring Rules to administrators and Management users only', () => {
    auth.session.set({ ...base, roles: ['Analyst'] });
    expect(router.serializeUrl(run(ruleAuthorGuard) as UrlTree)).toBe('/app/ask');
    auth.session.set({ ...base, roles: ['Management'] });
    expect(run(ruleAuthorGuard)).toBe(true);
    auth.session.set({ ...base, roles: ['OrganizationAdmin'] });
    expect(run(ruleAuthorGuard)).toBe(true);
  });

  it('sends signed-in users away from the login page', () => {
    auth.session.set(base);
    expect(router.serializeUrl(run(guestGuard, '/login') as UrlTree)).toBe('/app/ask');
    auth.session.set(null);
    expect(run(guestGuard, '/login')).toBe(true);
  });
});
