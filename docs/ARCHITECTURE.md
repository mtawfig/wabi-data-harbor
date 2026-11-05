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
| `Roles` | Organization-scoped roles; system roles `OrganizationAdmin`, `Management` (+ `Analyst` template) provisioned at onboarding | unique `(OrganizationId, RoleCode)`; alternate key `(OrganizationId, RoleId)` |
| `UserRoles` | Role assignments | composite FKs `(OrganizationId, UserId)` and `(OrganizationId, RoleId)` so a user of A can never hold a role of B |
| `Departments` / `Sections` | Organization structure | unique codes per organization (sections per department); composite FKs keep a section inside its department's organization, and a user's `(OrganizationId, DepartmentId, SectionId)` inside one section of one department |
| `DefaultRoleTemplates` | Platform-managed role templates (seeded: OrganizationAdmin, Management, Analyst) | unique `RoleCode`; copied into new organizations only |
| `UsageEvents` | Append-only verified event stream feeding platform and organization metrics (logins, failures, provisioning, email delivery, API errors; AI/report/support codes arrive with their phases) | application guard rejects UPDATE/DELETE; runtime login denied; indexes on organization/type/date |
| `SupportTickets` | Support desk tickets (P4.1) | unique `ReferenceNumber`; composite FK `(OrganizationId, CreatedByUserId)` keeps the requester inside the ticket's organization; status check constraint; indexes on `(OrganizationId, Status)`, `(AssignedAdministratorId, Status)`, `LastActivityAt` |
| `TicketSequences` | Yearly counter behind `WI-YYYY-NNNNNN` | `UPDATE … OUTPUT` inside the create transaction, never MAX+1 |
| `SupportTicketEvents` | Append-only ticket timeline (created, viewed, status changes, reopen, assignment, comments, attachments) | application guard rejects UPDATE/DELETE; runtime login denied; composite FK to the ticket's `(OrganizationId, TicketId)` |
| `SupportTicketComments` / `SupportTicketAttachments` | Conversation and files (metadata; bytes live in `IArtifactStorage` under random keys) | composite FKs to the ticket; unique `StorageKey`; SHA-256 and scan status recorded |
| `Notifications` | In-app notification centre for organization users and platform administrators (codes + parameters, localized in the UI) | recipient index `(RecipientType, RecipientId, ReadAt, CreatedAt)`; services always filter by the current principal |
| `Applications` | Business applications; one built-in `WI-INSIGHTS` row per organization | unique `(OrganizationId, ApplicationCode)`, unique built-in code per organization, globally unique `ConnectionFingerprint`, built-in rows cannot carry a connection string (check constraint) |

Conventions: UUID v7 keys generated in code (`Ids.New()`), `datetimeoffset` UTC timestamps stamped by the context, `rowversion` concurrency tokens on editable aggregates, enum codes stored as strings with check constraints.

### Onboarding (P2.1)

`OrganizationService.CreateAsync` runs in one transaction: organization row → system roles from `SystemRoles.Templates` → built-in Platform Insights application → initial Organization Administrator through `OrganizationUserProvisioningService` (user, login identity, `OrganizationAdmin` role, audit) → temporary password emailed through the outbox. Role codes are loaded into the principal at login and on security-stamp revalidation, so the `OrganizationAdmin` policy and the admin navigation follow the stored assignments.

### Organization administration (P2.2)

`OrganizationUserService`, `OrganizationStructureService`, `RoleService` and `RoleTemplateService` throw `DomainException` with a stable outcome and error codes; `DomainExceptionHandler` maps them to problem responses (404/409/400) and `TenantContextException` to 403, so controllers stay thin and the Angular `problemMessage` helper localizes the codes. Guards: an administrator cannot deactivate themselves or drop their own admin role, and the organization always keeps at least one active administrator. Role changes rotate the user's security stamp so open sessions pick up the new roles.

### Usage metrics (P3.1)

`IUsageEventWriter` appends `UsageEvents` inside the same unit of work as the business change (login, failed login, logout, password change, organization created/status changed, user and administrator created, email queued/sent/failed, unhandled API errors). `PlatformMetricsService` aggregates per day in SQL (`GROUP BY CAST(CreatedAt AS date)`) and buckets by day/week/month in memory; it reads identity tables and the event stream only, so application names, prompts and business data never reach the platform dashboard. Organization-scoped metrics for the organization dashboard (P7.2) reuse the same stream through the tenant filter.

