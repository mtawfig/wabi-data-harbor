# Wabi Insight — Review of the Master Development Prompt

**Reviewed:** 2026-11-05 · **Input:** "WABI INSIGHT — Master Development Prompt for Claude Code — Multi-Organization Edition"
**Output:** [`docs/WABI_INSIGHT_SPECIFICATION.md`](./WABI_INSIGHT_SPECIFICATION.md) v1.1 (revised spec) and [`docs/IMPLEMENTATION_PLAN.md`](./IMPLEMENTATION_PLAN.md).

The prompt is strong on security intent and scope. The points below are where it conflicts with itself, leaves a decision open that code cannot leave open, or asks for something that needs a business/licensing decision. Each item says what was changed in the revised specification. Items marked **DECISION NEEDED** are ones the owner should confirm or override; everything else was resolved with a stated default.

## A. Conflicts and corrections

| # | Original prompt | Problem | Resolution in v1.1 |
|---|---|---|---|
| A1 | §3 Public Area lists a "Protected first-time Platform Administrator bootstrap" page; §7.2 asks for a "protected initialization command". | Two mechanisms with different attack surfaces; a public web page that creates the first administrator must not be able to run migrations. | Both kept, one implementation: a `bootstrap` CLI command for headless deployments and a `POST /api/bootstrap` endpoint that requires `Bootstrap:InitializationSecret`, is active only while `BootstrapCompleted = false`, and never applies migrations. The Angular bootstrap page is a thin client of that endpoint and shows status afterwards. Migrations are applied by `dotnet ef database update` / `migrate` command at deploy time (§7.2 step 3 changed). |
| A2 | §11 "Each application is owned by one organization, except the special built-in Platform Insights application." | A nullable `OrganizationId` on `Applications` would weaken the mandatory-ownership rule in §4.2. | `Applications.OrganizationId` is always required. The built-in application is provisioned as one row **per organization** with `IsBuiltIn = true`, `BuiltInCode = 'WI-INSIGHTS'` and no connection string. The *catalog* (objects, columns, metric definitions) is global; the *application row* is tenant-owned. |
| A3 | §31 phases omit the Organization Administrator Dashboard (§14) entirely. | Required feature with no phase. | Added to Phase 7 (P7.2) because it reads the same tenant-scoped insights views. |
| A4 | §31 Phase 7 "Natural-language analytics for organization usage" precedes Phase 8 (AI engine). | NL queries cannot run before the query-plan engine exists. | Phase 7 delivers the data source, catalog, metric API and dashboards. NL questions over Platform Insights become live in Phase 8 and Platform Insights is the first data source the engine is tested against. |
| A5 | §31 Phase 3 (Platform Dashboard) lists AI usage and support-ticket KPIs before Phases 4 and 8 exist. | Cannot show data that is not produced yet. | Phase 3 builds the dashboard framework, usage-event pipeline, organization/user KPIs and health. Phases 4, 8, 9 and 10 each add their own KPI cards, charts and tables to the dashboard. |
| A6 | §15.3 status "Completed / Resolved" with code `RESOLVED`; §15.8 "Ticket reopened, if supported". | Ambiguous label and an undecided transition. | One status `RESOLVED` labelled "Resolved" (AR: "تم الحل"). Reopen is supported: `RESOLVED → STARTED` within 14 days by the requester, an OrganizationAdmin of that organization, or a Platform Administrator, recorded as `TicketReopened`. Full transition table is in §15.3 of the spec. |
| A7 | §4.4 allows multiple organizations to share a physical business database "if ever". | Supporting shared source databases safely needs a mapping model, UI and tests that v1 does not otherwise need. | **Not supported in v1.** Every configured connection is treated as organization-dedicated; a connection string already registered by another organization is rejected (hash comparison). Shared physical sources are a v2 feature. |
| A8 | §8 "Use ASP.NET Core Identity" with two separate account tables. | Full ASP.NET Core Identity assumes one user type per sign-in manager and brings tables we do not want (`AspNetUsers`). | Use Identity *primitives* only: `PasswordHasher<T>`, `UpperInvariantLookupNormalizer`, lockout and security-stamp semantics re-implemented in `AuthenticationService` over the two custom tables. One cookie scheme; the principal carries `principal_type` (`PlatformAdministrator` or `OrganizationUser`). |
| A9 | §27 lists `UserActivityEvents`, `ReportGenerationEvents`, `EmailDraftEvents`, `IntegrationOperationLogs`, `AnalysisExecutionMetrics` and also says "reuse existing tables … rather than duplicating". | Five near-identical event tables. | One append-only `UsageEvents` table (nullable `OrganizationId` for platform-level events) is the metrics source of truth. Only `AnalysisExecutionMetrics` (tokens, cost, durations) and `IntegrationOperationLogs` (provider request/response diagnostics) remain as separate tables because they carry different columns. `EmailMessages` (outbox) distinguishes drafts from sent mail. |
| A10 | §28 lists `/api/organization` (singular) next to plural resources. | Inconsistent naming. | `/api/organization` is kept as the "current organization" resource (profile, settings, dashboard); collection resources stay plural. |
| A11 | §7.1 says database initialization and bootstrap are tied; §7.2 step 3 "Create the required database schema/migrations". | A web request that applies migrations is a deployment anti-pattern. | See A1. |
| A12 | §16.1 "For ordinary requests created by non-admin users, the Organization Administrator must approve" leaves the OrganizationAdmin-as-requester case open. | Undefined approval rule. | Every grant needs approval by an active OrganizationAdmin of that organization. If the requester is an OrganizationAdmin they may approve their own request; the approval is recorded separately and the organization setting `RequireSecondAdminApprovalForSupportAccess` (default off) can force a different administrator. |

