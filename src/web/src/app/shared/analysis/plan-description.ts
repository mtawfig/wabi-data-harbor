import { AnalysisPanel, DisplayLabel, PlanConditionView, PlanFilterGroupView, PlanSelectItemView } from '../../core/platform/ai.models';
import { formatNumberValue, localeFor } from '../format/locale-format';

/** Transloco's translate, passed in so the description stays a pure function that is easy to test. */
export type Translate = (key: string, params?: Record<string, unknown>) => string;

/**
 * A panel's validated query plan in plain words: where the data came from, what was calculated, how it was grouped,
 * filtered, sorted and capped. Built from the plan and the catalogue's display names only; the SQL never reaches the
 * browser. Each entry is ready to show; empty lists and null mean "not part of this query".
 */
export interface PlanDescription {
  title: string;
  sources: string[];
  measures: string[];
  columns: string[];
  groups: string[];
  filters: string | null;
  having: string[];
  order: string[];
  limit: number | null;
  /**
   * Result column (the plan's output alias, lower-case) → the header to show. In Arabic every column is named from the
   * plan and the catalogue's Arabic display names ("مجموع المبلغ"); in English the planner's alias reads well already, so
   * only a plain column with a catalogue name uses that name.
   */
  headers: Record<string, string>;
}

const ASKER: Record<string, string> = {
  '$asker.employee': 'ask.plan.asker.employee',
  '$asker.department': 'ask.plan.asker.department',
  '$asker.section': 'ask.plan.asker.section',
};
/** A date relative to today, written by the planner and resolved by the server at each run: `$month_start-1m`. */
const RELATIVE_DATE = /^\$(today|week_start|month_start|quarter_start|year_start)(?:([+-])(\d{1,4})([dwmqy]))?$/;
/** The unit each anchor moves by: one step back from the start of this month is "the start of last month". */
const ANCHOR_UNIT: Record<string, string> = { today: 'd', week_start: 'w', month_start: 'm', quarter_start: 'q', year_start: 'y' };
const UNITS = ['day', 'week', 'month', 'quarter', 'year'];
const AGGREGATES = ['count', 'count_distinct', 'sum', 'avg', 'min', 'max'];
const OPERATORS = ['eq', 'neq', 'gt', 'gte', 'lt', 'lte', 'in', 'not_in', 'between', 'contains', 'starts_with', 'is_null', 'not_null'];

