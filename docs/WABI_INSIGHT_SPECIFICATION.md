# WABI INSIGHT — Enterprise AI Intelligence Platform
## Product Specification v1.1 (Multi-Organization Edition, revised)

| | |
|---|---|
| **Product** | Wabi Insight |
| **Tagline** | Ask Your Data. Trust the Answer. · اسأل بياناتك. وثق بالإجابة. |
| **Platform code** | AP-AIR |
| **Languages** | English (LTR), Arabic (RTL) |
| **Architecture** | Exactly three logical layers |
| **Authentication** | Local email + password only |
| **Configuration database** | One central SQL Server database |
| **Business data sources** | Microsoft SQL Server, Oracle |
| **Deployment model** | Multi-organization, logically isolated |
| **Frameworks** | .NET 10 LTS · Angular 22 · EF Core 10 · SQL Server 2022 |

This document is the revised master specification. Paragraphs tagged **[Revised]** change the original prompt; paragraphs tagged **[Decision]** fill a gap the prompt left open. The reasoning for each is in [`SPEC_REVIEW.md`](./SPEC_REVIEW.md). The delivery order is in [`IMPLEMENTATION_PLAN.md`](./IMPLEMENTATION_PLAN.md).

---

## 1. Mission

Wabi Insight lets multiple customer organizations use one shared application while keeping their users, configuration, permissions, business data, analyses, reports and support activity isolated. Employees ask natural-language questions about data they are authorized to see, get verified results, charts and reports, and share them under control. The company operating Wabi Insight (the "platform company") has a separate administration and support environment that lets it operate, monitor, onboard and support without access to customer business data.

Priorities, in order: security and tenant isolation; correctness and authorization; working end-to-end functionality; simplicity and maintainability; professional responsive UI; performance and extensibility.

---

## 2. Engineering practices

- Verified stack (2026-11-05): .NET 10 LTS, ASP.NET Core 10, EF Core 10, Angular 22.2, Node 22, SQL Server 2022, Oracle 23ai Free for tests.
- Maintain `CLAUDE.md`, `docs/IMPLEMENTATION_PLAN.md`, `docs/PROGRESS.md`, `docs/ARCHITECTURE.md`, `docs/SECURITY.md`, `docs/DESIGN_SYSTEM.md`. `CLAUDE.md` stays concise and never copies this specification.
- Work in small PRs, one per branch, each green in CI (build + tests for API and Angular) before the owner merges to `main`.
- Each PR: write code, build, run tests, fix, verify integrations, check user-facing behaviour in a browser, capture **real** screenshots when UI changes, update `docs/PROGRESS.md`. Blocked integrations and environment requirements are documented truthfully.

---

## 3. Three logical layers

### Layer 1 — Angular web application (one app)
- **Public area:** landing page, login, bootstrap page (status + one-time initialization), invitation acceptance, forced password change.
- **Organization area:** dashboard, AI workspace, history, reports, favorites and collections, internal and external sharing, notifications, profile, support tickets, organization administration.
- **Platform administration area:** platform dashboard, organizations, platform administrators, global settings, default role templates, Platform Insights configuration, support desk, support access, system health, audit logs.
- All areas in English and Arabic.

### Layer 2 — ASP.NET Core Web API (one deployable)
Internal services (interfaces + DI, async): Authentication, Bootstrap, Organization, PlatformAdministrator, OrganizationUser, Role, Permission, TenantContext, Application, Metadata, Database, AI, Analysis, History, Report, Sharing, ExternalStorage, Email, SupportTicket, SupportAccess, Notification, PlatformMetrics, Audit, Settings.

**[Decision]** Source is organized as `WabiInsight.Api` (host, controllers, middleware), `WabiInsight.Core` (entities, service interfaces and implementations, query-plan model, compilers) and `WabiInsight.Infrastructure` (EF Core, SQL Server/Oracle adapters, AI/email/storage providers). These are build-time projects inside Layer 2, not extra logical layers. Background work uses in-process hosted services fed by bounded channels; no brokers.

### Layer 3 — Data and integrations
One SQL Server configuration database; organization-owned SQL Server and Oracle connections; cloud LLM endpoints and future on-premises OpenAI-compatible endpoints; SMTP email; Google Drive; OneDrive (Microsoft Graph); secure artifact storage. No microservices, message brokers, agent frameworks or separate Python applications.

---

## 4. Multi-organization architecture

Each organization is a logical tenant with independent users, administrators, departments, sections, roles, role assignments, applications, connections, registered objects, column metadata, permissions, analysis history, reports, artifacts, shares, integration connections, support tickets, settings and usage records. An organization can never read another organization's private configuration or business information.

### 4.1 `Organizations`
`OrganizationId` (uuid v7), `OrganizationCode` (unique, immutable, e.g. `ACME`), `OrganizationName`, `OrganizationNameAr`, `Description`, `ContactEmail`, `ContactPhone`, `CountryCode`, `TimeZone`, `DefaultLanguage` (`en`|`ar`), `Status` (`ACTIVE`|`SUSPENDED`|`DEACTIVATED`), `CreatedAt`, `CreatedByAdministratorId`, `UpdatedAt`, `UpdatedByAdministratorId`, `RowVersion`.

