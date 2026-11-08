/**
 * Numbers as people read them: thousands separators and at most two decimals, in the interface language
 * (Arabic-Indic digits in Arabic, as in answer tables). Used wherever a raw number would otherwise be shown.
 */
export function formatNumber(value: number, language: string, maximumFractionDigits = 2): string {
  return value.toLocaleString(language === 'ar' ? 'ar-EG' : 'en-US', { maximumFractionDigits });
}

/** Formats numbers and numeric strings; anything else is returned as text unchanged. */
export function formatValue(value: unknown, language: string): string {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return formatNumber(value, language);
  }
  if (typeof value === 'string' && /^-?\d+(\.\d+)?$/.test(value.trim())) {
    return formatNumber(Number(value), language);
  }
  return value === null || value === undefined ? '' : String(value);
}