## B. Gaps filled with stated defaults

| # | Topic | Default chosen |
|---|---|---|
| B1 | Primary keys | UUID v7 (`Guid.CreateVersion7()`), non-enumerable, index-friendly. Ticket references remain human-readable. |
| B2 | Password policy | Minimum 12, maximum 128 characters, no composition rules (NIST 800-63B), reject the known sample password in production, reject passwords equal to the email. |
| B3 | Session policy | Sliding idle timeout 30 min, absolute lifetime 12 h, both platform-configurable; cookie `HttpOnly; Secure; SameSite=Lax`; antiforgery token via `XSRF-TOKEN` cookie + `X-XSRF-TOKEN` header; security stamp re-validated at most every 5 minutes. |
| B4 | Lockout | 5 failed attempts → 15 min lockout (configurable); identical response for unknown email and wrong password. |
| B5 | Suspended organization policy | Users cannot sign in and existing sessions are rejected; data is retained; the organization's tickets stay visible to Platform Administrators; scheduled/background work for it is skipped. |
| B6 | Ticket visibility | Requester sees own tickets; OrganizationAdmin sees all tickets of the organization; Platform Administrators see the platform queue. Attachment and comment visibility follow the ticket. |
| B7 | Ticket numbering | `TicketSequences(Year, NextValue)` row updated atomically (`UPDATE … OUTPUT`) inside the ticket-create transaction; format `WI-YYYY-NNNNNN`, resets yearly. |
| B8 | Email delivery | SMTP via MailKit; development uses a file-sink provider; all mail is written to an `EmailMessages` outbox first and sent by an in-process background sender with retry. |
| B9 | Artifact and attachment storage | `IArtifactStorage` abstraction; v1 provider is a local directory outside the web root with random object names; downloads go through authorized API endpoints only. Attachment limit 10 MB, allow-list of types (pdf, png, jpg, xlsx, docx, csv, txt, log, zip). |
| B10 | Background work | In-process `IHostedService` workers fed by bounded `Channel<T>` (email sending, report rendering, metadata refresh). No broker. |
| B11 | Data classification | `Internal` (default), `Confidential`, `Restricted`. External delivery of `Restricted` is blocked; `Confidential` requires explicit per-send disclosure approval by the owner and is logged; `Internal` requires the standard confirmation. |
| B12 | Role combination | Effective permission = union across the user's roles inside the organization. Row scopes combine with OR. Organization boundary and application ownership are evaluated first and are never widened by any role. |
| B13 | Row-scope mapping semantics | Per registered object, `RowScopeColumnMappings` names the column holding the employee identifier, department code and section code. Values are compared against `OrganizationUsers.EmployeeNumber`, `Departments.DepartmentCode`, `Sections.SectionCode` of the authenticated user. A scope whose mapping is missing yields **no rows**, never all rows. |
| B14 | Platform Insights implementation | SQL views in an `insights` schema of the configuration DB, every view exposes `OrganizationId`; queried through a dedicated login with SELECT on `insights` only; the compiler injects `OrganizationId = @tenant` as a mandatory predicate. Raw tables are never registered. |
| B15 | AI providers | Two adapters cover the list: OpenAI-compatible chat completions (OpenAI, Mistral, local servers such as Ollama/vLLM) and Gemini. Anthropic Messages API is listed as an optional third adapter. Structured output is requested via JSON schema / JSON mode where the provider supports it and validated regardless. |
| B16 | Frontend stack | Angular 22 standalone components + signals, Angular Material (M3 theme in brand colours, built-in RTL), Transloco for runtime EN/AR switching, ECharts (`ngx-echarts`) for charts with Arabic labels. |
| B17 | Backend layout | One deployable Web API (`WabiInsight.Api`) plus two class libraries (`WabiInsight.Core` – domain, services, interfaces; `WabiInsight.Infrastructure` – EF Core, providers). This is build organization inside Layer 2, not additional logical layers. |
| B18 | Testing strategy | xUnit; unit tests for compilers/validators/state machines; integration tests against real SQL Server (Testcontainers locally, GitHub Actions service container in CI). Oracle live tests run only when `WI_TEST_ORACLE_CONNECTION` is set. Angular: Vitest unit tests + Playwright screenshots. |
| B19 | Platform Administrator safety | An administrator cannot deactivate or delete themselves; at least one active Platform Administrator must always remain; the bootstrap administrator can be deactivated by another administrator but the record is never deleted. |
| B20 | Time handling | All timestamps stored in UTC (`datetime2`), rendered in the user's preferred timezone. |
| B21 | Audit immutability | `AuditLogs`, `UsageEvents`, `SupportTicketEvents` are append-only: the runtime DB login has no UPDATE/DELETE on them; migrations run under a separate elevated login. |

