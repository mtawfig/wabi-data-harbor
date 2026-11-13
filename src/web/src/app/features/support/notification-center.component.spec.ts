import { isolated } from './notification-center.component';

describe('notification texts', () => {
  it('wraps text values in direction isolates, so a name in the other language keeps the sentence punctuation in place', () => {
    const values = isolated({ ruleName: 'Same account, several devices (employee portal)', alerts: 4, newStatus: '', reference: undefined });
    expect(values.ruleName).toBe('⁨Same account, several devices (employee portal)⁩');
    // Numbers and empty values are left as they are: numbers are formatted for the language by the transpiler.
    expect(values.alerts).toBe(4);
    expect(values.newStatus).toBe('');
    expect(values.reference).toBeUndefined();
  });
});