export function describePanel(panel: AnalysisPanel, language: string, t: Translate): PlanDescription {
  const plan = panel.plan;
  const labels = new Map(Object.entries(panel.labels ?? {}).map(([key, value]) => [key.toLowerCase(), value]));
  const objects = [plan.source, ...(plan.joins ?? [])].filter((o): o is NonNullable<typeof o> => !!o?.object);
  const objectNames = new Map(objects.map((o) => [o.alias.toLowerCase(), o.object]));
  const select = plan.select ?? [];
  const byAlias = new Map(select.map((s) => [s.alias.toLowerCase(), s]));
  const list = (items: string[]) => items.join(language === 'ar' ? '، ' : ', ');

  // In Arabic, a name or value without Arabic letters (a column with no Arabic display name, a code such as BR-DXB01) is
  // isolated so the bidirectional algorithm keeps it whole and the surrounding Arabic punctuation in place.
  const isolate = (text: string): string => (language === 'ar' && text && !/[\u0600-\u06FF]/.test(text) ? `\u2068${text}\u2069` : text);

  const display = (label: DisplayLabel | undefined): string | null => {
    const text = language === 'ar' ? (label?.ar ?? label?.en) : (label?.en ?? label?.ar);
    return text?.trim() ? text.trim() : null;
  };

  /** "t.PostedAt" → the catalogue's name for that column, else the column name in words. */
  const column = (reference: string | null | undefined): string => {
    if (!reference) {
      return '';
    }
    const named = display(labels.get(reference.toLowerCase()));
    if (named) {
      return named;
    }
    const dot = reference.indexOf('.');
    return isolate(humanize(dot >= 0 ? reference.slice(dot + 1) : reference));
  };

  const object = (alias: string): string => display(labels.get(alias.toLowerCase())) ?? isolate(humanize(objectNames.get(alias.toLowerCase()) ?? alias));

  const item = (s: PlanSelectItemView): string => {
    const aggregate = s.aggregate?.toLowerCase();
    if (aggregate && AGGREGATES.includes(aggregate)) {
      if (aggregate === 'count' && (!s.column || s.column === '*')) {
        return t('ask.plan.agg.countRows');
      }
      return t(`ask.plan.agg.${aggregate}`, { column: column(s.column) });
    }
    const unit = s.dateTrunc?.toLowerCase();
    if (unit && UNITS.includes(unit)) {
      return t('ask.plan.dateTrunc', { unit: t(`ask.plan.unit.${unit}`), column: column(s.column) });
    }
    return column(s.column) || isolate(humanize(s.alias));
  };

  /** An output alias (group by, order by, having) as the item it names. */
  const alias = (name: string): string => {
    const s = byAlias.get(name.toLowerCase());
    return s ? item(s) : isolate(humanize(name));
  };

  /** "$month_start-1m" → "the start of last month"; "$today-7d" → "7 days before today". Never the placeholder itself. */
  const relativeDate = (anchor: string, sign: string | undefined, amount: string | undefined, unit: string | undefined): string => {
    const count = Number(amount ?? 0);
    if (!sign || !unit || count === 0) {
      return t(`ask.plan.relative.anchor.${anchor}`);
    }
    const direction = sign === '-' ? 'before' : 'after';
    if (count === 1 && unit === ANCHOR_UNIT[anchor]) {
      return t(`ask.plan.relative.${direction === 'before' ? 'previous' : 'next'}.${anchor}`);
    }
    // Plural forms differ by language (Arabic has one, two, few and many); a missing form falls back to "other".
    const category = new Intl.PluralRules(language === 'ar' ? 'ar' : 'en').select(count);
    const params = { count: formatNumberValue(count, '1.0-0', language, false) };
    const key = `ask.plan.relative.unit.${unit}.${category}`;
    const span = t(key, params);
    return t(`ask.plan.relative.${direction}`, { amount: span === key ? t(`ask.plan.relative.unit.${unit}.other`, params) : span, anchor: t(`ask.plan.relative.anchor.${anchor}`) });
  };

  const value = (raw: string): string => {
    const asker = ASKER[raw.trim().toLowerCase()];
    if (asker) {
      return t(asker);
    }
    const relative = RELATIVE_DATE.exec(raw.trim());
    if (relative) {
      return relativeDate(relative[1], relative[2], relative[3], relative[4]);
    }
    if (/^(true|false)$/i.test(raw.trim())) {
      return t(raw.trim().toLowerCase() === 'true' ? 'ask.plan.yes' : 'ask.plan.no');
    }
    return isolate(literal(raw, language));
  };

  const condition = (c: PlanConditionView, name: (reference: string) => string): string => {
    const op = c.operator?.toLowerCase();
    const subject = name(c.column);
    const values = (c.values ?? []).map(value);
    if (!OPERATORS.includes(op)) {
      return [subject, c.operator, ...values].filter(Boolean).join(' ');
    }
    if (c.compareTo) {
      return t(`ask.plan.op.${op}`, { column: subject, value: name(c.compareTo) });
    }
    if (op === 'between') {
      return t('ask.plan.op.between', { column: subject, from: values[0] ?? '', to: values[1] ?? '' });
    }
    return t(`ask.plan.op.${op}`, { column: subject, value: list(values) });
  };

  const group = (g: PlanFilterGroupView | null | undefined): string => {
    if (!g) {
      return '';
    }
    const parts = [
      ...(g.conditions ?? []).map((c) => condition(c, column)),
      ...(g.groups ?? []).map(group).filter(Boolean).map((text) => `(${text})`),
    ];
    const joiner = ` ${t(g.logic?.toLowerCase() === 'or' ? 'ask.plan.or' : 'ask.plan.and')} `;
    return parts.join(joiner);
  };

  const aggregated = select.some((s) => !!s.aggregate);
  const headers: Record<string, string> = {};
  for (const s of select) {
    const plain = !s.aggregate && !s.dateTrunc;
    const named = plain ? display(labels.get((s.column ?? '').toLowerCase())) : null;
    headers[s.alias.toLowerCase()] = language === 'ar' ? item(s) : (named ?? humanize(s.alias));
  }
  return {
    title: panel.title || plan.title || plan.objective,
    sources: [...new Set(objects.map((o) => object(o.alias)))],
    measures: select.filter((s) => !!s.aggregate).map(item),
    columns: aggregated ? [] : select.map(item),
    groups: aggregated ? (plan.groupBy ?? []).map(alias) : [],
    filters: group(plan.filters) || null,
    having: (plan.having ?? []).map((c) => condition(c, alias)),
    order: (plan.orderBy ?? []).map((o) => t(o.direction?.toLowerCase() === 'desc' ? 'ask.plan.desc' : 'ask.plan.asc', { column: alias(o.alias) })),
    limit: plan.limit ?? null,
    headers,
  };
}

/** "HomeBranchCode" → "Home branch code"; "vw_TransactionsLive" → "Transactions live"; "GroupNameAr" → "Group name (AR)". */
export function humanize(name: string): string {
  const words = name
    .replace(/^(vw|v)_/i, '')
    .replace(/([a-z])(Ar|En)$/, (_, last: string, code: string) => `${last} (${code.toUpperCase()})`)
    .replace(/_/g, ' ')
    .replace(/([a-z\d])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .trim()
    .split(/\s+/)
    .map((w, i) => (i > 0 && !/^\(?[A-Z\d]{2,}\)?$/.test(w) ? w.toLowerCase() : w));
  const text = words.join(' ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** A literal as people read it: numbers and dates in the interface language, anything else unchanged. */
function literal(raw: string, language: string): string {
  const text = raw.trim();
  if (/^-?\d+(\.\d+)?$/.test(text)) {
    const n = Number(text);
    // Codes with leading zeros stay as written.
    return /^-?0\d/.test(text) ? text : formatNumberValue(n, '1.0-4', language, Math.abs(n) >= 10000);
  }
  const date = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2}))?)?/.exec(text);
  if (date) {
    const [, y, m, d, hh, mm] = date;
    const hasTime = !!hh && (hh !== '00' || mm !== '00');
    const value = new Date(Date.UTC(+y, +m - 1, +d, +(hh ?? 0), +(mm ?? 0)));
    if (!Number.isNaN(value.getTime())) {
      return new Intl.DateTimeFormat(localeFor(language), {
        year: 'numeric', month: 'short', day: 'numeric', timeZone: 'UTC',
        ...(hasTime ? { hour: 'numeric', minute: '2-digit' } : {}),
      }).format(value);
    }
  }
  return text;
}
