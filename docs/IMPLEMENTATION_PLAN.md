# Wabi Insight — Implementation Plan (Phases and Pull Requests)

**Status:** Phases 1–14 merged; Phase 14 (Proactive Agent) was approved by the owner on 2026-11-07, design in [`PROACTIVE_AGENT.md`](./PROACTIVE_AGENT.md). P13.5 (second sample organization, Wabi Bank), P13.6 (its follow-ups), P13.7 (answer accuracy fixes found with it), P13.8 (query plans: totals, column comparisons and the asker's own codes), P13.9 (example questions per application, readable alert numbers), P13.10 (complete explanations, Arabic dates and numbers) and P13.11 (plain-language explanation of each query, compact language switch, generic Wabi Bank names) merged; P13.12 (product walkthrough video, English) merged; P13.13 (walkthrough video, Arabic) merged; P13.14 (file storage: server disk or Azure Blob Storage) merged; P13.15 (business content only for business users) merged; P13.16 (report layout: whole figures, readable chart labels, natural figure references), P13.17 (Arabic text in charts), P13.18 (Deploy (Azure)) and P13.19 (Windows VPS with Azure SQL + Blob Storage, beginner guide and CD) and P13.20 (beginner's guides for Windows Server + SQL Server Express, Ubuntu and Azure App Service) and P13.21 (prompt caching for the planner's catalogue) and P13.22 (demo organizations with their databases on the VPS in the Azure SQL setup) merged; P13.23 (deployment guide fixes from the review) merged; P13.24 (code fixes from the review) merged. The remaining review fixes are version 1 work (owner decision, 2026-11-10), in six pull requests P13.25–P13.30: P13.25 (sign-in and security hardening) merged, P13.26 (support access and Platform Administrator visibility) merged, P13.27 (scheduler and Periodic Reports) merged, P13.28–P13.30 planned. Version 2 is scoped in [`VERSION_2.md`](./VERSION_2.md) (P0.3 to P0.6 merged; P0.7 in review); the design of Sign in with Microsoft is in *Version 2* below (P0.2 merged).
**Specification:** [`docs/WABI_INSIGHT_SPECIFICATION.md`](./WABI_INSIGHT_SPECIFICATION.md) (v1.2; §35 added for Phase 14).
**Review of the original prompt:** [`docs/SPEC_REVIEW.md`](./SPEC_REVIEW.md).

## Working agreement

- `main` is the integration branch. Nothing is pushed to `main` directly.
- Every PR lives on its own branch and must be green in CI (build + tests for API and Angular) before the owner merges it.
- PRs are ordered; each PR assumes every earlier PR is merged. A PR is kept small enough to review in one sitting (target: < 3,000 changed lines excluding generated files and lock files).
- Every PR updates `docs/PROGRESS.md` and, when behaviour changes, the relevant doc under `docs/`.
- Branch naming: `wi/p<phase>-<nn>-<short-name>` (for example `wi/p01-02-config-db`). Sessions started from the Claude app receive a generated branch name; the PR title carries the `P1.2` identifier either way.
- PR title format: `P<phase>.<n> — <short description>`.

## Verified toolchain (checked 2026-11-05)

| Component | Version | Note |
|---|---|---|
| .NET | 10.0 (LTS, supported to Nov 2028) | .NET 11 (STS) ships Nov 2026; not used. |
| ASP.NET Core / EF Core | 10.x | |
| Angular | 22.2.x (latest stable 22.2.4, 2026-10-29) | Angular 23 expected mid-Nov 2026; upgrade in Phase 12 if stable by then. |
| Node.js | 22 LTS | |
| SQL Server | 2022 (dev container `mcr.microsoft.com/mssql/server:2022-latest`) | Integration tests run against a real SQL Server. |
| Oracle | 23ai Free (`gvenzl/oracle-free`) | Optional live tests; compiler tests are offline. |

## Phase and PR map

### Phase 0 — Planning (this PR)
| PR | Branch | Scope |
|---|---|---|
| P0.1 | `claude/query-harbor-plan-qjo2sh` | Revised specification, spec review, implementation plan, `CLAUDE.md`, `PROGRESS.md`. No application code. |
| P0.2 | `wi/p00-02-v2-microsoft-sign-in` | Version 2 plan: Sign in with Microsoft (Microsoft Entra ID), in *Version 2* below. No application code. |
| P0.3 | `wi/p00-03-version-2-scope` | Version 2 scope and requirements: [`VERSION_2.md`](./VERSION_2.md) (owner decisions, items carried over from the review, proposals, order). No application code. |
| P0.4 | `wi/p00-04-v2-deployment-plans` | Version 2 scope, additions: three service plans per organization (Cloud, On-premises, Hybrid), an organization's own AI model at a private address, sign-in with classic Active Directory. In [`VERSION_2.md`](./VERSION_2.md). No application code. |
| P0.5 | `wi/p00-05-v2-organization-smtp-connectivity` | Version 2 scope, additions: an organization's own e-mail server in every plan; the rule "the organization's own, or the platform's default"; the ways to reach an organization's systems from the owner's cloud, with a recommendation. In [`VERSION_2.md`](./VERSION_2.md). No application code. |
| P0.6 | `wi/p00-06-v2-outputs-branding` | Version 2 scope, additions: decisions on an organization's mail server (down, how to get one); the organization's logo and name on every output, the output's language from the question, a template gallery, the "Powered by" line. In [`VERSION_2.md`](./VERSION_2.md). No application code. |
| P0.7 | `wi/p00-07-v2-channels-plans` | Version 2 scope, additions: decision 10; Wabi Data Gateway; Wabi Hub; ask by WhatsApp (e-mail moved to version 3); plan positioning and a business model by calls, in AED. In [`VERSION_2.md`](./VERSION_2.md). No application code. |
| P0.8 | `wi/p00-08-v2-pricing-analysts` | Version 2 business model: paid analysts with a pooled allowance of calls, free viewers; tiers and prices in AED. In [`VERSION_2.md`](./VERSION_2.md) §10. No application code. |

### Phase 1 — Foundation and Multi-Tenant Identity
**Acceptance:** A Platform Administrator can be bootstrapped and sign in; the schema makes an organization user without a valid organization impossible; CI is green on both stacks.

| PR | Scope |
|---|---|
| P1.1 — Repository scaffolding and CI | Solution layout (`src/WabiInsight.Api`, `src/WabiInsight.Core`, `src/WabiInsight.Infrastructure`, `src/web` Angular 22), health endpoint, GitHub Actions workflow (dotnet build/test + ng build/test with a SQL Server service container), `docker-compose.dev.yml` for local SQL Server, `CLAUDE.md`, `ARCHITECTURE.md` skeleton. |
| P1.2 — Configuration database and tenant core | EF Core 10 model and first migration: `Organizations`, `Administrators`, `OrganizationUsers`, `LoginIdentities`, `SystemInitialization`, `PlatformSettings`, `AuditLogs`. UUIDv7 keys, scoped unique constraints, `ITenantContext`, tenant query filters, integration-test harness (Testcontainers locally, service container in CI) with a two-organization fixture. |
| P1.3 — Authentication and bootstrap | Bootstrap configuration + `bootstrap` CLI command + protected `/api/bootstrap`; cookie authentication with antiforgery; login/logout, password change, forced first-login change, lockout, inactive/suspended denial, security-stamp session invalidation; authorization policies (`PlatformAdministrator`, `OrganizationUser`, `OrganizationAdmin`); rate limiting on auth endpoints; production rejection of the sample password. |
| P1.4 — Angular design system, i18n, and auth UI | Brand theme (tokens, Angular Material M3 theme), Inter / IBM Plex Sans Arabic, Transloco EN/AR with RTL switching and persistence, three layout shells (public, organization, platform), login, bootstrap page, forced password change, profile basics, route guards and HTTP interceptors, first real screenshots. |

### Phase 2 — Organization Onboarding
**Acceptance:** Two test organizations are onboarded end to end with complete identity isolation, proven by tests.

| PR | Scope |
|---|---|
| P2.1 — Organization management and onboarding | Platform API + UI: create/edit/suspend/activate organizations, initial Organization Administrator invitation, one-time invitation token acceptance (set password), administrator-assisted handover, `EmailService` with SMTP provider + development file sink + `EmailMessages` outbox. |
| P2.2 — Organization identity and roles | Departments, sections, organization users (create/edit/deactivate/reset password), roles and user-role assignment, protected `OrganizationAdmin` and `Management` roles, default role templates (platform-managed, versioned) provisioned on organization creation, organization-admin UI pages. |

### Phase 3 — Platform Administration Dashboard
**Acceptance:** Platform Administrators see aggregated KPIs, charts, health and operational tables with date filters, and no organization application names, business data or connection details.

| PR | Scope |
|---|---|
| P3.1 — Usage events, metrics and health backend | `UsageEvents` pipeline (single append-only event table), `PlatformMetricsService` with date-range aggregation, health checks (API, config DB, AI provider stub, storage, email), `/api/platform/dashboard`, `/api/platform/health`, `/api/platform/metrics`, `/api/platform/audit`. |
| P3.2 — Platform dashboard and administration UI | Dashboard with KPI cards, ECharts charts in brand palette, filters, drilldown tables; System Health, Audit Logs, Platform Administrators management, General Settings (bootstrap status, platform settings). Later phases add their own widgets to this dashboard. |

### Phase 4 — Support Desk
**Acceptance:** Organization users create tickets; Platform Administrators manage them; every meaningful action appears in a stored timeline.

| PR | Scope |
|---|---|
| P4.1 — Support desk backend | `SupportTickets`, `SupportTicketEvents`, `SupportTicketComments`, `SupportTicketAttachments`, `TicketSequences` (concurrency-safe yearly reference `WI-YYYY-NNNNNN`), status state machine, first-open tracking, assignment, comments, attachment storage via `IArtifactStorage`, `Notifications` table + service, visibility rules per role. |
| P4.2 — Support desk UI | New ticket, My Tickets, organization ticket list (OrgAdmin), platform queue with filters/search/pagination, ticket details with Overview / Conversation / Activity Timeline / Attachments tabs, notification centre, dashboard ticket widgets. |

### Phase 5 — Authorized Support Access
**Acceptance:** A Platform Administrator cannot change any organization configuration without an approved, unexpired, narrowly scoped grant, and every assisted action is audited on the ticket.

| PR | Scope |
|---|---|
| P5.1 — Support access grants backend | `SupportAccessGrants` with typed operation catalog, request → OrgAdmin approval → support-mode session → scoped execution → expiry/revocation; `SupportAccessAuthorizationHandler`; audited execution of the permitted operations (user create/reset, role config, table permission update, metadata refresh, connection diagnostics). |
| P5.2 — Support access UI | OrgAdmin approval screen, platform Support Access page, support-mode banner and entry/exit, ticket tab 5 (Support Access), timeline and audit integration. |

### Phase 6 — Organization Data Configuration
**Acceptance:** Organization Administrators configure their own applications, metadata and permissions; tenant boundaries remain enforced by tests.

| PR | Scope |
|---|---|
| P6.1 — Applications, encrypted connections, metadata | `Applications` with Data Protection–encrypted connection strings (key ring outside the DB), SQL Server and Oracle connectivity adapters (test connection, read-only account check), metadata discovery and approval (`DatabaseObjects`, `DatabaseColumns`, `DatabaseRelationships`), refresh with diff, EN/AR display names, organization-admin UI. |
| P6.2 — Permissions model | `RoleTablePermissions`, `RoleColumnPermissions`, `RoleRowPermissions`, `RowScopeColumnMappings`, effective-permission resolver (union across roles, organization boundary first), `Management` automatic read access, permissions UI (table / column / row). |

### Phase 7 — Built-In Platform Insights and Organization Dashboard
**Acceptance:** Each Organization Administrator can retrieve only their own organization's platform metrics; the organization dashboard shows only that organization.

| PR | Scope |
|---|---|
| P7.1 — Platform Insights data source | `insights` schema of tenant-scoped SQL views in the configuration DB, dedicated read-only DB login, global `WI-INSIGHTS` catalog (objects, columns, metric definitions), per-organization built-in application row provisioned on onboarding, mandatory `OrganizationId` predicate, platform-side Platform Insights Configuration page. |
| P7.2 — Organization dashboard and Organization Insights | `/app/admin/dashboard` with organization-scoped KPIs, charts and filters; Organization Insights page; default OrgAdmin access and per-role metric grants. Natural-language questions against Platform Insights become available in Phase 8. |

### Phase 8 — Secure AI Engine
**Acceptance:** Authorized users receive accurate results from SQL Server, Oracle and Platform Insights; unsafe or unauthorized plans are rejected with tests proving it.

| PR | Scope |
|---|---|
| P8.1 — AI provider abstraction and query plan | `IAiProvider` with OpenAI-compatible (covers OpenAI, Mistral, local endpoints) and Gemini adapters, encrypted provider credentials, platform allow-list and organization selection, typed query-plan JSON schema, plan validator, authorization pass (table/column/row/organization), prompt-injection safeguards. |
| P8.2 — Compilers, execution and interpretation | SQL Server and Oracle dialect compilers, parameterized read-only execution with limits/timeouts, deterministic calculations, interpretation step, `/api/ai` end-to-end, `AnalysisExecutionMetrics`, compiler snapshot tests, live Oracle test gated by environment. |

### Phase 9 — User Workspace and History
**Acceptance:** Users ask questions, see tables and charts, reopen saved results without re-querying, re-run into new versions, and export bilingual reports.

| PR | Scope |
|---|---|
| P9.1 — AI workspace and persistent history | Workspace UI (application selector, prompt, output language, result tables/charts/narrative), `AnalysisHistories`, `AnalysisHistoryVersions`, `AnalysisArtifacts`, history list/search/filters/pagination, favorites, collections, archive, delete, re-run. |
| P9.2 — Reports and exports | PDF / DOCX / XLSX generation with real RTL and Arabic shaping, server-rendered charts, reports library, print view, `ReportGenerationEvents`, verified Arabic output files committed as test fixtures. |

### Phase 10 — Collaboration and Integrations
**Acceptance:** Sharing respects organization boundaries, classification rules and provider outcomes.

| PR | Scope |
|---|---|
| P10.1 — In-app and email sharing | `AnalysisShares`, Shared With Me / Shared By Me, expiry and revocation, notifications, email sharing with external-disclosure approval, `EmailDraftEvents` (draft vs sent). |
| P10.2 — Google Drive and OneDrive publishing — **deferred to the next version (owner decision, 2026-11-06)**; the organization *Integrations* page states this and lists the sharing channels that exist | `ExternalProviderConnections` (encrypted OAuth tokens), Drive upload + reader permissions, OneDrive upload via Microsoft Graph + read-only sharing, `ExternalShareRecords`, `IntegrationOperationLogs`, organization Integrations page, provider failure handling with mocked provider tests. |

### Phase 11 — Landing Page and Visual Polish
**Acceptance:** Public landing page in EN and AR, responsive, branded; consistent design across every screen.

| PR | Scope |
|---|---|
| P11.1 — Logo and landing page | SVG logo set (light/dark/icon/wordmark/favicon), ten-section landing page, data-chaos vs success scenes as illustrations, fictional labelled demo data. |
| P11.2 — Cross-page polish and screenshots | Visual consistency pass, responsive fixes, Playwright screenshot script producing the 42 required screens in EN/AR, `docs/SCREENSHOTS.md`. |

### Phase 12 — Security Hardening and Quality Assurance
**Acceptance:** Cross-tenant, support-access, read-only, localization and responsive tests pass; deployment is documented and validated.

| PR | Scope |
|---|---|
| P12.1 — Security hardening | Cross-tenant penetration-style test suite, IDOR sweeps, security headers, request-size and rate limits review, error-handling review, audit-trail verification, dependency audit. |
| P12.2 — Performance, deployment and final docs | Load checks on hot endpoints, index review, production Docker/IIS/Linux deployment guides, `SECURITY.md` and `ARCHITECTURE.md` final, Angular 23 upgrade if stable (not released as of 2026-11-06; stays on the backlog). |

### Phase 13 — Sample Data and Deployment Guides
**Acceptance:** A new deployment can be populated with realistic source databases and a ready-to-use organization with users of every role, from SQL scripts alone plus the connection step in the UI.

| PR | Scope |
|---|---|
| P13.1 — Sample source databases and WabiInsightDB seed | Flight operations, HR/payroll/attendance and procurement/stores SQL Server databases (one script each with database, owner and reader logins, tables and data) and a configuration seed with organization, structure, four users, applications, catalogue, classifications and permissions. |
| P13.5 — Wabi Bank sample organization | A fictional UAE bank as a second sample organization: Personnel (system of record for every identifier), Core Banking, Finance, Digital Banking and IT Help Desk databases with consistent 2025 data and planted findings, a cross-system consistency check, and a seed with eight role-scoped users, least-privilege permissions and five Monitoring Rules. |
| P13.6 — Wabi Bank follow-ups | From the live tests: code and value lists in the catalogue so the AI can name the bank's codes, classification-capped grants for directory roles, pending leave, realistic counter volumes, month-end loan balances, README questions that the data answers, and the recorded demo. |
| P13.7 — Answer accuracy fixes | Found by the live Wabi Bank tests: answers and reports say when row scopes limited their rows (and never conclude about what the asker cannot see), aggregates over yes/no columns, and no absence claims from partial results. |
| P13.8 — Query plans: totals, column comparisons, "my department" | Generic plan-language additions: conditions on aggregated values (`HAVING`), column-to-column comparisons, and the asker's own employee/department/section code as a server-filled placeholder the model never sees; schema kept inside the providers' grammar limit. |
| P13.9 — Example questions and readable numbers | Up to six example questions per application (EN/AR), written by Organization Administrators and shown on the Ask page; no built-in industry examples; sample seeds fill them. Alert, rule and digest numbers with separators and Arabic-Indic digits in Arabic. |
| P13.10 — Complete explanations, Arabic dates and numbers | The explanation call keeps its own low effort and a 4,096-token floor, and an unfinished explanation is flagged and explained on the page instead of left blank; every date, number and numeric translation parameter follows the interface language. |
| P13.11 — Plain-language "How this was computed", compact language switch, generic sample names | Answers stop carrying SQL: each question is logged server-side (`AnalysisQueryLogs`: question, plan, SQL with bound values, row counts, outcome) for accuracy reviews, and the page explains each validated plan in words in EN/AR; the language switch becomes a globe icon aligned with the header icons on phones; every person in the Wabi Bank sample gets a generic, clearly fictional EN/AR name, with visibly fictional e-mails, phone numbers and ID numbers. |
| P13.12 — Product walkthrough video (English) | An eight-minute narrated video of the real application on the Wabi Bank sample for organizations evaluating the product (no Platform Administrator), with subtitles, chapters and a timecoded script in `docs/demo/`, and the generator in `scripts/demo/walkthrough` (script, offline narration, title graphics, Playwright recorder, composer) so the Arabic version and later updates come from the same files. |
| P13.13 — Product walkthrough video (Arabic) | The same walkthrough in Arabic: Arabic interface, questions, answers, files and title cards, Arabic narration (Azure AI Speech neural voice, commercially licensed) with subtitles, chapters and a timecoded script in `docs/demo/`, generated by the same files with `--lang ar`. |
| P13.14 — File storage: server disk or Azure Blob Storage | `Storage:Provider` chooses the server disk (default) or a private Azure Blob Storage container for every generated and uploaded file, switched by commenting one line in `appsettings.json`; connection string kept out of the repository; same keys on both, disk fallback while switching, `storage-check` and `storage-copy` server commands; tests against Azurite. |
| P13.15 — Business content only | Files, answers and the pages business users work in never show how an answer was produced (query or row counts, read-only, SQL, AI tokens or cost): methodology section and row-count lines removed from PDF/Word, a business-reader rule in every AI writing prompt, counts and cost removed from Ask, history, shared and periodic views, plain-language failure messages in EN/AR. |
| P13.16 — Report layout | PDF and Word tables sized from their content so figures and header words never break (smaller type only for tables too wide for the page), KPI figures on one line, chart category labels on one line, two lines or at an angle so they never overlap, series names in words, month names on monthly axes, and study text that refers to charts and tables naturally instead of "Figure:" labels. |
| P13.17 — Arabic chart text | Chart labels (axes, categories, legends) set by the document text engine over the SVG shapes, so Arabic is shaped and laid out right to left in PDF, Word and Excel charts; Arabic month names on monthly axes. |
| P13.18 — Deploy (Azure) | Manual `Deploy (Azure)` workflow with checkboxes (Azure resources, database, API, web app, first administrator, tests, ref) over OpenID Connect; Bicep template for App Service + Azure SQL + Blob Storage; optional `Web:RootPath` so the API serves the web app; `ReverseProxy:TrustAllProxies`; step-by-step Azure guide. |
| P13.19 — Windows VPS + Azure SQL + Blob | Beginner's step-by-step guide `DEPLOYMENT_WINDOWS_AZURE.md` (Azure portal, VPS, IIS, HTTPS, GitHub runner, first deployment, backups, roll-back, troubleshooting); `Deploy (Windows)` + deploy script v3: Azure SQL restore point before migrations and a storage check when files are in Azure Blob Storage. |
| P13.20 — Beginner's deployment guides | `DEPLOYMENT_WINDOWS_SERVER_2019.md`, `DEPLOYMENT_LINUX.md` and `DEPLOYMENT_AZURE.md` rewritten as complete beginner's guides (deployment + CI/CD) in the style of `DEPLOYMENT_WINDOWS_AZURE.md`: glossary, information sheet, Check / Do / Verify per step, one-click deployments, day-to-day operations, roll-back, troubleshooting. Docs only. |
| P13.21 — Prompt caching | The planner's catalogue (METADATA block) becomes a second Anthropic cache breakpoint before the uncached question (`AiRequest.SharedContext`, sorted relationships, language after the catalogue); cache writes recorded and priced at 1.25× input. Live: about 88% lower cost per question after the first within five minutes. |
| P13.22 — Demo databases on the VPS | `DEPLOYMENT_WINDOWS_AZURE.md` Part H: SQL Server Express on the VPS for the demo business databases; the seeds run on a copy of the application database next to them, and the result returns to Azure SQL as a new database; applications connect with `localhost`. Docs only. |
| P13.23 — Guide fixes from the review | Prompting and fail-fast PowerShell blocks run as one unit (`. { … }`); roll-back accepts the short commit code; stronger port check, command download of SqlPackage and readable seed output in Part H; README, SECURITY and ARCHITECTURE corrections; CI token limited to read. |
| P13.24 — Code fixes from the review | Whole-number averages and totals on SQL Server; whole-day date filters; relative dates resolved at every run (re-runs and Periodic Reports follow the calendar); alert details respect row scopes; support grants fix the roles a created user may get; e-mail worker survives failures, drains on shutdown and marks abandoned messages failed; PDF and Word say when a table is shortened and draw every KPI. |
| P13.25 — Review fixes: sign-in and security hardening | Version 1 fixes from the review (owner decision, 2026-11-10: fixes are not version 2). **C2** request limits per signed-in user, not per network address; **C3** the idle sign-out really happens (timer-driven requests no longer renew the session); **C4** the set-up status page keeps the first administrator's e-mail from anonymous callers, and a lockout no longer ends an open session; **C8** organization logos are PNG or JPEG only and the logo answer keeps its sandbox policy; **C9** data sources never sign in with the server's own identity, and organization-managed AI providers only reach public addresses. |
| P13.26 — Review fixes: support access and Platform Administrator visibility | **C5** before a grant, a ticket's targets are the requester and the standard roles only (exact look-ups by e-mail address or role code; what a grant names, and table names for a table-permissions grant, only under the caller's approved grant), and the platform audit page withholds an organization's configuration details unless the reader holds an approved grant for it; **C6** a support grant to create a user names the new user's e-mail address, approved by the organization and enforced at execution; **C7** an alert records whose permissions it was evaluated under (`Alerts.EvaluatedAsUserId`, migration `AddAlertEvaluatedAs`), so a rule's new owner does not see older alert details. |
| P13.27 — Review fixes: scheduler and Periodic Reports | **E1** a failed scheduled run is retried after 5, 15 and 60 minutes, then waits for its regular schedule; the daily digest counts from the last digest that went out (`ScheduledJobs.LastSuccessAt`), so no alert is dropped; rule and report jobs follow a changed organization time zone; one job per organization, type and target (unique index) and a lease that is extended while the job runs (migration `SchedulerReliability`). **E2** an Organization Administrator can edit and resume a report someone else set up; a report stops when the share it was built on ends. **B6** an analysis with fixed dates says so where a Periodic Report is set up and on the report. |
| P13.28 — Review fixes: web app, English and Arabic (planned) | **F1** missing messages (expired session, failed re-run, alerts page, too many requests); **F2** Arabic page controls, mirrored arrows, labels for every error code; **F3** keyboard and screen-reader access; **F4** one ticket per submission, permissions editor race; **F5** technical fields no longer sent to business users; **C10** language choice kept; **B2** Arabic column headers and legends; **B7** chart series add up shared categories. |
| P13.29 — Review fixes: query engine and error handling (planned) | **E3** clear 400 and 409 answers instead of server errors, every failed query recorded; **A3** names with spaces or non-Latin letters; **E7** a cut-off study is reported, OpenAI-compatible and Gemini adapters tested against recorded answers. |
| P13.30 — Review fixes: Oracle end to end (planned) | **A2** discovery with a read-only login, read-only check, large decimals, integration tests against a real Oracle container. |
| P13.4 — Windows server layout | `C:\wabitex-apps\wabi-insight` (`src`, `web\app`, `web\api`) and `D:\wabi-insight\files` per kind, in the Windows deployment and DevOps guides and the deploy script; moving an existing installation. |
| P13.3 — CI/CD with manual deployments | Reusable release build, `Deploy (Linux)` / `Deploy (Windows)` workflows started by hand (frontend / backend / both, migration switch), self-hosted runner + server-side deploy scripts with backup, health check and automatic restore, DevOps guides for both platforms. |
| P13.2 — Step-by-step deployment guides | Windows Server 2019 (IIS + ARR) and Ubuntu 24.04 (nginx + systemd) guides from a fresh server to the live site, with SQL Server Express, a check and a verification for every step; reverse-proxy fixes found along the way. |