**[Decision]** `IsActive` is replaced by the `Status` code so that suspension and deactivation are distinguishable. Only Platform Administrators create organizations. Suspension and deactivation never delete records.

**[Decision] Suspended / deactivated policy:** users of the organization cannot sign in and existing sessions are rejected on the next request; background work for the organization is skipped; data is retained; the organization's tickets remain visible to Platform Administrators; reactivation restores access without data changes.

### 4.2 Mandatory ownership
Every organization user has a required `OrganizationId`. Every organization-owned entity carries an explicit `OrganizationId` column (preferred, including histories, shares, reports, tickets, applications) or a database-enforced relationship to a tenant-owned parent. Foreign keys, organization-scoped unique constraints and service validation prevent cross-organization references.

### 4.3 Tenant context
`ITenantContext` resolves, from the authenticated principal and configuration records only: `PrincipalType`, `OrganizationId`, `UserId`, organization roles, `DepartmentId`, `SectionId`, account status, organization status. An `OrganizationId` arriving in a route, query string, body, header or AI output is never authoritative. Tenant-facing endpoints scope all queries by the context. EF Core global query filters are applied to tenant entities **and** services validate ownership explicitly; filters are never the only boundary. Platform-wide operations use dedicated administrative query paths with field-level restrictions and explicit authorization, never by disabling filters casually.

### 4.4 External business databases
An organization owns its application connections; another organization can never select or execute them. **[Revised]** Version 1 supports only organization-dedicated source databases. A connection string already registered by another organization (compared by keyed hash of the normalized string) is rejected. Shared physical business databases with tenant-scope mappings are deferred to a later version; the system never executes a query against a source whose ownership is not established.

---

## 5. Two administrative levels

### 5.1 Platform Administrator
Stored in `Administrators`: `AdministratorId`, `Email`, `NormalizedEmail` (unique), `FullName`, `PasswordHash`, `IsActive`, `MustChangePassword`, `IsBootstrapAdministrator`, `SecurityStamp`, `FailedLoginCount`, `LockoutEnd`, `LastLoginAt`, `PasswordChangedAt`, `PreferredLanguage`, `CreatedAt`, `UpdatedAt`, `CreatedByAdministratorId`, `RowVersion`.

Platform Administrators have no `OrganizationId`, are never inserted into `OrganizationUsers`, and their principal carries `principal_type = PlatformAdministrator`.

**[Decision]** An administrator cannot deactivate or delete their own account; at least one active Platform Administrator must always remain; the bootstrap administrator can be deactivated by another administrator but never deleted.

### 5.2 Platform Administrator permissions
Can: create and manage organizations; activate/suspend/deactivate them; create the initial Organization Administrator during onboarding; manage other Platform Administrators; configure platform settings, default role templates and the built-in Platform Insights catalog; view aggregated metrics and health; manage the support queue, respond to and resolve tickets; perform explicitly approved, temporary, scoped support actions in support mode.

Cannot by default: open organization query pages, AI results, report contents, document libraries, connection strings, application names/lists, metadata or permission editors, or organization users' private information beyond what onboarding, billing, support routing and operations require. The platform dashboard shows the **count** of configured applications, never names or connection details. Platform Administrators hold no tenant privileges of their own.

### 5.3 Organization Administrator
Protected system role `OrganizationAdmin`, stored as a role in each organization and assigned to `OrganizationUsers`. Manages only that organization's users, departments, sections, roles, data permissions, applications, connections, metadata, AI provider selection (within platform policy), reports and sharing settings, support tickets, organization settings and usage dashboards. May hold business-data roles too; `OrganizationAdmin` alone grants no business-data access.

### 5.4 Management role
Protected organization-scoped role `Management`: automatic, read-only access to all **approved** registered objects, columns and rows of its own organization (`ALL` scope). Grants no administration privileges and nothing in other organizations.

---

## 6. Organization onboarding
1. Platform Administrator creates the organization (code, names, contact, country, timezone, default language).
2. Backend assigns `OrganizationId` and validates the code.
3. Protected role templates are provisioned (`OrganizationAdmin`, `Management`, `Analyst`, …) with the template version recorded.
4. The built-in Platform Insights application row is created for the organization (§12).
5. Platform Administrator enters the initial Organization Administrator's email and full name.
6. A pending organization user with the `OrganizationAdmin` role is created (`Status = PENDING_INVITATION`).
7. A single-use invitation token (hashed at rest, 72-hour expiry, configurable) is sent by email. If email is unavailable, the Platform Administrator can display the invitation link **once** for administrator-assisted handover; the event is audited.
8. The administrator opens the link, sets a password, and the account becomes active.
9. They sign in and configure departments, sections, users, applications, metadata and permissions.

This onboarding provisioning is the only case where a Platform Administrator creates an organization user without a support grant. Plain-text credentials and tokens never appear in logs.

---

## 7. Platform Administrator bootstrap

### 7.1 Configuration (before migration)
Settings (environment variables, user secrets, deployment secret store, or an ignored local file; never committed):
`Bootstrap:Enabled`, `Bootstrap:AdministratorEmail` (default `support@wabitex.com`), `Bootstrap:AdministratorFullName`, `Bootstrap:AdministratorPassword`, `Bootstrap:InitializationSecret`.

