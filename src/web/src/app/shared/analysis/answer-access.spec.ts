import { describe, expect, it } from 'vitest';
import { AnalysisPanel } from '../../core/platform/ai.models';
import { answerAccess } from './answer-access';

const panel = (rowScopes?: AnalysisPanel['rowScopes']): AnalysisPanel => ({
  title: 't',
  plan: { objective: 'o', assumptions: [] } as unknown as AnalysisPanel['plan'],
  compiledSqlPreview: 'SELECT 1',
  result: { columns: [], rows: [], rowCount: 0, truncated: false, kpis: [], series: [], durationMs: 0 } as unknown as AnalysisPanel['result'],
  rowScopes,
});

describe('answerAccess', () => {
  it('is empty when no panel was limited by a row scope (unrestricted answers and older stored answers)', () => {
    expect(answerAccess([panel(), panel(null), panel([])])).toEqual([]);
  });

  it('merges the scopes of every panel, without duplicates, narrowest first', () => {
    const scopes = answerAccess([
      panel([{ scope: 'Department', value: 'BR-DXB01' }]),
      panel([{ scope: 'Self', value: 'W1004' }, { scope: 'Department', value: 'BR-DXB01' }]),
    ]);
    expect(scopes).toEqual([{ scope: 'Self', value: 'W1004' }, { scope: 'Department', value: 'BR-DXB01' }]);
  });
});