### Phase 14 — Proactive Agent: Monitoring Rules, Alerts, Periodic Reports
**Acceptance:** An organization defines rules inside the tool over its own catalogued data and over platform usage, receives in-app alerts (immediate for High) and one daily digest email, acts on alerts by hand (acknowledge, resolve, suspend user), and receives saved analyses on a schedule rendered under each recipient's own permissions. Nothing in the engine is specific to a business domain; templates appear only when the organization's metadata has the fields they need. Design and owner decisions: [`PROACTIVE_AGENT.md`](./PROACTIVE_AGENT.md); specification §35.

| PR | Scope |
|---|---|
| P14.0 — Design and documentation | `PROACTIVE_AGENT.md`, specification §35, this phase, README/CLAUDE/ARCHITECTURE updates. No application code. |
| P14.1 — Access ledger and scheduler | `AnalysisAccessFacts` written with every analysis (including rejected plans), nightly `AccessBaselines`, `ScheduledJobs` with atomic leases, 24-month retention job, `DatabaseColumns.SemanticRole` with suggestions, migrations, usage events. |
| P14.2 — Monitoring Rules and Alerts | Typed rule document, validation/authorization, data-rule compiler on the dialect compilers, access-rule evaluator (limits and baselines), template catalogue filtered by metadata, `Alerts`/`AlertEvents`, cooldown, immediate in-app for High, daily digest email, human actions incl. suspend, API, Angular pages (rules, templates, editor, test run, alert centre), EN/AR, platform counts. |
| P14.3 — Periodic Reports | Subscriptions, per-recipient execution under the recipient's permissions, PDF/XLSX, outbox delivery with attachment and link, delivery log, pause after three failures, administrator pages, Management self-service, "sent to me" in My Reports. |
| P14.4 — Natural-language rules and system rules | `wabi_monitoring_rule` schema and prompt, draft → confirm → save, system rules (connections, AI budget, email, metadata drift), `RuleInvalid` rule-health alerts, screenshots and docs. |
| P14.5 — Wrap-up | Recorded proactive-agent demo on the sample organization (`scripts/demo/proactive-agent`), scheduler lease release fix, Phase 14 screenshot index, status docs. |

