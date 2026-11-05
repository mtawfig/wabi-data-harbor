# Wabi Insight — Architecture

Status: P1.1 skeleton, P1.2 configuration database and tenant core, P1.3 authentication and bootstrap; each phase extends the relevant section.

## Three logical layers

| Layer | Location | Responsibility |
|---|---|---|
| 1 — Angular web application | `src/web` | One Angular 22 app with public, organization and platform areas (EN/AR, LTR/RTL). |
| 2 — ASP.NET Core Web API | `src/WabiInsight.Api` (host) + `src/WabiInsight.Core` (domain, services, interfaces, query-plan model, compilers) + `src/WabiInsight.Infrastructure` (EF Core, DB adapters, providers) | One deployable API. The two class libraries are build organization inside this layer, not extra layers. |
| 3 — Data and integrations | SQL Server configuration DB, organization SQL Server/Oracle sources, LLM endpoints, SMTP, Google Drive, OneDrive, artifact storage | Reached only through Infrastructure. |

## Repository layout

```
WabiInsight.slnx
Directory.Build.props          shared .NET settings (net10.0, warnings as errors, analyzers)
src/WabiInsight.Api            controllers, middleware, Program.cs
src/WabiInsight.Core           entities, services, interfaces (no framework dependencies beyond abstractions)
src/WabiInsight.Infrastructure EF Core DbContext, migrations, providers, health checks
src/web                        Angular application
tests/WabiInsight.UnitTests    fast tests, no external services
tests/WabiInsight.IntegrationTests  WebApplicationFactory + SQL Server (from P1.2)
docs/                          specification, plan, progress, architecture, security, design system
.github/workflows/ci.yml       build + test for API and web on every PR
docker-compose.dev.yml         local SQL Server 2022
```

## Configuration database (P1.2)

One SQL Server database, EF Core 10, migrations in `src/WabiInsight.Infrastructure/Persistence/Migrations`, applied by `dotnet run --project src/WabiInsight.Api -- migrate` at deployment time (never by a web request).

| Table | Purpose | Key constraints |
|---|---|---|
| `Organizations` | Customer tenants | unique `OrganizationCode`; `Status` check (`Active`, `Suspended`, `Deactivated`); `DefaultLanguage` check (`en`, `ar`) |
| `Administrators` | Platform Administrators, **no `OrganizationId`** | unique `NormalizedEmail`; filtered unique index allows exactly one `IsBootstrapAdministrator` |
| `OrganizationUsers` | Employees, **`OrganizationId` NOT NULL + FK** | unique `(OrganizationId, NormalizedEmail)`; unique `(OrganizationId, EmployeeNumber)`; `Status` check |
| `LoginIdentities` | Global login registry across both stores | primary key `NormalizedEmail`; unique `(PrincipalType, PrincipalId)` |
| `SystemInitialization` | Bootstrap state | singleton (`Id = 1` check constraint) |
| `PlatformSettings` | Key/value platform settings | primary key `Key` |
| `AuditLogs` | Append-only security events | application guard rejects UPDATE/DELETE; `scripts/sql/create-runtime-login.sql` denies them at the database |
| `EmailMessages` | Transactional email outbox/log (metadata only, no bodies) | `Status` check (`Queued`, `Sent`, `Failed`); indexes on organization, status and template |

Conventions: UUID v7 keys generated in code (`Ids.New()`), `datetimeoffset` UTC timestamps stamped by the context, `rowversion` concurrency tokens on editable aggregates, enum codes stored as strings with check constraints.

### Tenant core

- `TenantPrincipal` is the trusted identity (type, organization, user/administrator id, roles, department, section). `TenantContext` is scoped per request, initialized once by the authentication middleware (P1.3) and read through `ITenantContext`.
- `ConfigurationDbContext` applies a global query filter `OrganizationId == CurrentOrganizationId` to every `ITenantOwned` entity. Anonymous and platform-administrator scopes see **no** tenant rows by default.
- `AsPlatformScope(tenantContext)` is the only sanctioned cross-organization read; it throws for anything but a platform administrator and callers must apply field-level restrictions.
- Write guard in `SaveChanges`: `OrganizationId` must be set, organization users cannot write outside their organization, anonymous scopes cannot write tenant data, `AuditLogs` cannot be modified or deleted.

