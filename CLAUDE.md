# Wabi Insight — Claude Code Guide

Wabi Insight (platform code AP-AIR) is a bilingual (EN/AR), multi-organization enterprise AI intelligence platform. Read `docs/WABI_INSIGHT_SPECIFICATION.md` for requirements and `docs/IMPLEMENTATION_PLAN.md` for the phase/PR order. Keep `docs/PROGRESS.md` current in every PR.

## Architectural invariants (never weaken)
- Exactly three logical layers: Angular app (`src/web`), one ASP.NET Core Web API (`src/WabiInsight.Api` + `Core` + `Infrastructure` libraries), data/integrations (one SQL Server config DB, tenant SQL Server/Oracle sources, LLM, email, Drive/OneDrive, artifact storage). No microservices, brokers, or separate Python apps.
- Platform Administrators live in `Administrators` and have **no** `OrganizationId`. Organization users live in `OrganizationUsers` with a **required** `OrganizationId`. `LoginIdentities` makes login emails globally unique.
- `OrganizationId` always comes from `ITenantContext` (authenticated principal), never from the request body, route, query string, or AI output.
- Every tenant-owned query is scoped by EF tenant filters **and** explicit ownership checks in services. Platform-wide reads use dedicated admin query paths with field restrictions.
- AI output is a typed query plan that is validated and authorized before a dialect compiler emits parameterized, read-only SQL. Never execute model-generated SQL. Never append WHERE clauses to arbitrary SQL.
- Row scopes (`ALL`, `DEPARTMENT`, `SECTION`, `SELF`) apply after organization and application ownership; `ALL` never crosses organizations.
- Platform Administrators access organization configuration only through an approved, unexpired, scoped `SupportAccessGrant` in support mode, and never business data.
- Secrets (connection strings, provider keys, OAuth tokens) are encrypted with ASP.NET Core Data Protection and never returned to the browser. The key ring lives under `DataProtection:KeyRingPath` (default `.artifacts/keys`, git-ignored); point it at a persistent directory in deployment or encrypted values become unreadable.
- `AuditLogs`, `UsageEvents`, `SupportTicketEvents` are append-only.
- Status/type values are stable codes; labels are localized (EN + AR) in resources.
- Phase 14 (proactive agent, `docs/PROACTIVE_AGENT.md`): monitoring rules evaluate under their **owner's** permissions and never widen them; periodic reports render **per recipient under the recipient's permissions**; alerts never suspend users automatically (human action only); the engine holds no business-domain logic — rules bind to the organization's catalogued objects/columns and templates are offered only when the required field roles exist; `AnalysisAccessFacts`, `RuleEvaluations`, `AlertEvents` are append-only.

## Conventions
- .NET 10 LTS, C# nullable + implicit usings, async everywhere, DI via interfaces, UUID v7 keys, UTC `datetime2`.
- Angular 22 standalone components, signals, Angular Material M3 brand theme, Transloco for EN/AR, ECharts for charts.
- Tests: xUnit (unit + SQL Server integration via Testcontainers / CI service container), Vitest for Angular, Playwright for screenshots. Multi-tenant tests always use at least two organizations.
- Branches: one PR per branch, titled `P<phase>.<n> — <description>`; `main` is merged by the owner only.

## Secrets and local configuration
- Owner decision G: the configuration-database connection string is committed in `src/WabiInsight.Api/appsettings.json` (private repository). Keep it valid JSON; CI loads the file. `//` comment lines are allowed (the .NET settings loader accepts them): the `Storage` section keeps the Azure Blob provider line commented as an on/off switch.
- Every other secret (SMTP password, AI keys, OAuth tokens, bootstrap secret) stays out of source control: use the git-ignored `appsettings.Local.json` (see `appsettings.Local.example.json`), `dotnet user-secrets`, or environment variables.
- Temporary passwords are emailed to the account owner and never returned by the API or shown in the UI.
- Local development against the docker-compose SQL Server and the File email sink: copy the example file to `appsettings.Local.json`.