### Support desk (P4.1)

`SupportTicketService` resolves the caller's role from the tenant context only (requester, OrganizationAdmin of the ticket's organization, or Platform Administrator) and applies the §15.3 transition table through the pure `TicketStateMachine`. Platform reads use `AsPlatformScope`; organization reads go through the tenant filter plus a `CreatedByUserId` restriction for non-administrators. The first platform view records `FirstOpenedBy/At`, a `TicketViewed` timeline event and `NEW → READ` exactly once; later views only write an audit row. A requester or OrganizationAdmin reply on `PendingUserResponse` moves the ticket back to `Started` automatically. Attachments are limited to 10 MB and an extension allow-list, hashed, passed through `IAttachmentScanner` (no-op by default) and stored by `FileSystemArtifactStorage` under `Storage:RootPath` with random object names; platform downloads are audited (review C6). `NotificationDispatcher` writes `Notifications` rows in the same unit of work and queues bilingual emails through the outbox after the change is saved. Ticket KPIs (open tickets, critical incidents, tickets created, average first response and resolution) are now collected by `PlatformMetricsService`.

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
| `GET /api/platform/administrators` | Platform administrators only: list administrators (no hashes or stamps). |
| `POST /api/platform/administrators` | Platform administrators only: create an administrator; the temporary password is emailed. |
| `POST /api/platform/administrators/{id}/status` | Platform administrators only: activate/deactivate another administrator; refuses self-changes and deactivating the last active administrator; ends the target's sessions. |
| `POST /api/platform/administrators/{id}/reset-password` | Platform administrators only: reset another administrator; the temporary password is emailed. |
| `GET/POST /api/platform/organizations`, `GET/PUT …/{id}`, `POST …/{id}/status`, `POST …/{id}/initial-administrator/resend` | Platform administrators only: organization lifecycle and onboarding. Responses carry counts, never application names. |
| `GET /api/organization`, `PUT /api/organization` | The signed-in user's own organization (tenant context); PUT requires the `OrganizationAdmin` role. |
| `GET/POST /api/departments`, `PUT …/{id}`; `GET/POST /api/sections`, `PUT …/{id}` | Organization structure (reads for organization users, writes for `OrganizationAdmin`). |
| `GET/POST /api/roles`, `PUT/DELETE …/{id}` | Organization roles (`OrganizationAdmin`); system roles are protected. |
| `GET/POST /api/users`, `GET/PUT …/{id}`, `POST …/{id}/status`, `POST …/{id}/reset-password` | Organization user management (`OrganizationAdmin`); temporary passwords are emailed. |
| `GET/POST /api/platform/role-templates`, `PUT …/{id}` | Platform-managed default role templates (Platform Administrators). |
| `GET /api/platform/dashboard?from&to&bucket&organizationId` | KPI cards, time series, per-organization usage aggregates and recent errors from `UsageEvents`. Metrics of later phases carry `collected = false`. |
| `GET /api/platform/health` | Detailed on-demand health report (API, configuration database, email provider configuration, outbox backlog). |
| `GET /api/platform/audit`, `GET …/event-types` | Paged, filtered audit trail for Platform Administrators. |
| `GET/POST /api/support/tickets`, `GET …/{id}`, `GET …/{id}/events`, `GET/POST …/{id}/comments`, `POST …/{id}/status`, `GET/POST …/{id}/attachments`, `GET …/{id}/attachments/{attachmentId}` | Organization area support desk: requester sees own tickets, `OrganizationAdmin` all of the organization's. |
| `GET /api/platform/support/tickets`, same sub-resources, plus `POST …/{id}/assign` | Platform queue across organizations (Platform Administrators), first-open rule, assignment. |
| `GET /api/notifications`, `GET …/unread-count`, `POST …/{id}/read`, `POST …/read-all` | Notification centre of the signed-in principal. |

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

### Support desk and notifications (P4.2)
`SupportService` and `NotificationsService` in `core/`; `features/support/` holds the ticket list, form, detail (tabs), notification centre and dashboard widget, each parameterised by `area` so one component serves `/app/support…` and `/platform/support…`. Routes: `/app/support`, `/app/support/new`, `/app/support/:id`, `/app/notifications`, `/platform/support`, `/platform/support/:id`, `/platform/notifications`.

## Tenant model, security, data model

See `docs/WABI_INSIGHT_SPECIFICATION.md` §4, §10, §27 and `docs/SECURITY.md` (written in P1.3).