## Version 2 (planned, not started)

Work the owner has moved to version 2. Nothing here is built in version 1; each item starts only after the owner approves it.

**The full scope of version 2 is in [`VERSION_2.md`](./VERSION_2.md):** every item with its source, priority and size, the requirements, the open decisions and the proposed order of pull requests. This section keeps the detailed design of the items that already have one.

| Owner decision | Date | Where |
|---|---|---|
| Google Drive and OneDrive publishing | 2026-11-06 | Below; scope in Phase 10 (P10.2); `VERSION_2.md` D1 |
| Sign in with Microsoft | 2026-11-08 | Below (full design); `VERSION_2.md` C1 |
| Drill-down in charts | 2026-11-09 | `VERSION_2.md` B1 |
| API data sources (not only SQL Server and Oracle databases) | 2026-11-10 | `VERSION_2.md` A1 |
| Three service plans per organization: Cloud, On-premises, Hybrid | 2026-11-10 | `VERSION_2.md` G1, G3, G4 |
| An organization's own AI model (on-premises, private cloud or its own public-cloud account) | 2026-11-10 | `VERSION_2.md` G2 |
| Sign in with classic Active Directory, without a password on domain computers | 2026-11-10 | `VERSION_2.md` C11 |
| An organization's own e-mail server (SMTP) in every plan; the platform's when none is set | 2026-11-10 | `VERSION_2.md` G5 |
| Reaching an organization's systems from the owner's cloud: options and recommendation, chosen when the step is reached | 2026-11-10 | `VERSION_2.md` G6 |
| The organization's logo and name on every output; the output's language from the question; a template gallery per kind of output; "Powered by Wabi Insight by WabiTex Solutions FZE" on every output | 2026-11-10 | `VERSION_2.md` H1–H4 |
| Reaching an organization's systems: nothing for On-premises; for Cloud the G6 recommendation plus a Wabi Data Gateway | 2026-11-10 | `VERSION_2.md` G6, G7 |
| Ask by WhatsApp (asking by e-mail moved to version 3) | 2026-11-10 | `VERSION_2.md` D4, §6 |
| Pricing by analysts with a pooled allowance of calls, free viewers, prices in AED | 2026-11-10 | `VERSION_2.md` §10 |