## C. Business and licensing decisions — DECISION NEEDED

| # | Topic | Recommendation | Why it needs you |
|---|---|---|---|
| C1 | **PDF library** | QuestPDF (supports RTL and Arabic shaping, actively maintained). | QuestPDF's Community licence is free only for organizations under USD 1M annual revenue; commercial deployment above that needs a paid licence. Alternative with no licence cost: HTML → PDF through headless Chromium (Playwright), which adds a Chromium dependency on the server. Spec v1.1 assumes **QuestPDF** and keeps the renderer behind `IPdfRenderer` so it can be swapped. |
| C2 | **Landing-page imagery** | SVG/vector illustrations drawn for the two scenes. | "Photorealistic visuals" require licensed stock photography or AI-generated images that you approve for commercial use. Vectors are produced in-repo with no licence risk; photos can replace them later. |
| C3 | **Optional Anthropic provider** | Add as a third adapter behind the same interface. | Not in the original list; costs nothing to include, but it is your product roadmap. |
| C4 | **Google Drive / OneDrive OAuth apps** | Required for Phase 10 to be tested live. | Needs a Google Cloud project and an Azure app registration owned by Wabitex. Development uses mocked providers; live verification needs those credentials. |
| C5 | **Platform Insights user-level detail** | Organization Administrators see per-department and per-role aggregates and per-user counts only; no prompt text. | §12.1 says to avoid exposing individual activity "without appropriate authorization". The default above is privacy-conservative; you may want per-user detail for OrganizationAdmin. |
| C6 | **Platform Administrator access to ticket attachments** | Allowed (support cannot work otherwise), logged, and the uploader is warned not to attach business data. | This is the one path by which organization-provided content reaches a Platform Administrator by design. |

