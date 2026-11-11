import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTestTransloco } from '../../../testing/transloco-testing';
import { ForgotPasswordComponent } from './forgot-password.component';

describe('ForgotPasswordComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTestTransloco()],
    }).compileComponents();
  });

  it('sends the address and shows the same confirmation whatever the address', async () => {
    const fixture = TestBed.createComponent(ForgotPasswordComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h1')?.textContent?.trim()).toBe('Forgot your password?');

    fixture.componentInstance.form.setValue({ email: 'someone@org.test' });
    const submit = fixture.componentInstance.submit();
    const request = http.expectOne('/api/auth/forgot-password');
    expect(request.request.body).toEqual({ email: 'someone@org.test' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await submit;
    fixture.detectChanges();

    const sent = element.querySelector('[data-testid="forgot-password-sent"]');
    expect(sent?.textContent).toContain('If an account exists for this address');
    expect(element.querySelector('[data-testid="forgot-password-form"]')).toBeNull();
  });

  it('does not send an address that is not an e-mail', async () => {
    const fixture = TestBed.createComponent(ForgotPasswordComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    fixture.componentInstance.form.setValue({ email: 'not an address' });
    await fixture.componentInstance.submit();

    http.expectNone('/api/auth/forgot-password');
    expect(fixture.componentInstance.sent()).toBe(false);
  });

  it('says so when too many requests were made', async () => {
    const fixture = TestBed.createComponent(ForgotPasswordComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    fixture.componentInstance.form.setValue({ email: 'someone@org.test' });
    const submit = fixture.componentInstance.submit();
    http.expectOne('/api/auth/forgot-password').flush(null, { status: 429, statusText: 'Too Many Requests' });
    await submit;
    fixture.detectChanges();

    const error = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="forgot-password-error"]');
    expect(error?.textContent).toContain('Too many attempts');
  });
});