Fixes are not version 2 (owner decision, 2026-11-10): what the review of 2026-11-10 found is fixed in version 1, in P13.25–P13.30 (Phase 13).

Version 1 and AI providers (owner decision, 2026-11-10): organizations use the platform's subscription. The feature that lets an organization add its own provider stays, restricted as in P13.25, and is switched off by a Platform Administrator emptying the allowed provider types. No code change.

### Sign in with Microsoft (Microsoft Entra ID)
**Owner decision (2026-11-08):** planned for version 2. Version 1 keeps local e-mail and password sign-in only (specification §8).

**Goal.** People sign in with **Sign in with Microsoft** instead of typing a password into Wabi Insight. Microsoft checks the password and the second step (Microsoft Authenticator or a one-time code); Wabi Insight never sees either. Users can have any e-mail address: Outlook/Hotmail (personal Microsoft accounts), company Microsoft 365 accounts, Gmail and other providers.

**What the owner sets up in Microsoft (no server, no Microsoft 365 or company domain needed).**
- The Microsoft Entra directory that came with the owner's Azure subscription (an `@outlook.com` Azure account is enough).
- One **app registration** for Wabi Insight: application (client) ID, a client certificate or secret (kept in secrets or environment variables, never in source control), redirect URI `https://<host>/signin-oidc` per environment, sign-out URI.
- Users are **invited as guests** by e-mail (Microsoft Entra B2B): Microsoft-account users sign in with Microsoft, company users with their company account, Gmail users with Google (once Google is enabled as an identity provider) or a one-time e-mail code, anyone else with a one-time e-mail code.
- **Two-step sign-in** is required in Entra: security defaults (free) or a Conditional Access policy for the Wabi Insight app (Microsoft Entra ID P1). The exact behaviour for guest accounts is confirmed against Microsoft's documentation when the work starts.
- Later options, not in the first version 2 release: a multi-tenant registration so a customer organization on Microsoft 365 links its own directory, and Microsoft Entra External ID for self-service sign-up at scale.