The password `my_default_password_change_it` is accepted **only** in the `Development` environment as the explicit sample default. In any other environment, bootstrap with that password, an empty password, or a missing `InitializationSecret` is rejected. Migration alone never creates an account.

### 7.2 Execution **[Revised]**
Two entry points share one `BootstrapService`:
- `dotnet WabiInsight.Api.dll bootstrap` (CLI, for headless deployment), and
- `POST /api/bootstrap` requiring the `InitializationSecret`, active only while `SystemInitialization.BootstrapCompleted = false`, rate-limited.

Both: verify authorization and bootstrap state → verify the schema is at the current migration (migrations are applied beforehand by `dotnet ef database update` or the `migrate` CLI command, never by the web request) → create the administrator with hashed password, `IsBootstrapAdministrator = true`, `MustChangePassword = true` → register the login identity → write `SystemInitialization` (`BootstrapCompleted`, `BootstrapCompletedAt`, `BootstrapAdministratorId`, `SchemaVersion`) → audit. A unique constraint on the single `SystemInitialization` row makes repeated or concurrent attempts fail atomically.

### 7.3 Bootstrap administration UI
Platform Administration → General Settings shows: bootstrap completed, date, bootstrap administrator email, schema version, initialization status. No post-initialization action recreates the original administrator. Further Platform Administrators are created through protected administration.

---

## 8. Authentication
Local email/password only; no SSO, directory, social login or public registration in v1. One public login screen serves both account categories; the backend resolves the category through `LoginIdentities`.

**[Decision] `LoginIdentities`:** `NormalizedEmail` (primary key), `PrincipalType`, `PrincipalId`, `CreatedAt`. Every administrator and organization user creation inserts here inside the same transaction, guaranteeing global uniqueness across both stores.

**[Revised] Identity primitives:** ASP.NET Core Identity's `PasswordHasher<T>` (PBKDF2, v3 format), `UpperInvariantLookupNormalizer`, lockout and security-stamp semantics are used over the two custom tables through `AuthenticationService`. One cookie authentication scheme carries claims: `sub`, `principal_type`, `org` (organization users only), `security_stamp`, `must_change_password`, `lang`.

**[Decision] Policies:** password 12–128 characters, no composition rules, not equal to the email, sample password rejected outside Development; lockout after 5 failures for 15 minutes (configurable); identical error for unknown email / wrong password; sliding idle timeout 30 min and absolute lifetime 12 h (platform-configurable); cookies `HttpOnly; Secure; SameSite=Lax`; antiforgery via `XSRF-TOKEN` cookie and `X-XSRF-TOKEN` header; security stamp re-validated at most every 5 minutes so password change, deactivation, suspension and administrator reset invalidate sessions.

Supported: login/logout, password change, forced first-login change, administrator-assisted reset (one-time reset token or temporary password with `MustChangePassword`), lockout, inactive-account and suspended-organization denial, expiration, invalidation.

---

## 9. Organization users and structure

`OrganizationUsers`: `UserId`, `OrganizationId` (required), `Email`, `NormalizedEmail`, `FullName`, `EmployeeNumber`, `PhoneNumber`, `DepartmentId`, `SectionId`, `PreferredLanguage`, `PreferredLocale`, `PreferredTimezone`, `Status` (`PENDING_INVITATION`|`ACTIVE`|`INACTIVE`), `MustChangePassword`, `PasswordHash`, `SecurityStamp`, `FailedLoginCount`, `LockoutEnd`, `LastLoginAt`, `PasswordChangedAt`, `CreatedAt`, `CreatedByUserId`, `CreatedByAdministratorId` (onboarding or support only), `UpdatedAt`, `UpdatedByUserId`, `RowVersion`. Unique: `(OrganizationId, NormalizedEmail)`, `(OrganizationId, EmployeeNumber)` where not null.

`Departments`: `DepartmentId`, `OrganizationId`, `DepartmentCode`, `DepartmentNameEn`, `DepartmentNameAr`, `IsActive`; unique `(OrganizationId, DepartmentCode)`.
`Sections`: `SectionId`, `OrganizationId`, `DepartmentId`, `SectionCode`, `SectionNameEn`, `SectionNameAr`, `IsActive`; unique `(OrganizationId, DepartmentId, SectionCode)`; a user's section must belong to the user's department and organization (validated and enforced by a composite foreign key).

Organization Administrators manage users only in their organization and assign only their organization's roles. After onboarding, Platform Administrators touch organization users only through an approved support-access grant.

---

## 10. Roles, permissions and tenant isolation

Organization-scoped tables: `Roles` (`RoleId`, `OrganizationId`, `RoleCode`, `NameEn`, `NameAr`, `IsSystem`, `TemplateVersion`), `UserRoles`, `RoleTablePermissions`, `RoleColumnPermissions`, `RoleRowPermissions`, `RowScopeColumnMappings`. Unique `(OrganizationId, RoleCode)`. A role can only be assigned to users of the same organization (composite FK on `OrganizationId`).

**Default role templates** (`DefaultRoleTemplates`, platform-managed, versioned): `OrganizationAdmin`, `Management`, `Analyst`. Templates are copied at organization creation; later template changes never modify existing organizations' roles automatically. A future "apply template update" action is explicit and audited.

