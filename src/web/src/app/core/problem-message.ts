import { HttpErrorResponse } from '@angular/common/http';
import { TranslocoService } from '@jsverse/transloco';
import { ProblemDetails } from './auth/session.model';

/**
 * Turns an API problem response into a localized message. The API's `detail` carries one or more stable
 * error codes (comma separated); each is looked up under `admin.errors.<code>` and unknown codes fall
 * back to the raw code so nothing is silently hidden.
 */
export function problemMessage(response: HttpErrorResponse, transloco: TranslocoService, fallbackKey = 'admin.errors.unknown'): string {
  const problem = response.error as ProblemDetails | undefined;
  const detail = problem?.detail?.trim();
  if (!detail) {
    return transloco.translate(fallbackKey);
  }

  return detail
    .split(',')
    .map((code) => code.trim())
    .filter((code) => code.length > 0)
    .map((code) => {
      const key = `admin.errors.${code}`;
      const translated = transloco.translate(key);
      return translated === key ? code : translated;
    })
    .join(' ');
}