**Rules (the version 1 invariants stay).**
- Entra proves *who* the person is; Wabi Insight decides *what they may see*. A Microsoft sign-in is accepted only for an **existing, active** `Administrators` or `OrganizationUsers` row created or invited by an administrator. No account is created automatically from a Microsoft sign-in.
- Accounts are linked by Microsoft's immutable identifiers (directory `tid` + user `oid`), never by e-mail alone. The first sign-in links through the invitation (a one-time link token, or a verified match on the invited address); after that only `tid` + `oid` count, so a changed or reused e-mail address cannot move access to someone else.
- `OrganizationId` still comes from the linked user record through `ITenantContext`, never from token claims; row scopes, roles and support-access rules are unchanged.
- Same session handling as today: cookie session, CSRF protection, security stamp, inactive-user and suspended-organization denial. `MustChangePassword` does not apply to Microsoft-only users.
- A per-organization setting chooses the allowed sign-in methods (password, Microsoft, or both); a platform setting does the same for Platform Administrators (for example Microsoft with two-step sign-in only).
- Audit events: `Auth.ExternalLinked`, `Auth.ExternalUnlinked`, `Auth.ExternalSignIn`, `Auth.ExternalSignInRejected` (stable reason codes: not invited, inactive, wrong directory, link mismatch). Secrets are never returned to the browser.