**Table level:** users access only explicitly granted, approved registered tables/views of their organization.
**Column level:** only granted columns, checked for every reference: SELECT, WHERE, JOIN, GROUP BY, HAVING, ORDER BY, calculations and aggregates.
**Row level:** scopes `ALL`, `DEPARTMENT`, `SECTION`, `SELF`. Organization isolation is mandatory and evaluated first; `ALL` means all rows of the authorized object within the organization.

**[Decision] Row-scope mapping:** `RowScopeColumnMappings` names, per registered object, the column holding the employee identifier, the department code and the section code. Predicates compare them with the authenticated user's `EmployeeNumber`, `Departments.DepartmentCode` and `Sections.SectionCode`. A scope whose mapping is absent yields no rows.
**[Decision] Combination:** effective permissions are the union across the user's roles; row predicates combine with OR; `Management` contributes `ALL` on every approved object; nothing widens the organization boundary.

**Enforcement:** no SQL Server RLS or Oracle VPD in v1. The backend validates a typed query plan and a dialect compiler emits SQL with predicates applied at the source-table level (before joins and aggregations). No WHERE clause is ever appended to arbitrary SQL; unsupported or unsafe plan shapes are rejected; dedicated read-only database accounts are required; the LLM never defines authorization.

---

## 11. Organization business applications

`Applications`: `ApplicationId`, `OrganizationId` (required), `ApplicationName`, `ApplicationCode`, `Description`, `DatabaseType` (`SQLSERVER`|`ORACLE`|`PLATFORM_INSIGHTS`), `EncryptedConnectionString` (null for built-in), `ConnectionFingerprint` (keyed hash, unique), `IsBuiltIn`, `BuiltInCode`, `IsActive`, `IsApproved`, `CreatedAt`, `CreatedByUserId`, `UpdatedAt`, `RowVersion`. Unique `(OrganizationId, ApplicationCode)`.

**[Revised]** Every application row is tenant-owned, including the built-in one (§12). Organization Administrators configure their applications; Platform Administrators never browse application lists; the platform dashboard shows aggregated counts only.

**Connection protection:** connection strings are encrypted with ASP.NET Core Data Protection using a purpose-specific protector; the key ring is persisted outside the configuration database (file system or key vault) with automatic key rotation and retained old keys for decryption. Decrypted values exist only in server memory for authorized operations. The API returns only "Connection configured" plus the server/database display hint the administrator typed; replacement and "test connection" are supported. Test connection also verifies the account is read-only where the engine allows it.

---

## 12. Built-in Platform Insights application

Code `WI-INSIGHTS`, `DatabaseType = PLATFORM_INSIGHTS`, one row per organization. Data comes from the configuration database through approved, read-only, tenant-scoped SQL views.

### 12.1 Security **[Decision]**
- Views live in an `insights` schema; every view exposes `OrganizationId`; they never expose password hashes, secrets, prompt text, raw configuration tables, other organizations, or protected administration data.
- A dedicated SQL login with SELECT on `insights` only executes insights queries.
- The compiler injects `OrganizationId = @tenant` as a mandatory predicate from `ITenantContext`; no user input can change it.
- The object/column catalog for `WI-INSIGHTS` is global (`OrganizationId` null on catalog rows is allowed **only** for this catalog, which is marked `IsGlobalCatalog = true`), maintained by Platform Administrators.
- Per-user detail is limited to counts per user; prompt content is never exposed through insights (owner may relax, see review C5).

### 12.2 Default permissions
`OrganizationAdmin` receives read access to all approved insights metrics automatically. Other roles receive specific metrics only when the Organization Administrator grants them. Platform Administrators manage the catalog and metric definitions without accessing private organization applications.

### 12.3 Metrics
Total users, active users, logins, analyses generated/completed/failed, reports created, email drafts generated vs emails sent, downloads, prints (where logged), internal shares, external sharing operations, support tickets by status/type/impact, department usage, monthly trends, AI token usage, estimated AI cost when pricing is configured, failed queries and operational errors. All metrics derive from `UsageEvents` and related tables, never from AI output.

---

## 13. Platform Administrator dashboard (`/platform/dashboard`)
KPI cards: total/active/suspended organizations; total organization users; active users in period; configured application count; AI requests; completed and failed analyses; reports generated; email drafts; open tickets; critical incidents; average first response and resolution times.
Operational health: API health, request success rate, average and p95 latency, AI provider connectivity, query failure rate, background worker failures, storage usage, report generation failures, email delivery failures, integration error summary, configuration DB health; each with status and last-checked timestamp.
Charts (brand blue/aqua): organizations over time, active users, AI usage, requests by day/week/month, report trends, ticket volume/type/impact/status, response and resolution time trends, error trends.
Filters: date range presets (today, 7 days, 30 days, this month, custom), organization (aggregate support/operational metrics only), ticket type, impact, status, assigned administrator. Filters affect cards, charts and tables consistently.
Drilldown tables: onboarding summary, usage aggregates, ticket queue, health events, platform errors. No link bypasses privacy restrictions.

**[Revised]** The dashboard framework ships in Phase 3; ticket, AI, report and sharing widgets are added by the phases that produce the data.

---