## D. Environment requirements (documented, not blockers for planning)

- Local development needs the .NET 10 SDK, Node 22, and Docker (for SQL Server 2022). The planning container had Node and Docker but no .NET SDK; the SDK is installed by script in P1.1 and in CI.
- CI runs SQL Server as a GitHub Actions service container. Oracle live tests are optional.
- Email, Google Drive, OneDrive and AI providers need credentials supplied through user-secrets or environment variables; none are committed.
- Real screenshots require the full stack running; P1.1 adds a `docker-compose.dev.yml` and P11.2 adds the Playwright capture script.

## E. Naming decision (2026-11-05)

The owner selected **Wabi Insight** as the product name, replacing "Query Harbor" from the original prompt. Applied everywhere:

| Item | Old | New |
|---|---|---|
| Product name | Query Harbor | Wabi Insight |
| Specification file | `docs/QUERY_HARBOR_SPECIFICATION.md` | `docs/WABI_INSIGHT_SPECIFICATION.md` |
| .NET project prefix | `QueryHarbor.*` | `WabiInsight.*` |
| Built-in application | Query Harbor Platform Insights (`QH-INSIGHTS`) | Platform Insights (`WI-INSIGHTS`) |
| Ticket reference | `QH-YYYY-NNNNNN` | `WI-YYYY-NNNNNN` |
| Branch prefix | `qh/` | `wi/` |
| Test environment variable | `QH_TEST_ORACLE_CONNECTION` | `WI_TEST_ORACLE_CONNECTION` |

Unchanged: platform code `AP-AIR`, tagline "Ask Your Data. Trust the Answer." / "اسأل بياناتك. وثق بالإجابة.", the lighthouse-and-waves logo brief. Trademark and domain checks for "Wabi Insight" are the owner's responsibility before public release.

## F. Post-login landing page (2026-11-05)

The owner asked for organization users to land on the "Ask Wabi Insight" prompting page after sign-in. The original §22 routed Organization Users and Organization Administrators to their dashboards. Applied: `OrganizationUser → /app/ask`, with Dashboard one click away in the sidebar; `PlatformAdministrator → /platform/overview`. A `returnUrl` from a guarded deep link still takes precedence, and a `MustChangePassword` session always lands on `/change-password` first.

## G. Committed configuration-database connection string (2026-11-05)

The owner decided, after the risk was raised, that the configuration-database connection string (host, login and password) lives in `src/WabiInsight.Api/appsettings.json` because the repository is private and single-owner. Applied as instructed. Consequences recorded for the record: anyone with repository read access can reach the configuration database; the value also stays in git history after any later change; `appsettings.Local.json` (git-ignored) can still override it per machine. Every other secret (SMTP password, AI provider keys, OAuth tokens, bootstrap secret) stays out of source control: the committed SMTP password is the placeholder `REPLACE_WITH_REAL_PASSWORD` and must be supplied locally or through the deployment configuration.

## H. Temporary passwords are delivered by email only (2026-11-05)

When an administrator creates or resets an account, the temporary password is generated server-side, hashed, emailed to the account owner, and never shown to the administrator or returned by the API (the API returns the masked recipient address). The first sign-in forces a password change. Email delivery uses the outbox + background worker design from §B8; bodies are never persisted, only recipient, subject, template, status and attempts.