## Essential commands
```bash
# .NET SDK (cloud/agent containers without a system install)
./scripts/install-dotnet.sh && export PATH=$HOME/.dotnet:$PATH

# Local SQL Server 2022
docker compose -f docker-compose.dev.yml up -d

# API
dotnet build WabiInsight.slnx -c Release
dotnet test WabiInsight.slnx -c Release
dotnet run --project src/WabiInsight.Api          # http://localhost:5080
dotnet run --project src/WabiInsight.Api -- migrate   # apply pending migrations (deployment step)
dotnet run --project src/WabiInsight.Api -- bootstrap # create the first Platform Administrator from Bootstrap:* settings (once)
dotnet run --project src/WabiInsight.Api -- seed-demo # Development only: demo organization + demo.user@demo.test
dotnet run --project src/WabiInsight.Api -- storage-check   # write/read/delete a test file in the configured storage (disk or Azure Blob)
dotnet run --project src/WabiInsight.Api -- storage-copy    # after switching to AzureBlob: copy the disk files to the container
dotnet tool restore && dotnet ef migrations add <Name> --project src/WabiInsight.Infrastructure --startup-project src/WabiInsight.Infrastructure --output-dir Persistence/Migrations

# Integration tests need SQL Server: either Docker (Testcontainers) or
WI_TEST_SQLSERVER_CONNECTION="Server=localhost,1433;Database=master;User Id=sa;Password=WabiInsight_Dev_2026!;TrustServerCertificate=True;Encrypt=True" dotnet test WabiInsight.slnx
# Azure Blob storage tests need Azurite: Docker (Testcontainers) or a local `azurite-blob --skipApiVersionCheck` with
# WI_TEST_AZURITE_CONNECTION="UseDevelopmentStorage=true"

# Web (from src/web)
npm ci
npm run lint && npm test && npm run build -- --configuration production
npm start                                          # http://localhost:4200, proxies /api to 5080

# Screens, load check, container images (P11.2 / P12)
WI_HISTORY_ID=… WI_ORGANIZATION_ID=… WI_TICKET_ID=… node scripts/screenshots/capture.js   # 42 screens EN/AR desktop+mobile
WI_SQLCMD="sqlcmd -S localhost,1433 -U sa -P … -C -d WabiInsightDB" node scripts/demo/proactive-agent/record.js --gif docs/demo/p14-5-proactive-agent-alert.gif   # proactive-agent demo (sample org)
WI_SAMPLE_PASSWORD=… WI_SQLCMD="sqlcmd …" node scripts/demo/wabi-bank/record.js --gif docs/demo/p13-6-wabi-bank.gif   # Wabi Bank demo (row scopes + AML alert)
node scripts/perf/load-check.js --base http://localhost:5080 --users 20 --logins 5 --seconds 30
docker build -f src/WabiInsight.Api/Dockerfile -t wabi-insight-api . && docker build -f src/web/Dockerfile -t wabi-insight-web src/web
```
Sample source databases and the WabiInsightDB seed (organization GAS, four users): run `scripts/sql/samples/01`–`04-*.sql` in order with sqlcmd after `migrate` (regenerate 01–03 with `python3 scripts/sql/samples/build_sample_sql.py`; see its README).
Second sample organization Wabi Bank (WBK, five databases, eight users): run `scripts/sql/samples/wabi-bank/01`–`05-*.sql`, then `06-seed-wabiinsightdb.sql` (`07-check-consistency.sql` must report 0); regenerate with `python3 scripts/sql/samples/wabi-bank/build_bank_sql.py`.
Deployment shapes (Docker Compose, systemd + nginx, IIS) live under `deploy/` and are documented in `docs/DEPLOYMENT.md` (step-by-step: `docs/DEPLOYMENT_WINDOWS_SERVER_2019.md`, `docs/DEPLOYMENT_LINUX.md`; SQL Server Express 2019–2025 supported); security controls and the cross-tenant sweep are in `docs/SECURITY.md`.
CI/CD: `ci.yml` runs on every PR; deployments are manual only (`deploy-linux.yml` / `deploy-windows.yml` → reusable `build-release.yml` → self-hosted runner → `deploy/cicd/*` scripts). Keep `actionlint` clean (`.github/actionlint.yaml` lists the `wabi-insight` runner label). Guides: `docs/DEVOPS_LINUX.md`, `docs/DEVOPS_WINDOWS.md`. Windows server layout: `C:\wabitex-apps\wabi-insight\{src,web\app,web\api,keys,logs,releases,deploy}` and `D:\wabi-insight\{files\<kind>,backup}`.
Screenshots and the demo GIF are captured with Playwright from the running app (see `docs/DESIGN_SYSTEM.md`); the full 42-screen script arrives in P11.2. Development uses the sample bootstrap values in `appsettings.Development.json`; any other environment must supply `Bootstrap__AdministratorPassword` and `Bootstrap__InitializationSecret` through secrets.

## Verification before pushing
1. `dotnet build` + `dotnet test` green.
2. `ng build` + `ng test` + `ng lint` green.
3. UI changes checked in the browser; real screenshots only.
4. `docs/PROGRESS.md` updated.