## 14. Organization Administrator dashboard (`/app/admin/dashboard`)
Organization-only: users, active users, departments, sections, configured applications (names visible), registered objects, reports generated, analyses completed, AI usage, support tickets, sharing activity, operational indicators; date and other filters. Delivered in Phase 7 together with Organization Insights.

---

## 15. Support desk module
Shared across the platform; all organization-facing access is tenant-scoped.

### 15.1 Types — `REQUEST`, `INCIDENT` (localized labels).
### 15.2 Impact — `LOW`, `MEDIUM`, `HIGH`, `CRITICAL`; labels configurable, codes stable.
### 15.3 Status and transitions **[Revised]**
Codes: `NEW`, `READ`, `STARTED`, `PENDING_USER_RESPONSE`, `CANCELLED`, `RESOLVED` (label "Resolved" / "تم الحل").

| From | To | Who |
|---|---|---|
| NEW | READ | automatic on first Platform Administrator view |
| NEW, READ | STARTED | Platform Administrator |
| NEW, READ | CANCELLED | requester, OrganizationAdmin of the org, Platform Administrator |
| STARTED | PENDING_USER_RESPONSE | Platform Administrator |
| PENDING_USER_RESPONSE | STARTED | automatic on requester/OrganizationAdmin reply, or Platform Administrator |
| STARTED, PENDING_USER_RESPONSE | RESOLVED | Platform Administrator |
| STARTED, PENDING_USER_RESPONSE | CANCELLED | Platform Administrator, or requester with reason |
| RESOLVED | STARTED (reopen) | requester, OrganizationAdmin, Platform Administrator, within 14 days (configurable) |

Any other transition is rejected with a validation error; nothing fails silently.

### 15.4 Reference — `WI-YYYY-NNNNNN` from `TicketSequences(Year, NextValue)` updated atomically inside the create transaction. No MAX+1.
### 15.5 `SupportTickets`
`TicketId`, `ReferenceNumber` (unique), `OrganizationId`, `CreatedByUserId`, `TicketType`, `Impact`, `Category`, `Subject`, `Description`, `Status`, `AssignedAdministratorId`, `FirstOpenedByAdministratorId`, `FirstOpenedAt`, `StartedAt`, `PendingResponseAt`, `ResolvedAt`, `CancelledAt`, `ReopenedCount`, `CreatedAt`, `UpdatedAt`, `LastActivityAt`, `ResolutionSummary`, `RequiresSupportAccess`, `RowVersion`. Indexes on `(OrganizationId, Status)`, `(AssignedAdministratorId, Status)`, `LastActivityAt`.
### 15.6 Create (`/app/support/new`) — type, impact, category, subject, description, optional attachments; reference, organization, creator, timestamp and `NEW` set by the server. Requester identity and organization come from the tenant context.
### 15.7 Lists (`/app/support`, platform queue `/platform/support`) **[Decision]** requester sees own tickets; OrganizationAdmin sees all of the organization's; Platform Administrators see the platform queue. Columns as specified; search, sort, pagination, date/type/impact/status/assignment filters.
### 15.8 Details (`/app/support/:id`, `/platform/support/:id`) — header (reference, subject, type, impact, status, created, owner, assigned) and tabs: Overview; Conversation (chronological, attributed, authorized replies); Activity Timeline (mandatory, stored events, connected vertical timeline with icons; each event shows type, old/new values, actor name, actor category, timestamp, note); Attachments (authorized, validated upload/download through API, never predictable public paths); Support Access (only when `RequiresSupportAccess`).
### 15.9 First open — first Platform Administrator view records `FirstOpenedByAdministratorId`, `FirstOpenedAt`, a `TicketViewed` event and `NEW → READ`. Later views never overwrite those fields and only write an audit entry, not timeline events.
### 15.10 Activity tables — `SupportTicketEvents` (`EventId`, `TicketId`, `OrganizationId`, `ActorType`, `ActorId`, `ActorDisplayName`, `EventType`, `PreviousStatus`, `NewStatus`, `DetailsJson`, `CreatedAt`; append-only), `SupportTicketComments`, `SupportTicketAttachments` (10 MB, type allow-list, stored through `IArtifactStorage`, virus-scan hook point).

---

## 16. Support requests for administrative assistance
Platform Administrators never configure an organization's users, security, metadata or applications without an approved grant; a ticket alone grants nothing.

### 16.1 Workflow
Organization user or administrator creates a ticket → Platform Administrator reviews and proposes the exact operations needed (from the typed catalog) → an active `OrganizationAdmin` of that organization approves (**[Decision]** the requester may approve their own request if they are an OrganizationAdmin unless the organization enables `RequireSecondAdminApprovalForSupportAccess`) → a grant is issued with expiry → the assigned Platform Administrator enters support mode → performs only permitted operations → every action is recorded on the ticket and in audit → access expires or is revoked → ticket resolved.

