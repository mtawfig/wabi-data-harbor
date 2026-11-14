/**
 * Dates and numbers in the interface language. Arabic uses the Egyptian Arabic locale, as the answer tables always have:
 * Arabic month names and Arabic-Indic digits with the Arabic separators; English uses US conventions. Built on the
 * browser's Intl API, so switching language re-formats everything without reloading.
 */
export type DateFormat = 'short' | 'medium' | 'mediumDate' | 'shortDate' | 'shortTime' | 'monthYear';

export function localeFor(language: string): string {
  return language === 'ar' ? 'ar-EG' : 'en-US';
}

// Explicit fields rather than date styles: Arabic's "medium" style is all digits, while people expect the month name
// ("٨ نوفمبر ٢٠٢٦"). In English these give the same text as Angular's DatePipe formats of the same names.
const DATE_OPTIONS: Record<DateFormat, Intl.DateTimeFormatOptions> = {
  short: { year: '2-digit', month: 'numeric', day: 'numeric', hour: 'numeric', minute: '2-digit' },
  medium: { year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit', second: '2-digit' },
  mediumDate: { year: 'numeric', month: 'short', day: 'numeric' },
  shortDate: { year: '2-digit', month: 'numeric', day: 'numeric' },
  shortTime: { hour: 'numeric', minute: '2-digit' },
  monthYear: { year: 'numeric', month: 'short' },
};

const dateFormatters = new Map<string, Intl.DateTimeFormat>();
const numberFormatters = new Map<string, Intl.NumberFormat>();

export function formatDateValue(value: string | number | Date | null | undefined, format: DateFormat, language: string): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) {
    return String(value);
  }
  const key = `${language}|${format}`;
  let formatter = dateFormatters.get(key);
  if (!formatter) {
    formatter = new Intl.DateTimeFormat(localeFor(language), DATE_OPTIONS[format] ?? DATE_OPTIONS.medium);
    dateFormatters.set(key, formatter);
  }
  return formatter.format(date);
}

/** Angular-style digits info ("1.0-2": minimum integer digits, then minimum-maximum fraction digits); default "1.0-3". */
export function formatNumberValue(value: number | string | null | undefined, digitsInfo: string | undefined, language: string, useGrouping = true): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }
  const number = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(number)) {
    return String(value);
  }
  const match = /^(\d+)\.(\d+)-(\d+)$/.exec(digitsInfo ?? '1.0-3') ?? ['', '1', '0', '3'];
  const key = `${language}|${digitsInfo ?? ''}|${useGrouping}`;
  let formatter = numberFormatters.get(key);
  if (!formatter) {
    formatter = new Intl.NumberFormat(localeFor(language), {
      minimumIntegerDigits: Number(match[1]),
      minimumFractionDigits: Number(match[2]),
      maximumFractionDigits: Math.max(Number(match[2]), Number(match[3])),
      useGrouping,
    });
    numberFormatters.set(key, formatter);
  }
  return formatter.format(number);
}

/**
 * Numbers passed into translated sentences ("{{count}} rows"): in Arabic they get Arabic-Indic digits, grouped only from
 * 10,000 so that years and versions (2026, 3) read naturally; English text is left as it was.
 */
export function localizeParams<T extends Record<string, unknown>>(params: T, language: string): T {
  if (language !== 'ar') {
    return params;
  }
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(params)) {
    result[key] = typeof value === 'number' && Number.isFinite(value) ? formatNumberValue(value, '1.0-2', language, Math.abs(value) >= 10000) : value;
  }
  return result as T;
}
