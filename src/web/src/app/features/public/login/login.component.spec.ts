import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideTestTransloco } from '../../../testing/transloco-testing';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideRouter([{ path: 'app/ask', children: [] }]), provideHttpClient(), provideHttpClientTesting(), provideTestTransloco()],
    }).compileComponents();
  });

  it('renders the localized form and submits credentials', async () => {
    const fixture = TestBed.createComponent(LoginComponent);
    const http = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h1')?.textContent?.trim()).toBe('Sign in');

    fixture.componentInstance.form.setValue({ email: 'alice@org-a.test', password: 'User#Battery#Staple#2026' });
    const submit = fixture.componentInstance.submit();
    http.expectOne('/api/auth/login').flush({ id: 'u', principalType: 'OrganizationUser', email: 'alice@org-a.test', displayName: 'Alice', organizationId: 'o', roles: [], mustChangePassword: false, preferredLanguage: 'en' });
    await new Promise<void>((resolve) => setTimeout(resolve));
    http.expectOne('/api/auth/csrf').flush({ token: 't', headerName: 'X-XSRF-TOKEN' });
    await submit;

    expect(navigate).toHaveBeenCalledWith('/app/ask');
  });

  it('shows a localized error for invalid credentials', async () => {
    const fixture = TestBed.createComponent(LoginComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    fixture.componentInstance.form.setValue({ email: 'alice@org-a.test', password: 'wrong' });
    const submit = fixture.componentInstance.submit();
    http.expectOne('/api/auth/login').flush({ type: 'https://wabi-insight.dev/problems/invalid_credentials' }, { status: 401, statusText: 'Unauthorized' });
    await submit;
    fixture.detectChanges();

    const error = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="login-error"]');
    expect(error?.textContent).toContain('The email or password is incorrect.');
  });
});