### 16.2 `SupportAccessGrants`
`GrantId`, `TicketId`, `OrganizationId`, `RequestedByUserId`, `ProposedByAdministratorId`, `ApprovedByOrganizationAdminUserId`, `AssignedPlatformAdministratorId`, `ScopeType`, `AllowedOperationsJson` (typed catalog entries), `ApprovedObjectReferences` (specific user/role/object ids), `Reason`, `ApprovedAt`, `ExpiresAt` (default 4 h, max 72 h), `RevokedAt`, `RevokedBy`, `Status` (`PROPOSED`|`APPROVED`|`ACTIVE`|`EXPIRED`|`REVOKED`|`COMPLETED`), `RowVersion`.
Operation catalog (v1): `CreateOrganizationUser`, `ResetOrganizationUserPassword`, `ConfigureRole`, `UpdateTablePermissions`, `RefreshMetadata`, `DiagnoseApplicationConnection`, `UpdateOrganizationSettings`. Each names the specific target references. No global tenant administration grant exists.

### 16.3 Support session
Bound to one administrator, one organization, one ticket, one grant; explicit entry and exit; visible banner in the UI; scope and expiry enforced per operation by a dedicated authorization handler; old/new values audited (secrets excluded); immediate revocation by any OrganizationAdmin of the organization or the administrator. Support mode never grants business-data access; diagnosing a data object uses the narrowest mechanism (connectivity, object existence, row count) and any business-data exposure requires a separate, explicit grant operation that v1 does not include. No silent impersonation.

### 16.4 Auditability
Every assisted change is attributable to administrator, organization, ticket, grant, timestamp, operation, affected resource and outcome, and appears in both the ticket timeline and `AuditLogs`.

---

## 17. Metadata management
Discovery of tables/views, columns, data types, primary and foreign keys; business descriptions; EN/AR display names; object and column approval; refresh with diff (new objects/columns stay unapproved, removed ones are flagged). Organization Administrators manage only their applications' metadata. Platform Administrators reach organization metadata only through a `RefreshMetadata` grant. The Platform Insights catalog is global and read-only for organizations. Business descriptions are user content and are treated as untrusted when passed to the LLM (length-limited, delimited, never interpreted as instructions).

---

## 18. AI integration
`IAiProvider` abstraction with adapters: OpenAI-compatible chat completions (OpenAI, Mistral, local vLLM/Ollama/LM Studio endpoints) and Gemini; Anthropic Messages API optional (review C3). `AIProviderConfigurations` holds provider type, endpoint, model, encrypted credentials, pricing for cost estimates, and scope (`PLATFORM` default or `ORGANIZATION` override) within the platform allow-list. Restricted organization data never goes to an unapproved provider and there is no automatic fallback to an external model for restricted workloads. Structured output (JSON schema / JSON mode) is requested where supported and always validated server-side.

---

## 19. Secure AI query workflow
1. Authenticate principal. 2. Resolve tenant context. 3. Resolve the selected application and validate ownership. 4. Resolve effective table/column/row permissions. 5. Build the authorized metadata snapshot (approved, permitted objects and columns only). 6. Send prompt + snapshot + output-language instruction to the selected approved provider with injection safeguards. 7. Receive a structured query plan (JSON). 8. Validate shape (allowed operators, functions, join forms, limits). 9. Authorize every object and column reference. 10. Attach mandatory organization/application scope and row predicates. 11. Compile to SQL Server or Oracle SQL. 12. Execute parameterized, read-only, with statement timeout and row cap on a read-only account (Oracle: `SET TRANSACTION READ ONLY`; SQL Server: read-only login + `SET TRANSACTION ISOLATION LEVEL READ COMMITTED`). 13. Deterministic calculations. 14. Structured result model (tables, series, KPIs). 15. Ask the provider to interpret verified results (data passed as untrusted content). 16. Render tables, charts, narrative, report. 17. Persist to history with artifacts. 18. Authorized download, print, share.

The AI never selects connections, overrides context, or modifies data; unsupported plan shapes are rejected with a clear message and logged as a failed query.

---

## 20. Localization
English LTR with Inter; Arabic RTL with IBM Plex Sans Arabic (both OFL, self-hosted). Runtime language switcher on landing, login, organization app, organization admin, platform admin and support desk; persisted for authenticated users. Centralized resources: Transloco JSON in Angular, `.resx` in the API for validation, notifications, emails and exports. All labels, validation messages, statuses, ticket workflow, notifications, charts and report labels localized; codes stay language-neutral. AI output language: same as interface, English, or Arabic, stored in history. Exports (PDF/DOCX/XLSX) support real RTL, Arabic shaping, tables, headers and charts; sample Arabic files are generated in tests and kept as fixtures.

---

## 21. Branding and landing page
Logo: vector lighthouse + data waves + connectivity marks; SVG variants for dark/light, icon, wordmark, favicon.
Colours: Primary Navy `#0B1F3A`, Ocean Blue `#087DF2`, Aqua `#22B8CF`, Cloud White `#F5F8FC`, White `#FFFFFF`, Slate `#334155`, Border Gray `#E2E8F0`.
Dimensions: header 72–80 px, expanded sidebar ≈248 px, card radius 12–16 px, hero heading 52–60 px, page heading 26–30 px, navigation 14–15 px, body 15–16 px.
Landing page theme "From Data Chaos to Confident Decisions" with two contrasting scenes; **[Revised]** scenes are produced as in-repo vector illustrations (photorealistic licensed imagery can replace them later, review C2). Sections: header, hero, capabilities, product demonstration, use cases, how it works, security, multi-organization benefits, call to action, footer; fictional labelled demo data; EN/AR with correct direction.

