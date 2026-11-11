import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { provideTestTransloco } from '../../../testing/transloco-testing';
import { ResetPasswordComponent } from './reset-password.component';

const TOKEN = 'Zm9yLXRlc3RzLW9ubHktbm90LWEtcmVhbC10b2tlbi0xMjM';

describe('ResetPasswordComponent', () => {
  let replaced: string[];
  let fragments: BehaviorSubject<string | null>;

  async function open(fragment: string | null): Promise<{ fixture: ComponentFixture<ResetPasswordComponent>; http: HttpTestingController; element: HTMLElement }> {
    replaced = [];
    fragments = new BehaviorSubject<string | null>(fragment);
    await TestBed.configureTestingModule({
      imports: [ResetPasswordComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTestTransloco(),
        { provide: ActivatedRoute, useValue: { fragment: fragments } },
        { provide: Location, useValue: { replaceState: (path: string) => replaced.push(path) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ResetPasswordComponent);
    fixture.detectChanges();
    return { fixture, http: TestBed.inject(HttpTestingController), element: fixture.nativeElement as HTMLElement };
  }

  async function settle(fixture: ComponentFixture<ResetPasswordComponent>): Promise<void> {
    await new Promise<void>((resolve) => setTimeout(resolve));
    fixture.detectChanges();
  }

  it('opened without a link, shows no form and offers a new link', async () => {
    const { fixture, http, element } = await open(null);
    await settle(fixture);

    http.expectNone('/api/auth/reset-password/check');
    expect(element.querySelector('[data-testid="reset-password-form"]')).toBeNull();
    expect(element.querySelector('[data-testid="reset-password-invalid"]')?.textContent).toContain('This link is not valid any more');
    expect(element.querySelector('[data-testid="reset-password-request-new"]')?.getAttribute('href')).toBe('/forgot-password');
  });

  it('never asks the API about something that is not a token', async () => {
    const { fixture, http, element } = await open('token=<script>alert(1)</script>');
    await settle(fixture);

    http.expectNone('/api/auth/reset-password/check');
    expect(element.querySelector('[data-testid="reset-password-invalid"]')).not.toBeNull();
  });

  it('with a used or expired link, shows no form', async () => {
    const { fixture, http, element } = await open(`token=${TOKEN}`);
    http.expectOne('/api/auth/reset-password/check').flush({ valid: false });
    await settle(fixture);

    expect(element.querySelector('[data-testid="reset-password-form"]')).toBeNull();
    expect(element.querySelector('[data-testid="reset-password-invalid"]')).not.toBeNull();
  });

  it('takes the token out of the address bar, then saves the new password typed twice', async () => {
    const { fixture, http, element } = await open(`token=${TOKEN}`);
    expect(replaced).toEqual(['/reset-password']);
    const check = http.expectOne('/api/auth/reset-password/check');
    expect(check.request.body).toEqual({ token: TOKEN });
    check.flush({ valid: true });
    await settle(fixture);

    expect(element.querySelector('h1')?.textContent?.trim()).toBe('Choose a new password');
    expect(element.querySelectorAll('input[type="password"]').length).toBe(2);

    // Two different entries are not sent.
    fixture.componentInstance.form.setValue({ newPassword: 'Reset#Strong#Pass#2026', confirmPassword: 'Reset#Strong#Pass#2027' });
    await fixture.componentInstance.submit();
    http.expectNone('/api/auth/reset-password');

    fixture.componentInstance.form.setValue({ newPassword: 'Reset#Strong#Pass#2026', confirmPassword: 'Reset#Strong#Pass#2026' });
    const submit = fixture.componentInstance.submit();
    const save = http.expectOne('/api/auth/reset-password');
    expect(save.request.body).toEqual({ token: TOKEN, newPassword: 'Reset#Strong#Pass#2026' });
    save.flush(null, { status: 204, statusText: 'No Content' });
    await new Promise<void>((resolve) => setTimeout(resolve));
    http.expectOne('/api/auth/csrf').flush({ token: 't', headerName: 'X-XSRF-TOKEN' });
    await submit;
    fixture.detectChanges();

    expect(element.querySelector('[data-testid="reset-password-form"]')).toBeNull();
    expect(element.querySelector('[data-testid="reset-password-done"]')?.textContent).toContain('Your password was changed');
    expect(element.querySelector('[data-testid="reset-password-sign-in"]')?.getAttribute('href')).toBe('/login');
  });

  it('shows the password rule that was broken and keeps the form', async () => {
    const { fixture, http, element } = await open(`token=${TOKEN}`);
    http.expectOne('/api/auth/reset-password/check').flush({ valid: true });
    await settle(fixture);

    fixture.componentInstance.form.setValue({ newPassword: 'someone@org.test', confirmPassword: 'someone@org.test' });
    const submit = fixture.componentInstance.submit();
    http.expectOne('/api/auth/reset-password').flush({ errors: { NewPassword: ['password_equals_email'] } }, { status: 400, statusText: 'Bad Request' });
    await submit;
    fixture.detectChanges();

    expect(element.querySelector('[data-testid="reset-password-error"]')?.textContent).toContain('must not be your email address');
    expect(element.querySelector('[data-testid="reset-password-form"]')).not.toBeNull();
  });

  it('when the link ended while the form was open, replaces the form with the offer of a new link', async () => {
    const { fixture, http, element } = await open(`token=${TOKEN}`);
    http.expectOne('/api/auth/reset-password/check').flush({ valid: true });
    await settle(fixture);

    fixture.componentInstance.form.setValue({ newPassword: 'Reset#Strong#Pass#2026', confirmPassword: 'Reset#Strong#Pass#2026' });
    const submit = fixture.componentInstance.submit();
    http.expectOne('/api/auth/reset-password').flush({ errors: { Token: ['reset_link_invalid'] } }, { status: 400, statusText: 'Bad Request' });
    await submit;
    fixture.detectChanges();

    expect(element.querySelector('[data-testid="reset-password-form"]')).toBeNull();
    expect(element.querySelector('[data-testid="reset-password-invalid"]')).not.toBeNull();
  });

  it('picks up a link opened in the tab where the page is already showing', async () => {
    const { fixture, http, element } = await open(null);
    await settle(fixture);
    expect(element.querySelector('[data-testid="reset-password-invalid"]')).not.toBeNull();

    // Only the part after "#" changes: the page is not loaded again.
    fragments.next(`token=${TOKEN}`);
    http.expectOne('/api/auth/reset-password/check').flush({ valid: true });
    await settle(fixture);

    expect(element.querySelector('[data-testid="reset-password-form"]')).not.toBeNull();
    expect(replaced).toEqual(['/reset-password', '/reset-password']);

    // The address losing its fragment afterwards does not take the form away.
    fragments.next(null);
    await settle(fixture);
    expect(element.querySelector('[data-testid="reset-password-form"]')).not.toBeNull();
  });
});
