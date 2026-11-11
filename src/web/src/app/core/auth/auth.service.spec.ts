import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTestTransloco } from '../../testing/transloco-testing';
import { authInterceptor } from './auth.interceptor';
import { LanguageService } from '../i18n/language.service';
import { AuthService } from './auth.service';
import { Session } from './session.model';

const orgSession: Session = {
  id: '11111111-1111-1111-1111-111111111111',
  principalType: 'OrganizationUser',
  email: 'alice@org-a.test',
  displayName: 'Alice',
  organizationId: '22222222-2222-2222-2222-222222222222',
  roles: [],
  mustChangePassword: false,
  preferredLanguage: 'en',
};

/** Lets pending promise continuations (firstValueFrom → next request) run before the next expectation. */
const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.removeItem('wi.lang');
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(), provideTestTransloco()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('logs in, attaches the antiforgery header and routes organization users to the workspace', async () => {
    const csrf = service.refreshCsrf();
    http.expectOne('/api/auth/csrf').flush({ token: 'token-1', headerName: 'X-XSRF-TOKEN' });
    await csrf;

    const login = service.login('alice@org-a.test', 'User#Battery#Staple#2026');
    const request = http.expectOne('/api/auth/login');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('token-1');
    request.flush(orgSession);
    await settle();
    http.expectOne('/api/auth/csrf').flush({ token: 'token-2', headerName: 'X-XSRF-TOKEN' });
    await login;

    expect(service.isAuthenticated()).toBe(true);
    expect(service.isOrganizationUser()).toBe(true);
    expect(service.homeUrl()).toBe('/app/ask');
    expect(service.csrfToken()).toBe('token-2');
  });

  it('maps login problem types to error codes', async () => {
    const login = service.login('alice@org-a.test', 'wrong');
    http.expectOne('/api/auth/login').flush({ type: 'https://wabi-insight.dev/problems/locked_out' }, { status: 401, statusText: 'Unauthorized' });

    await expect(login).rejects.toEqual({ code: 'locked_out', status: 401 });
    expect(service.isAuthenticated()).toBe(false);
  });

  it('maps 429 to rate_limited', async () => {
    const login = service.login('alice@org-a.test', 'wrong');
    http.expectOne('/api/auth/login').flush(null, { status: 429, statusText: 'Too Many Requests' });

    await expect(login).rejects.toEqual({ code: 'rate_limited', status: 429 });
  });

  it('routes platform administrators to the overview and must-change sessions to the password page', () => {
    service.session.set({ ...orgSession, principalType: 'PlatformAdministrator', organizationId: undefined });
    expect(service.homeUrl()).toBe('/platform/overview');

    service.session.set({ ...orgSession, mustChangePassword: true });
    expect(service.homeUrl()).toBe('/change-password');
  });

  it('applies the account language on login', async () => {
    const login = service.login('alice@org-a.test', 'x');
    http.expectOne('/api/auth/login').flush({ ...orgSession, preferredLanguage: 'ar' });
    await settle();
    http.expectOne('/api/auth/csrf').flush({ token: 't', headerName: 'X-XSRF-TOKEN' });
    await login;

    expect(document.documentElement.getAttribute('dir')).toBe('rtl');
  });

  it('keeps the language this browser chose after a reload and brings the account up to date (C10)', async () => {
    localStorage.setItem('wi.lang', 'ar');
    const init = service.initialize();
    http.expectOne('/api/auth/csrf').flush({ token: 't', headerName: 'X-XSRF-TOKEN' });
    await settle();
    http.expectOne('/api/auth/me').flush({ ...orgSession, preferredLanguage: 'en' });
    await init;
    expect(document.documentElement.getAttribute('dir')).toBe('rtl');
    const save = http.expectOne({ method: 'PUT', url: '/api/auth/language' });
    expect(save.request.body).toEqual({ language: 'ar' });
    save.flush({ ...orgSession, preferredLanguage: 'ar' });
    await settle();
    expect(service.session()?.preferredLanguage).toBe('ar');
  });

  it('saves a language picked with the switcher on the signed-in account', async () => {
    service.session.set(orgSession);
    const language = TestBed.inject(LanguageService);
    language.use('en');
    language.toggle();
    http.expectOne({ method: 'PUT', url: '/api/auth/language' }).flush({ ...orgSession, preferredLanguage: 'ar' });
    await settle();
    expect(localStorage.getItem('wi.lang')).toBe('ar');
    expect(service.session()?.preferredLanguage).toBe('ar');
  });

  it('renews the antiforgery token when the session is lost and retries a sign-in the old token spoiled (F1)', async () => {
    service.session.set(orgSession);
    service.sessionLost();
    http.expectOne('/api/auth/csrf').flush({ token: 'fresh', headerName: 'X-XSRF-TOKEN' });
    await settle();
    expect(service.csrfToken()).toBe('fresh');

    const login = service.login('alice@org-a.test', 'x');
    http.expectOne('/api/auth/login').flush({ type: 'https://wabi-insight.dev/problems/antiforgery' }, { status: 400, statusText: 'Bad Request' });
    await settle();
    http.expectOne('/api/auth/csrf').flush({ token: 'again', headerName: 'X-XSRF-TOKEN' });
    await settle();
    const retry = http.expectOne('/api/auth/login');
    expect(retry.request.headers.get('X-XSRF-TOKEN')).toBe('again');
    retry.flush(orgSession);
    await settle();
    http.expectOne('/api/auth/csrf').flush({ token: 'after', headerName: 'X-XSRF-TOKEN' });
    await login;
    expect(service.isAuthenticated()).toBe(true);
  });
});