---

## 22. Application UI and navigation
One design system (Angular Material M3 theme + custom components), three shells.
Organization user: Dashboard, Ask Wabi Insight, My History, My Reports, Favorites, Shared With Me, Shared By Me, Support Desk, Notifications, Profile.
Organization administrator adds: Organization Administration (dashboard), Users, Departments, Sections, Roles, Applications, Metadata, Permissions, Organization Insights, Integrations, Organization Settings, reached through a visible Administration entry.
Platform administrator shell: Platform Overview, Organizations, Platform Administrators, System Health, Support Desk, Support Access, Global Role Templates, Platform Insights Configuration, General Settings, Audit Logs.
Post-login routing: Platform Administrator → `/platform/dashboard`; Organization Administrator → `/app/dashboard` with Administration entry; Organization User → `/app/dashboard`. `MustChangePassword` forces the password screen first.

---

## 23. Persistent analysis history
`AnalysisHistories`: `HistoryId`, `OrganizationId`, `OwnerUserId`, `ApplicationId`, `Title`, `OriginalPrompt`, `PromptLanguage`, `OutputLanguage`, `CurrentVersionNumber`, `AnalysisType`, `Status`, `DataClassification` (`INTERNAL`|`CONFIDENTIAL`|`RESTRICTED`), `IsFavorite`, `IsArchived`, `IsDeleted`, `CreatedAt`, `UpdatedAt`, `RowVersion`.
`AnalysisHistoryVersions`: version number, `QueryPlanJson`, `CompiledSqlPreview` (parameters redacted), `ResultSummary`, `ResultArtifactId`, `ModelProviderId`, `ModelName`, token/cost figures, `CreatedAt`, `CompletedAt`, status. `AnalysisArtifacts` store result sets and rendered files through `IArtifactStorage` with `OrganizationId`.
Features: list, search, date and application filters, pagination, favorites, `AnalysisCollections`, archive, soft delete, re-run (new version under current permissions; history never overwritten), versions, export, print, share. Reopening never re-queries the source. Every record is tenant-owned and retrieval is authorized by ownership or valid share.

---

## 24. Sharing
In-app sharing within the same organization only (`AnalysisShares`: recipient, permission `VIEW`, expiry, revocation, `OrganizationId`); recipient must be active; sharing never bypasses table/column/row/organization rules (the shared artifact is the stored result the owner was authorized to see, and the share records that). Shared With Me / Shared By Me pages. Email delivery of reports or secure links through the configured provider with classification-aware external-disclosure approval (§B11 in review). Google Drive (OAuth, upload, `reader` permission for named recipients) and OneDrive (Microsoft Graph upload, read-only sharing links/invitations). Neither connection enables login. Cross-organization in-app sharing is disabled in v1. Records: `AnalysisShares`, `ExternalShareRecords`, `ExternalProviderConnections` (encrypted tokens, never sent to the browser), `Notifications`, `AnalysisArtifacts`.

---

## 25. Reports and analytics
Tables, KPI cards, bar/line/area/donut/comparative charts, time-series dashboards, statistical summaries, executive reports, professional email drafts. Exports PDF (QuestPDF behind `IPdfRenderer`, review C1), DOCX (Open XML SDK with `bidi`), XLSX (ClosedXML with RTL sheets); charts rendered server-side to PNG with Arabic shaping. Bilingual, directional, verified numbers only; saved reports keep organization ownership and classification.

---

## 26. Settings
**Platform settings** (`PlatformSettings`, key/value with typed accessors): product name, platform code `AP-AIR`, default language, session policy, security defaults, bootstrap status (read-only), default role templates, Platform Insights definitions, allowed AI providers, global AI limits, audit policy, default retention, supported sharing providers, support desk settings, health settings. Platform settings never grant access to organization records.
**Organization settings** (`OrganizationSettings`): profile, default language, timezone, user preference defaults, role/security configuration, data-source integrations, AI provider selection among allowed providers, report defaults, retention within platform limits, sharing preferences within platform limits, external integration configuration, `RequireSecondAdminApprovalForSupportAccess`. Platform restrictions always override.

---

## 27. Database design summary (one SQL Server database)
**Platform:** `Administrators`, `LoginIdentities`, `Organizations`, `PlatformSettings`, `SystemInitialization`, `DefaultRoleTemplates`, `PlatformMetricsDefinitions`, `AuditLogs`.
**Organization identity/security:** `OrganizationUsers`, `Departments`, `Sections`, `Roles`, `UserRoles`, `RoleTablePermissions`, `RoleColumnPermissions`, `RoleRowPermissions`, `RowScopeColumnMappings`, `OrganizationSettings`, `InvitationTokens`.
**Business data configuration:** `Applications`, `DatabaseObjects`, `DatabaseColumns`, `DatabaseRelationships`, `AIProviderConfigurations`.
**Analysis and collaboration:** `AnalysisHistories`, `AnalysisHistoryVersions`, `AnalysisArtifacts`, `AnalysisShares`, `ExternalShareRecords`, `ExternalProviderConnections`, `AnalysisCollections`, `AnalysisCollectionItems`, `Notifications`.
**Support desk:** `SupportTickets`, `TicketSequences`, `SupportTicketEvents`, `SupportTicketComments`, `SupportTicketAttachments`, `SupportAccessGrants`, `SupportSessions`.
**Usage and audit [Revised]:** `UsageEvents` (single append-only event stream, nullable `OrganizationId`), `AnalysisExecutionMetrics`, `IntegrationOperationLogs`, `EmailMessages` (outbox, draft vs sent). `insights.*` views over these for Platform Insights.
Conventions: uuid v7 keys, UTC `datetime2`, `RowVersion` on editable aggregates, organization-scoped unique constraints, composite FKs carrying `OrganizationId` where cross-tenant references are possible, indexes on every `(OrganizationId, …)` access path; migrations run under an elevated login, the runtime login has no UPDATE/DELETE on append-only tables.

