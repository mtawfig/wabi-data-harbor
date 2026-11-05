import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { PlatformInfo } from './core/platform/platform-info';

const info: PlatformInfo = {
  productName: 'Wabi Insight',
  productNameAr: 'وابي إنسايت',
  platformCode: 'AP-AIR',
  tagline: 'Ask Your Data. Trust the Answer.',
  taglineAr: 'اسأل بياناتك. وثق بالإجابة.',
  version: '0.1.0',
  supportedLanguages: ['en', 'ar'],
};

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('renders the product name from the API', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/platform/info').flush(info);
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Wabi Insight');
    expect(compiled.querySelector('.shell__status')?.textContent).toContain('API online');
    http.verify();
  });

  it('shows offline when the API is unreachable', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/platform/info').error(new ProgressEvent('error'));
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.shell__status')?.textContent).toContain('API offline');
    http.verify();
  });
});