### Test strategy

Integration tests run against a real SQL Server: `WI_TEST_SQLSERVER_CONNECTION` (CI service container or a local instance) or, when absent, a Testcontainers SQL Server 2022 container. A fresh database is migrated per run and seeded with two organizations, one user each, and one administrator. SQLite or in-memory providers are never used because constraints, filtered indexes and `rowversion` must be verified on the real engine.

## Request pipeline (P1.3)

`UseExceptionHandler` (RFC 9457 problem details) → `UseStatusCodePages` → HSTS (non-development) → HTTPS redirection → rate limiter → cookie authentication (with security-stamp revalidation) → `TenantContextMiddleware` (claims → `TenantPrincipal`) → `PasswordChangeRequiredMiddleware` → authorization (fallback: authenticated) → MVC with the `AntiforgeryValidationFilter` → controllers. Health endpoints are mapped with `AllowAnonymous`.

Background work: `EmailOutboxWorker` (hosted service) consumes an in-process bounded channel fed by `EmailOutbox`, sends through the configured `IEmailSender` (`SmtpEmailSender`, `FileEmailSender`, `NullEmailSender`) and records the outcome on the `EmailMessages` row.

CLI commands handled before the pipeline starts: `migrate` (apply migrations) and `bootstrap` (create the first Platform Administrator). See `docs/SECURITY.md` for the authentication design.

## Endpoints (P1.1)

| Endpoint | Purpose |
|---|---|
| `GET /health/live` | Process liveness; JSON with per-check status. |
| `GET /health/ready` | Readiness: API self-check plus configuration-database connectivity (P1.2). Storage, email and AI provider checks follow in later phases. |
| `GET /api/platform/info` | Public product identity (name, code, tagline, version, languages). Never exposes configuration or bootstrap state. |
| `GET /api/auth/csrf` | Antiforgery cookie + request token (anonymous). |
| `POST /api/auth/login` | Email/password login for both account categories; sets the `wi.session` cookie. Rate-limited. |
| `POST /api/auth/logout` | Ends the session (audited). |
| `GET /api/auth/me` | Current session: principal type, organization, roles, must-change-password flag. |
| `POST /api/auth/change-password` | Changes the password, rotates the security stamp, clears must-change-password. |
| `GET /api/bootstrap/status` | Bootstrap completion state (anonymous, non-sensitive). |
| `POST /api/bootstrap/initialize` | One-time first-administrator creation with the initialization secret. Rate-limited. |
| `POST /api/platform/administrators` | Platform administrators only: create an administrator; the temporary password is emailed. |
| `POST /api/platform/administrators/{id}/reset-password` | Platform administrators only: reset another administrator; the temporary password is emailed. |

## Angular (P1.4)

Standalone components with signals; `provideHttpClient(withFetch(), withInterceptors([authInterceptor]))`; `proxy.conf.json` forwards `/api` and `/health` to port 5080 during `ng serve`.

```
src/app/core/auth        AuthService (session signal, csrf, login/logout/change-password, homeUrl), interceptor (X-XSRF-TOKEN, 401 → login), guards
src/app/core/i18n        LanguageService (lang/dir, persistence), Transloco HTTP loader
src/app/shared/layout    AppShellComponent (header, sidebar, user menu)
src/app/shared/ui        BrandLogo, LanguageSwitcher, PagePlaceholder
src/app/features/public  landing, login, bootstrap
src/app/features/auth    change-password
src/app/features/organization  shell + navigation, ask workspace, user dashboard
src/app/features/platform      shell + navigation, overview, general settings (bootstrap status)
public/i18n              en.json, ar.json
```

Startup: `provideAppInitializer` initializes the language and calls `GET /api/auth/csrf` + `GET /api/auth/me` to restore a session. Guards are UX only; the API enforces authorization. Routes whose feature belongs to a later phase render `PagePlaceholderComponent` with the phase number.

## Tenant model, security, data model

See `docs/WABI_INSIGHT_SPECIFICATION.md` §4, §10, §27 and `docs/SECURITY.md` (written in P1.3).
