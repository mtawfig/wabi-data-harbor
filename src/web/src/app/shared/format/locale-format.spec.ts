import { describe, expect, it } from 'vitest';
import { formatDateValue, formatNumberValue, localizeParams } from './locale-format';

describe('locale formats', () => {
  const when = new Date(2026, 10, 8, 16, 46, 34);

  it('formats dates in the interface language', () => {
    expect(formatDateValue(when, 'mediumDate', 'en')).toBe('Nov 8, 2026');
    expect(formatDateValue(when, 'medium', 'en')).toBe('Nov 8, 2026, 4:46:34 PM');
    expect(formatDateValue(when, 'short', 'en')).toBe('11/8/26, 4:46 PM');
    expect(formatDateValue(when, 'mediumDate', 'ar')).toBe('٨ نوفمبر ٢٠٢٦');
    expect(formatDateValue(when, 'medium', 'ar')).toContain('٤:٤٦:٣٤');
    expect(formatDateValue(when, 'medium', 'ar')).toContain('نوفمبر');
    expect(formatDateValue(null, 'medium', 'ar')).toBe('');
  });

  it('formats numbers with Angular-style digits info', () => {
    expect(formatNumberValue(1234.5, '1.0-0', 'en')).toBe('1,235');
    expect(formatNumberValue(1234.5, '1.2-2', 'en')).toBe('1,234.50');
    expect(formatNumberValue(58000, undefined, 'ar')).toBe('٥٨٬٠٠٠');
    expect(formatNumberValue('12.345', '1.0-2', 'en')).toBe('12.35');
  });

  it('gives numbers in Arabic sentences Arabic digits, grouping only large ones', () => {
    expect(localizeParams({ count: 6, year: 2026, rows: 12500, name: 'BR-DXB01' }, 'ar')).toEqual({ count: '٦', year: '٢٠٢٦', rows: '١٢٬٥٠٠', name: 'BR-DXB01' });
    expect(localizeParams({ count: 6 }, 'en')).toEqual({ count: 6 });
  });
});
