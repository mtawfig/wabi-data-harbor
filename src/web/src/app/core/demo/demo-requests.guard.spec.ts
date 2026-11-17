import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { provideTestTransloco } from '../../testing/transloco-testing';
import { LandingComponent } from '../../features/public/landing/landing.component';
import { demoRequestsOpenGuard } from './demo-requests.guard';

describe('Request a demo — switched on or off (P13.56)', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LandingComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTestTransloco()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  const guard = () => TestBed.runInInjectionContext(() => demoRequestsOpenGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)) as Promise<boolean | UrlTree>;
  const buttons = (element: HTMLElement) => ['header-request-demo', 'hero-request-demo', 'cta-request-demo'].filter((id) => element.querySelector(`[data-testid="${id}"]`));

  it('switched off: the landing page shows no button and the page sends everyone home', async () => {
    const fixture = TestBed.createComponent(LandingComponent);
    fixture.detectChanges();
    const result = guard();
    http.expectOne('/api/public/demo-requests/availability').flush({ enabled: false });
    expect(TestBed.inject(Router).serializeUrl((await result) as UrlTree)).toBe('/');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(buttons(fixture.nativeElement)).toEqual([]);
  });

  it('a failed answer counts as off; switched on: the three buttons show and the page opens', async () => {
    const failed = guard();
    http.expectOne('/api/public/demo-requests/availability').flush('', { status: 500, statusText: 'Server Error' });
    expect(await failed).not.toBe(true);

    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({ imports: [LandingComponent], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTestTransloco()] }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(LandingComponent);
    fixture.detectChanges();
    const result = guard();
    http.expectOne('/api/public/demo-requests/availability').flush({ enabled: true });
    expect(await result).toBe(true);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(buttons(fixture.nativeElement)).toEqual(['header-request-demo', 'hero-request-demo', 'cta-request-demo']);
  });
});
