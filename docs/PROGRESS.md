# Wabi Insight — Progress Log

| Date | PR | Phase | Status | Notes |
|---|---|---|---|---|
| 2026-11-05 | P1.1 | 1 — Foundation | In review | Solution scaffolding: .NET 10 API/Core/Infrastructure, unit + integration test projects, health and platform-info endpoints, Angular 22 shell calling the API, ESLint/Vitest, GitHub Actions CI (SQL Server service container), docker-compose for local SQL Server, `ARCHITECTURE.md` skeleton. 7 .NET tests + 2 Angular tests green. |
| 2026-11-05 | P0.1 | 0 — Planning | In review | Product named **Wabi Insight**. Revised specification v1.1, spec review with owner decisions C1–C6, implementation plan (12 phases, 26 PRs), `CLAUDE.md`. No application code yet. |

## Decisions
- 2026-11-05: Owner accepted the defaults for C1–C6 in `docs/SPEC_REVIEW.md` (QuestPDF behind `IPdfRenderer`, vector landing illustrations, optional Anthropic adapter, mocked Drive/OneDrive until OAuth apps exist, per-user counts only in insights, Platform Administrators may open ticket attachments).

## Blocked / awaiting input
- None.

## Environment notes
- Cloud container: Node 22.23, Docker 29.9; .NET 10 SDK installed with `scripts/install-dotnet.sh`; SQL Server not yet needed (P1.2 adds Testcontainers locally and the CI service container is already configured).
