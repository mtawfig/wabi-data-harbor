import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { countryOptions } from '../../../core/demo/countries';
import { provideTestTransloco } from '../../../testing/transloco-testing';
import { RequestDemoComponent } from './request-demo.component';

describe('RequestDemoComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RequestDemoComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTestTransloco()],
    }).compileComponents();
  });

  const complete = { fullName: ' Noura Al Mansoori ', email: 'noura@falcon.test', organizationName: 'Falcon Logistics', countryCode: 'AE', consent: true };

  it('sends the form with the page language and shows the thank-you', async () => {
    const fixture = TestBed.createComponent(RequestDemoComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h1')?.textContent?.trim()).toBe('Request a demo');

    fixture.componentInstance.form.patchValue({ ...complete, jobTitle: 'Head of Operations', message: '  ' });
    const submit = fixture.componentInstance.submit();
    const request = http.expectOne('/api/public/demo-requests');
    expect(request.request.body).toEqual({
      fullName: 'Noura Al Mansoori',
      email: 'noura@falcon.test',
      organizationName: 'Falcon Logistics',
      countryCode: 'AE',
      consent: true,
      jobTitle: 'Head of Operations',
      phone: undefined,
      industry: undefined,
      organizationSize: undefined,
      databaseSystems: undefined,
      demoLanguage: 'en',
      preferredTime: undefined,
      message: undefined,
      pageLanguage: 'en',
      website: undefined,
    });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await submit;
    fixture.detectChanges();

    expect(element.querySelector('[data-testid="request-demo-sent"]')?.textContent).toContain('Your request is with us');
    expect(element.querySelector('[data-testid="request-demo-form"]')).toBeNull();
  });

  it('sends nothing without the required fields or without consent', async () => {
    const fixture = TestBed.createComponent(RequestDemoComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    await fixture.componentInstance.submit();
    fixture.componentInstance.form.patchValue({ ...complete, consent: false });
    await fixture.componentInstance.submit();
    fixture.detectChanges();

    http.expectNone('/api/public/demo-requests');
    expect(fixture.componentInstance.sent()).toBe(false);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Please agree to be contacted');
  });

  it('marks the fields the API refuses and says so when there are too many attempts', async () => {
    const fixture = TestBed.createComponent(RequestDemoComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.form.patchValue(complete);
    let submit = component.submit();
    http.expectOne('/api/public/demo-requests').flush({ errors: { organizationName: ['invalid'] } }, { status: 400, statusText: 'Bad Request' });
    await submit;
    expect(component.error()).toBe('invalid');
    expect(component.form.controls.organizationName.errors).toEqual({ server: 'invalid' });

    component.form.patchValue(complete);
    submit = component.submit();
    http.expectOne('/api/public/demo-requests').flush(null, { status: 429, statusText: 'Too Many Requests' });
    await submit;
    expect(component.error()).toBe('rate_limited');
    expect(component.sent()).toBe(false);
  });

  it('keeps the trap field out of sight and out of the tab order', () => {
    const fixture = TestBed.createComponent(RequestDemoComponent);
    fixture.detectChanges();
    const trap = (fixture.nativeElement as HTMLElement).querySelector('.demo-trap');
    expect(trap?.getAttribute('aria-hidden')).toBe('true');
    expect(trap?.querySelector('input')?.getAttribute('tabindex')).toBe('-1');
  });

  it('lists countries by name in the language asked for', () => {
    const english = countryOptions('en');
    const arabic = countryOptions('ar');
    expect(english.find((c) => c.code === 'AE')?.name).toBe('United Arab Emirates');
    expect(arabic.find((c) => c.code === 'AE')?.name).toBe('الإمارات العربية المتحدة');
    expect(english.length).toBe(arabic.length);
    expect(english.map((c) => c.name)).toEqual([...english.map((c) => c.name)].sort(new Intl.Collator('en').compare));
  });
});
