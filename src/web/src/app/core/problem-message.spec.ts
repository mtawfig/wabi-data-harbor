import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { provideTestTransloco } from '../testing/transloco-testing';
import { problemMessage } from './problem-message';

describe('problemMessage', () => {
  let transloco: TranslocoService;
  const response = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestTransloco()] });
    transloco = TestBed.inject(TranslocoService);
    transloco.setActiveLang('en');
  });

  it('says when there were too many requests or the server could not be reached (F1)', () => {
    expect(problemMessage(response(429, null), transloco)).toContain('Too many requests');
    expect(problemMessage(response(0, null), transloco)).toContain('could not be reached');
  });

  it('labels every code in the detail and in a validation problem, in Arabic too (F2)', () => {
    expect(problemMessage(response(400, { detail: 'phone_invalid, time_zone_invalid' }), transloco)).toBe('Enter a valid phone number. Choose a valid time zone.');
    expect(problemMessage(response(400, { errors: { Language: ['language_invalid'] } }), transloco)).toBe('Choose English or Arabic.');
    expect(problemMessage(response(422, { detail: 'column_not_permitted' }), transloco)).not.toContain('column_not_permitted');
    transloco.setActiveLang('ar');
    expect(problemMessage(response(400, { detail: 'phone_invalid' }), transloco)).toBe('أدخل رقم هاتف صحيحًا.');
  });

  it('never shows a raw code or free text, and falls back to the general message', () => {
    expect(problemMessage(response(400, { detail: 'some_code_nobody_labelled' }), transloco)).toBe(transloco.translate('admin.errors.unknown'));
    expect(problemMessage(response(500, { detail: 'System.InvalidOperationException at line 4' }), transloco)).toBe(transloco.translate('admin.errors.unknown'));
    expect(problemMessage(response(400, {}), transloco, 'organizations.errors.unknown')).toBe(transloco.translate('organizations.errors.unknown'));
  });
});
