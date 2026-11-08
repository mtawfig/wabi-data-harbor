import { describe, expect, it } from 'vitest';
import { formatNumber, formatValue } from './number-format';

describe('formatNumber', () => {
  it('adds thousands separators and keeps at most two decimals', () => {
    expect(formatNumber(58000, 'en')).toBe('58,000');
    expect(formatNumber(1234567.891, 'en')).toBe('1,234,567.89');
  });

  it('uses Arabic-Indic digits in Arabic', () => {
    expect(formatNumber(58000, 'ar')).toBe('٥٨٬٠٠٠');
  });
});

describe('formatValue', () => {
  it('formats numbers and numeric strings, and leaves other text alone', () => {
    expect(formatValue(55000.5, 'en')).toBe('55,000.5');
    expect(formatValue('58000', 'en')).toBe('58,000');
    expect(formatValue('2026-11', 'en')).toBe('2026-11');
    expect(formatValue('BR-DXB01', 'en')).toBe('BR-DXB01');
    expect(formatValue(null, 'en')).toBe('');
  });
});
