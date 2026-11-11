import { describe, expect, it } from 'vitest';
import ar from '../../../../public/i18n/ar.json';
import en from '../../../../public/i18n/en.json';
import { AnalysisPanel } from '../../core/platform/ai.models';
import { Translate, describePanel, humanize } from './plan-description';

/** A minimal translate over the real EN/AR files: nested keys and {{param}} interpolation, as Transloco does. */
function translator(language: 'en' | 'ar'): Translate {
  const file = (language === 'ar' ? ar : en) as unknown as Record<string, unknown>;
  return (key, params) => {
    const text = key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], file);
    if (typeof text !== 'string') {
      throw new Error(`missing ${language} translation ${key}`);
    }
    return text.replace(/{{\s*(\w+)\s*}}/g, (_, name: string) => String(params?.[name] ?? ''));
  };
}

const result = { columns: [], rows: [], rowCount: 0, truncated: false, kpis: [], series: [], durationMs: 0 } as unknown as AnalysisPanel['result'];

/** Branch totals of large cash deposits by month, for the asker's own department, with a minimum total. */
const panel: AnalysisPanel = {
  title: 'Large cash deposits by branch',
  result,
  plan: {
    title: 'Large cash deposits by branch',
    objective: 'o',
    assumptions: [],
    source: { schema: 'dbo', object: 'vw_TransactionsLive', alias: 't' },
    joins: [{ schema: 'dbo', object: 'Branches', alias: 'b' }],
    select: [
      { alias: 'branch', column: 'b.BranchName' },
      { alias: 'month', column: 't.PostedAt', dateTrunc: 'month' },
      { alias: 'total_amount', column: 't.Amount', aggregate: 'sum' },
      { alias: 'postings', aggregate: 'count' },
    ],
    filters: {
      logic: 'and',
      conditions: [
        { column: 't.TransactionCode', operator: 'eq', values: ['CDP'] },
        { column: 't.PostedAt', operator: 'between', values: ['2025-10-01', '2025-12-31'] },
        { column: 't.PostingUnitCode', operator: 'eq', values: ['$asker.department'] },
      ],
      groups: [{ logic: 'or', conditions: [{ column: 't.Amount', operator: 'gte', values: ['50000'] }, { column: 't.Channel', operator: 'in', values: ['Branch', 'ATM'] }] }],
    },
    groupBy: ['branch', 'month'],
    having: [{ column: 'total_amount', operator: 'gt', values: ['100000'] }],
    orderBy: [{ alias: 'total_amount', direction: 'desc' }],
    limit: 50,
  },
  labels: {
    t: { en: 'Transactions (live)', ar: 'المعاملات (مباشر)' },
    b: { en: 'Branches', ar: 'الفروع' },
    'b.BranchName': { en: 'Branch name', ar: 'اسم الفرع' },
    't.PostedAt': { en: 'Posted at', ar: 'وقت الترحيل' },
    't.Amount': { en: 'Amount', ar: 'المبلغ' },
    't.TransactionCode': { en: 'Transaction code', ar: 'رمز المعاملة' },
  },
};

