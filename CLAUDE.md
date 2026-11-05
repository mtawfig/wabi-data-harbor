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
- Secrets (connection strings, provider keys, OAuth tokens) are encrypted with ASP.NET Core Data Protection and never returned to the browser.
- `AuditLogs`, `UsageEvents`, `SupportTicketEvents` are append-only.
- Status/type values are stable codes; labels are localized (EN + AR) in resources.

## Conventions
- .NET 10 LTS, C# nullable + implicit usings, async everywhere, DI via interfaces, UUID v7 keys, UTC `datetime2`.
- Angular 22 standalone components, signals, Angular Material M3 brand theme, Transloco for EN/AR, ECharts for charts.
- Tests: xUnit (unit + SQL Server integration via Testcontainers / CI service container), Vitest for Angular, Playwright for screenshots. Multi-tenant tests always use at least two organizations.
- Branches: one PR per branch, titled `P<phase>.<n> — <description>`; `main` is merged by the owner only.

## Secrets and local configuration
Never put real connection strings, passwords or secrets in `appsettings.json` or `appsettings.Development.json`. Use the git-ignored `src/WabiInsight.Api/appsettings.Local.json` (see `appsettings.Local.example.json`), `dotnet user-secrets`, or environment variables such as `ConnectionStrings__ConfigurationDatabase`. CI validates `appsettings.json` and a committed secret breaks every PR.

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
dotnet tool restore && dotnet ef migrations add <Name> --project src/WabiInsight.Infrastructure --startup-project src/WabiInsight.Infrastructure --output-dir Persistence/Migrations

# Integration tests need SQL Server: either Docker (Testcontainers) or
WI_TEST_SQLSERVER_CONNECTION="Server=localhost,1433;Database=master;User Id=sa;Password=WabiInsight_Dev_2026!;TrustServerCertificate=True;Encrypt=True" dotnet test WabiInsight.slnx

# Web (from src/web)
npm ci
npm run lint && npm test && npm run build -- --configuration production
npm start                                          # http://localhost:4200, proxies /api to 5080
```
Screenshots and the demo GIF are captured with Playwright from the running app (see `docs/DESIGN_SYSTEM.md`); the full 42-screen script arrives in P11.2. Development uses the sample bootstrap values in `appsettings.Development.json`; any other environment must supply `Bootstrap__AdministratorPassword` and `Bootstrap__InitializationSecret` through secrets.

## Verification before pushing
1. `dotnet build` + `dotnet test` green.
2. `ng build` + `ng test` + `ng lint` green.
3. UI changes checked in the browser; real screenshots only.
4. `docs/PROGRESS.md` updated.
