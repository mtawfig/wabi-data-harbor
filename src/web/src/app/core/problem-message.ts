import { HttpErrorResponse } from '@angular/common/http';
import { TranslocoService } from '@jsverse/transloco';
import { ProblemDetails, problemCode } from './auth/session.model';

/** Where an error code may have its label: general errors, organization errors, query-plan rejections, password rules. */
const NAMESPACES = ['admin.errors', 'organizations.errors', 'ask.codes', 'changePassword.errors'];

/**
 * Turns an API problem response into a localized message. The API's `detail` carries one or more stable error codes
 * (comma separated); a validation problem carries them in `errors`. Each code is looked up in the label namespaces, a
 * request refused for being too frequent (429) or that never reached the server says so, and a code without a label
 * gives the general message instead of showing the code itself.
 */
export function problemMessage(response: HttpErrorResponse, transloco: TranslocoService, fallbackKey = 'admin.errors.unknown'): string {
  if (response.status === 429) {
    return transloco.translate('admin.errors.too_many_requests');
  }
  if (response.status === 0) {
    return transloco.translate('admin.errors.network');
  }

  const problem = response.error as (ProblemDetails & { errors?: Record<string, string[]> }) | undefined;
  const codes = [
    ...(problem?.detail ?? '').split(','),
    ...Object.values(problem?.errors ?? {}).flat(),
    // A problem type names a code too (https://…/problems/antiforgery), used when nothing more specific is given.
    problemCode(problem),
  ]
    .map((code) => (typeof code === 'string' ? code.trim() : ''))
    .filter((code) => /^[a-z][a-z0-9_]*$/.test(code));
  const specific = codes.filter((code) => code !== problemCode(problem));

  const messages = [...new Set(specific.length > 0 ? specific : codes)].map((code) => label(code, transloco)).filter((text): text is string => text !== null);
  return messages.length > 0 ? [...new Set(messages)].join(' ') : transloco.translate(fallbackKey);
}

function label(code: string, transloco: TranslocoService): string | null {
  for (const namespace of NAMESPACES) {
    const key = `${namespace}.${code}`;
    const translated = transloco.translate(key);
    if (translated !== key) {
      return translated;
    }
  }
  return null;
}
