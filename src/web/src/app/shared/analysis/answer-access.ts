import { AnalysisPanel, PanelRowScope } from '../../core/platform/ai.models';

const ORDER: Record<PanelRowScope['scope'], number> = { Self: 0, Section: 1, Department: 2 };

/**
 * The distinct row scopes that limited an answer's rows to what the asker may see, narrowest first. The page shows them
 * next to the answer so a permission-filtered result is never read as the whole organization.
 */
export function answerAccess(panels: readonly AnalysisPanel[]): PanelRowScope[] {
  const seen = new Map<string, PanelRowScope>();
  for (const panel of panels) {
    for (const scope of panel.rowScopes ?? []) {
      seen.set(`${scope.scope}|${scope.value}`, scope);
    }
  }
  return [...seen.values()].sort((a, b) => ORDER[a.scope] - ORDER[b.scope] || a.value.localeCompare(b.value));
}
