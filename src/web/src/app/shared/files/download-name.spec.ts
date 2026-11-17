import { downloadFileName } from './download-name';

describe('downloadFileName', () => {
  it('prefers the real (UTF-8) name over the plain one, so an Arabic title keeps its words', () => {
    const header = `attachment; filename="____-____-2025-v1-20261117-0911.xlsx"; filename*=UTF-8''%D8%B1%D8%AD%D9%84%D8%A7%D8%AA-%D8%A7%D9%84%D8%B9%D8%B1%D8%A8%D9%8A%D8%A9-2025-v1-20261117-0911.xlsx`;
    expect(downloadFileName(header, 'analysis.xlsx')).toBe('رحلات-العربية-2025-v1-20261117-0911.xlsx');
  });

  it('reads a plain name, quoted or not, and falls back when there is none or it cannot be decoded', () => {
    expect(downloadFileName('attachment; filename="flights-2025-v2.xlsx"', 'analysis.xlsx')).toBe('flights-2025-v2.xlsx');
    expect(downloadFileName('attachment; filename=flights.xlsx', 'analysis.xlsx')).toBe('flights.xlsx');
    expect(downloadFileName(`attachment; filename="plain.xlsx"; filename*=UTF-8''%E0%A4%A`, 'analysis.xlsx')).toBe('plain.xlsx');
    expect(downloadFileName(null, 'analysis.xlsx')).toBe('analysis.xlsx');
    expect(downloadFileName('attachment', 'analysis.xlsx')).toBe('analysis.xlsx');
  });
});
