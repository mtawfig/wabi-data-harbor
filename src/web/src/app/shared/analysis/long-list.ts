import { AnalysisResult } from '../../core/platform/ai.models';

/** The most rows a table shows on the page; a longer list says so under the table and is whole in Excel (P13.50). */
export const DISPLAY_ROWS = 100;

/** The rows a table shows: the first hundred. */
export function shownRows(result: AnalysisResult): AnalysisResult['rows'] {
  return result.rows.length > DISPLAY_ROWS ? result.rows.slice(0, DISPLAY_ROWS) : result.rows;
}

/**
 * For a list longer than its table: how many rows the table shows and how many the list has. The total is the
 * database's count when the list stopped at the size limit, and null when that count is not known. A list that fits
 * in its table (a "top five" included) has no note.
 */
export function longList(result: AnalysisResult): { shown: number; total: number | null } | null {
  if (!result.truncated && result.rows.length <= DISPLAY_ROWS) {
    return null;
  }
  const shown = Math.min(result.rows.length, DISPLAY_ROWS);
  const total = result.truncated ? (result.totalRows ?? null) : result.rows.length;
  return { shown, total: total !== null && total > shown ? total : null };
}