**Data.** New `ExternalLogins` table: principal type and id, provider (`MicrosoftEntra`), directory id (`tid`), object id (`oid`), linked at, last sign-in at; unique on provider + `tid` + `oid`, and on principal + provider. Sign-in methods are stored on the organization and in platform settings.

**Interface (EN/AR).** *Sign in with Microsoft* button on the login page; invitation acceptance offers *Use my Microsoft account*; the profile shows the linked account (unlink only while a password exists); Organization Administration → Settings: allowed sign-in methods; Platform Administration: provider status (configured or not, never the secret).

**Tests.** Integration tests with a test OpenID Connect issuer and signing key, always with two organizations: an uninvited user is refused; the first sign-in links and later sign-ins use `tid` + `oid`; a token from another directory is refused; an inactive user or suspended organization is refused; an e-mail match alone does not sign in a linked account; tenant isolation holds; audit entries are written.

**Documentation.** App registration and guest invitation steps in the deployment guides (Windows Server 2019, Linux), redirect URIs per environment, `SECURITY.md`, specification §8.

| PR | Scope |
|---|---|
| V2.1 — Microsoft sign-in: backend | OpenID Connect sign-in with Microsoft Entra ID (Microsoft.Identity.Web), `ExternalLogins` and migration, link and sign-in services, sign-in method settings, audit, integration tests. |
| V2.2 — Microsoft sign-in: interface and guides | Login button, invitation and profile linking, organization and platform settings (EN/AR), deployment-guide steps for the app registration and guest invitations, screenshots. |

### Google Drive and OneDrive publishing
Deferred from P10.2 (owner decision, 2026-11-06); see Phase 10 for the scope.

## Milestone verification (every PR)

1. `dotnet build` and `dotnet test` pass (unit + integration against SQL Server).
2. `ng build` and `ng test` pass; `ng lint` clean.
3. Manual check of user-facing behaviour, with real screenshots when UI changed.
4. `docs/PROGRESS.md` updated with what shipped, what is blocked, and environment requirements.
