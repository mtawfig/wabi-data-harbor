import { describe, expect, it } from 'vitest';
import { AnalysisResult } from '../../core/platform/ai.models';
import { DISPLAY_ROWS, longList, shownRows } from './long-list';

function result(rows: number, truncated = false, totalRows?: number | null): AnalysisResult {
  return { columns: [{ name: 'id', dataType: 'int', isNumeric: true, isTemporal: false }], rows: Array.from({ length: rows }, (_, i) => [i + 1]), rowCount: rows, truncated, totalRows, kpis: [], series: [], durationMs: 1 };
}

describe('long lists', () => {
  it('shows the first hundred rows of a longer list and every row of a shorter one', () => {
    expect(shownRows(result(312))).toHaveLength(DISPLAY_ROWS);
    expect(shownRows(result(312))[99]).toEqual([100]);
    expect(shownRows(result(68))).toHaveLength(68);
    expect(shownRows(result(100))).toHaveLength(100);
  });

  it('has no note for a list that fits in its table, a requested top-N included', () => {
    expect(longList(result(0))).toBeNull();
    expect(longList(result(5))).toBeNull();
    expect(longList(result(100))).toBeNull();
  });

  it('states the rows shown and the rows the list has', () => {
    // A list that is whole but longer than the table: its own number of rows.
    expect(longList(result(312))).toEqual({ shown: 100, total: 312 });
    // A list that stopped at the size limit: the count the database gave.
    expect(longList(result(1000, true, 10555))).toEqual({ shown: 100, total: 10555 });
    // The count could not be taken: the note gives the rows shown only.
    expect(longList(result(1000, true))).toEqual({ shown: 100, total: null });
    expect(longList(result(1000, true, null))).toEqual({ shown: 100, total: null });
  });
});