---

## 28. API endpoints
Authentication: `/api/auth` (login, logout, me, change-password, accept-invitation, csrf), `/api/bootstrap`.
Platform: `/api/platform/dashboard`, `/organizations`, `/administrators`, `/settings`, `/health`, `/metrics`, `/role-templates`, `/insights-catalog`, `/audit`, `/api/platform/support/tickets…`, `/api/platform/support/access-grants`.
Organization: `/api/organization` (current organization profile, settings, dashboard), `/api/users`, `/api/departments`, `/api/sections`, `/api/roles`, `/api/applications`, `/api/metadata`, `/api/permissions`.
AI and reporting: `/api/ai`, `/api/history`, `/api/reports`, `/api/shares`, `/api/notifications`, `/api/integrations`.
Support: `/api/support/tickets`, `/api/support/tickets/{id}/events|comments|attachments`, `/api/support/access-grants`.
Typed DTOs, FluentValidation-style validation, authorization policies, pagination (`page`, `pageSize`, max 200), RFC 9457 problem-details errors, correlation ids. Tenant-facing endpoints resolve `OrganizationId` from the tenant context only; Angular guards are UX, never security.

---

## 29. Security requirements
Local authentication; separate administrator identity; mandatory `OrganizationId`; organization-scoped authorization; default deny; cross-tenant and IDOR protection (uuid v7, ownership checks); table/column/row restrictions; read-only execution; typed plan validation; parameterized SQL; encrypted connection strings, AI credentials and OAuth tokens; secure artifacts; authorized history, sharing and downloads; ticket visibility rules; explicit, time-limited support grants; append-only audit; secure error handling (no stack traces, problem details); request size limits; rate limiting on auth, bootstrap, AI and upload endpoints; antiforgery; prompt-injection safeguards (metadata and results as delimited data, allow-listed plan grammar); output sanitization (narrative rendered as text/markdown without raw HTML); security headers (CSP, HSTS, frame denial). Crucial rule: no principal of any kind uses application authorization to read another organization's private business data; support access is a narrow, audited configuration exception.

---

## 30. Automated testing
Unit: compilers (snapshot per dialect), plan validator, permission resolver, ticket state machine, grant scope checks, password/bootstrap rules. Integration (real SQL Server, two-organization fixture always): everything in the original §30 list — platform administration, organization isolation, user management, permissions, Platform Insights tenant scoping, support desk, support access, history and sharing, localization (API resources and export files). Angular: Vitest unit tests for guards, interceptors, i18n/RTL, key components; Playwright for screenshots and smoke flows. Oracle live tests gated by `WI_TEST_ORACLE_CONNECTION`. Providers (AI, email, Drive, OneDrive) are tested through fakes; live checks are manual and documented.

---

## 31. Implementation phases
See [`IMPLEMENTATION_PLAN.md`](./IMPLEMENTATION_PLAN.md). Order: 1 Foundation and identity → 2 Onboarding → 3 Platform dashboard → 4 Support desk → 5 Support access → 6 Organization data configuration → 7 Platform Insights and organization dashboard → 8 Secure AI engine → 9 Workspace and history → 10 Collaboration and integrations → 11 Landing page and polish → 12 Hardening and QA.

---

## 32. Screens and screenshots
The 42 screens listed in the original prompt (public 1–4, platform 5–15, organization administrator 16–29, organization user 30–42) are captured by the Playwright script in P11.2 in English and Arabic at desktop and mobile widths, from the running application only.

---

## 33. Deliverables
One Angular frontend; one ASP.NET Core Web API; one SQL Server configuration database with EF Core migrations; secure bootstrap; separate administrator identity; organization management and onboarding; organization users, departments, sections, organization-scoped roles with `OrganizationAdmin` and `Management`; table/column/row authorization; encrypted SQL Server and Oracle connections with adapters; built-in Platform Insights; platform and organization dashboards; support desk with stored timeline; scoped support access; AI provider abstraction; natural-language analysis; persistent history with versions; internal and external sharing; PDF/DOCX/XLSX bilingual reports; EN/AR UI with LTR/RTL; branding and landing page; tests; real screenshots; setup and deployment documentation. No real credentials or secrets in source control.

---

## 34. Non-negotiable requirements
All items of the original §34 stand, with these clarifications: the built-in Platform Insights application is one tenant-owned row per organization over a global catalog; "Completed / Resolved" is the single status `RESOLVED`; shared physical business databases are out of scope for v1; migrations are applied at deployment, not by the bootstrap request; the sample development password is rejected outside the Development environment.