describe('describePanel', () => {
  it('explains a grouped, filtered and sorted query in plain English, with catalogue names', () => {
    const d = describePanel(panel, 'en', translator('en'));
    expect(d.title).toBe('Large cash deposits by branch');
    expect(d.sources).toEqual(['Transactions (live)', 'Branches']);
    expect(d.measures).toEqual(['Total Amount', 'Number of rows']);
    expect(d.columns).toEqual([]);
    expect(d.groups).toEqual(['Branch name', 'Posted at by month']);
    expect(d.filters).toBe(
      'Transaction code is CDP and Posted at is between Oct 1, 2025 and Dec 31, 2025 and Posting unit code is your department'
        + ' and (Amount is at least 50,000 or Channel is one of Branch, ATM)',
    );
    expect(d.having).toEqual(['Total Amount is more than 100,000']);
    expect(d.order).toEqual(['Total Amount (descending)']);
    expect(d.limit).toBe(50);
  });

  it('uses the Arabic catalogue names, Arabic digits and month names in Arabic', () => {
    const raw = describePanel(panel, 'ar', translator('ar'));
    // Names and codes without Arabic letters are isolated so they stay whole inside the Arabic sentence.
    expect(raw.filters).toContain('يساوي \u2068CDP\u2069');
    const plain = (text: string | null) => (text ?? '').replace(/[\u2068\u2069]/g, '');
    const d = { ...raw, filters: plain(raw.filters), groups: raw.groups.map(plain), having: raw.having.map(plain), measures: raw.measures.map(plain), sources: raw.sources.map(plain) };
    expect(d.sources).toEqual(['المعاملات (مباشر)', 'الفروع']);
    expect(d.measures[0]).toBe('إجمالي المبلغ');
    expect(d.groups).toEqual(['اسم الفرع', 'وقت الترحيل حسب الشهر']);
    expect(d.filters).toContain('رمز المعاملة يساوي CDP');
    expect(d.filters).toContain('إدارتك');
    expect(d.filters).toContain('أكتوبر');
    expect(d.having).toEqual(['إجمالي المبلغ أكبر من ١٠٠٬٠٠٠']);
  });

  it('names table columns and chart series from the catalogue in Arabic, and keeps the planner alias in English', () => {
    expect(describePanel(panel, 'ar', translator('ar')).headers).toEqual({
      branch: 'اسم الفرع',
      month: 'وقت الترحيل حسب الشهر',
      total_amount: 'إجمالي المبلغ',
      postings: 'عدد الصفوف',
    });
    expect(describePanel(panel, 'en', translator('en')).headers).toEqual({ branch: 'Branch name', month: 'Month', total_amount: 'Total amount', postings: 'Postings' });
  });

  it('falls back to readable column names for answers stored before labels existed, and never shows SQL', () => {
    const older: AnalysisPanel = { ...panel, labels: undefined };
    const d = describePanel(older, 'en', translator('en'));
    expect(d.sources).toEqual(['Transactions live', 'Branches']);
    expect(d.groups).toEqual(['Branch name', 'Posted at by month']);
    expect(JSON.stringify(d)).not.toMatch(/SELECT|FROM \[|dbo\./);
  });

  it('describes a plain list (no totals), a column comparison, empty checks and the asker placeholder for their own employee number', () => {
    const list: AnalysisPanel = {
      title: '',
      result,
      plan: {
        title: '',
        objective: 'Leavers whose access outlived their contract',
        assumptions: [],
        source: { object: 'Employees', alias: 'e' },
        select: [{ alias: 'name', column: 'e.FullName' }, { alias: 'left_on', column: 'e.TerminationDate' }],
        filters: {
          conditions: [
            { column: 'e.RevokedDate', operator: 'gt', compareTo: 'e.TerminationDate' },
            { column: 'e.TerminationDate', operator: 'not_null' },
            { column: 'e.EmployeeNumber', operator: 'neq', values: ['$asker.employee'] },
            { column: 'e.IsActive', operator: 'eq', values: ['false'] },
          ],
        },
        orderBy: [{ alias: 'left_on', direction: 'asc' }],
      },
    };
    const d = describePanel(list, 'en', translator('en'));
    expect(d.title).toBe('Leavers whose access outlived their contract');
    expect(d.measures).toEqual([]);
    expect(d.columns).toEqual(['Full name', 'Termination date']);
    expect(d.groups).toEqual([]);
    expect(d.filters).toBe('Revoked date is more than Termination date and Termination date is not empty and Employee number is not your employee number and Is active is no');
    expect(d.order).toEqual(['Termination date (ascending)']);
    expect(d.limit).toBeNull();
    expect(d.having).toEqual([]);
  });

  it('describes dates relative to today in words, never as the placeholder, in both languages', () => {
    const panel = (values: string[], operator: string): AnalysisPanel => ({
      title: '',
      result,
      plan: {
        title: '', objective: 'Orders', assumptions: [],
        source: { object: 'Orders', alias: 'o' },
        select: [{ alias: 'orders', aggregate: 'count' }],
        filters: { conditions: [{ column: 'o.OrderDate', operator, values }] },
      },
    });
    const text = (values: string[], operator: string, language: 'en' | 'ar') => describePanel(panel(values, operator), language, translator(language)).filters;

    expect(text(['$month_start'], 'gte', 'en')).toBe('Order date is at least the start of this month');
    expect(text(['$month_start-1m', '$today'], 'between', 'en')).toBe('Order date is between the start of last month and today');
    expect(text(['$today-1d'], 'eq', 'en')).toBe('Order date is yesterday');
    expect(text(['$today-7d'], 'gte', 'en')).toBe('Order date is at least 7 days before today');
    expect(text(['$year_start+1m'], 'lt', 'en')).toBe('Order date is less than 1 month after the start of this year');
    for (const values of [['$quarter_start-2q'], ['$week_start+1w'], ['$today-2d'], ['$today-5d'], ['$today-11d'], ['$today-100d']]) {
      for (const language of ['en', 'ar'] as const) {
        const described = text(values, 'gte', language) ?? '';
        expect(described).not.toContain('$');
        expect(described).not.toContain('ask.plan');
      }
    }
    expect(text(['$today-2d'], 'gte', 'ar')).toContain('يومان قبل اليوم');
    expect(text(['$month_start-1m'], 'gte', 'ar')).toContain('بداية الشهر الماضي');
  });

  it('turns identifiers into words', () => {
    expect(humanize('HomeBranchCode')).toBe('Home branch code');
    expect(humanize('vw_TransactionsLive')).toBe('Transactions live');
    expect(humanize('total_amount')).toBe('Total amount');
    expect(humanize('IBAN')).toBe('IBAN');
    expect(humanize('CustomerIBAN')).toBe('Customer IBAN');
    expect(humanize('GroupNameAr')).toBe('Group name (AR)');
    expect(humanize('Year')).toBe('Year');
  });
});
