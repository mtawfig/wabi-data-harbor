# Wabi Insight — Security

Status: P1.3 (authentication, sessions, bootstrap, tenant context). Later phases extend each section.

## Account model
- Two stores, one login screen. `Administrators` (platform company, no `OrganizationId`) and `OrganizationUsers` (required `OrganizationId`). `LoginIdentities` (primary key `NormalizedEmail`) resolves the account category and makes a login email unique across both stores.
- Emails are normalized (trimmed, upper-invariant) before lookup; the original casing is kept for display.
- Passwords are hashed with ASP.NET Core Identity's `PasswordHasher` (PBKDF2-HMAC-SHA512, per-password salt, v3 format). Hashes are re-hashed transparently on login when the algorithm parameters change. No custom cryptography.

## Password policy
Length only (NIST SP 800-63B): 12–128 characters, not equal to the login email, not the known sample password. The sample password `my_default_password_change_it` is accepted **only** when the host environment is `Development` and only for bootstrap; every other environment rejects it, and the change-password endpoint rejects it everywhere.

## Login behaviour
| Case | Response |
|---|---|
| Unknown email or wrong password | 401, identical problem type `invalid_credentials`; a dummy hash verification equalizes timing for unknown emails |
| 5 failed attempts (configurable) | account locked for 15 minutes (configurable); further attempts return `locked_out` even with the right password |
| Inactive account | `account_disabled` |
| User of a suspended/deactivated organization | `organization_suspended` |
| More than 10 login attempts per minute per client (configurable) | 429 |

Every outcome is written to `AuditLogs` (`Auth.LoginSucceeded`, `Auth.LoginFailed`, `Auth.LoginDenied`, `Auth.LockedOut`) without the password.

## Sessions
- Cookie `wi.session`: `HttpOnly`, `SameSite=Lax`, `Secure` when the request is HTTPS, not persistent. Sliding idle timeout 30 minutes and absolute lifetime 12 hours (both platform-configurable); 401/403 are returned as JSON, never redirects.
- The cookie carries only identifiers and the security stamp. The security stamp is re-validated against the database at most every 5 minutes (configurable, 0 = every request): a changed password, an administrator reset, deactivation, lockout or organization suspension ends the session on the next validation.
- `change-password` rotates the security stamp (invalidating every other session) and re-issues the current session.
- `must_change_password` sessions can reach only `me`, `csrf`, `logout` and `change-password`; everything else returns 403 `password-change-required`.

## Antiforgery (CSRF)
Double-submit pattern: `GET /api/auth/csrf` issues the `wi.xsrf` cookie and returns the request token; every unsafe request (POST/PUT/PATCH/DELETE) must send it in `X-XSRF-TOKEN`. Tokens are bound to the identity, so clients fetch a new one after login. Validation is default-on for all controllers; `logout` and `csrf` opt out explicitly. Failures return 400 `antiforgery`.

## Organization roles
`Roles` and `UserRoles` are organization-scoped with composite foreign keys that make cross-organization assignments impossible at the database level. Role codes travel in the session as `role` claims, refreshed with the security stamp, and feed the `OrganizationAdmin` policy. Organization onboarding is the one path where a Platform Administrator creates an organization user (the initial administrator); its temporary password is emailed and the account must change it at first sign-in.

## Organization structure and user management
Departments, sections and users are tenant-filtered and additionally bound by composite foreign keys (`OrganizationId` travels with every department, section and role reference), so a mismatched section/department or a cross-organization role assignment fails in the database even if application code regressed. Organization Administrators cannot deactivate their own account, remove their own administrator role, or leave the organization without an active administrator. New users and resets go through the same emailed temporary-password flow as administrators.

## Authorization
- Fallback policy: every endpoint requires an authenticated session unless it carries `[AllowAnonymous]` (login, csrf, bootstrap, platform info, health). Unknown routes return 401 to anonymous callers.
- Policies: `PlatformAdministrator` (principal type claim), `OrganizationUser` (principal type + organization claim), `OrganizationAdmin` (organization user with the `OrganizationAdmin` role claim).
- Tenant context: `TenantContextMiddleware` rebuilds the trusted `TenantPrincipal` from the cookie after authentication. A platform administrator carrying an organization claim, or an organization user without one, is treated as unauthenticated.

## Bootstrap (first Platform Administrator)
- Configuration `Bootstrap:*` is read from environment variables, user secrets or a secret store. Only the Development sample file commits sample values.
- Two entry points share `BootstrapService`: the `bootstrap` CLI command (uses the configured secret) and `POST /api/bootstrap/initialize` (requires the secret in the body, rate-limited to 3/min per client). `GET /api/bootstrap/status` reveals only completion state, date, the bootstrap email, schema version and whether migrations are pending.
- Preconditions: bootstrap enabled, a secret of at least 16 characters, plausible email, policy-compliant password, schema at the latest migration, no completed `SystemInitialization` row and no existing administrator.
- Effects, in one transaction: administrator with `IsBootstrapAdministrator = true` and `MustChangePassword = true`, login identity, singleton initialization row, `Bootstrap.Completed` audit entry. The singleton check constraint and the filtered unique index make concurrent attempts fail atomically; the loser receives 409.
- Migrations are never applied by the bootstrap request. Use the `migrate` command at deployment time.

## Account provisioning and temporary passwords
`POST /api/platform/administrators` (create) and `POST /api/platform/administrators/{id}/reset-password` are restricted to platform administrators. Both generate a cryptographically random 20-character temporary password, store only its hash, and **email** it to the account owner (bilingual template, RTL for Arabic). The API response carries the masked recipient address and the outbox message id, never the password; the administrator who triggered the action does not see it. The target account gets a new security stamp (existing sessions end), `MustChangePassword = true` and cleared lockout; the first sign-in is forced through change-password. Self-reset is refused; use change-password.

## Email delivery
Settings live under `Email` (`Provider` = `Smtp` | `File` | `None`). SMTP uses MailKit with STARTTLS on port 587 (implicit TLS on 465); plain connections are refused when `EnableSsl` is true. Messages go through an outbox: the `EmailMessages` row stores recipient, subject, template code, language, status (`Queued`, `Sent`, `Failed`), attempts, last error and the requesting principal, and the body is kept only in memory until the background worker delivers it (bounded retries, configurable delay). Credentials therefore never reach the database or the logs. The committed SMTP password is a placeholder; supply the real one through `appsettings.Local.json` or the deployment configuration. `DkimSelector` is accepted for future DKIM signing but not applied in v1.

## Configuration database connection string
By owner decision G (see `docs/SPEC_REVIEW.md`), the connection string is committed in `appsettings.json` for this private repository. Treat repository read access as database access.

## Platform operations data
Platform Administrators read aggregates (`/api/platform/dashboard`), health (`/api/platform/health`) and the audit trail (`/api/platform/audit`). The dashboard counts applications but never returns their names or connection details; audit details never contain secrets or business data; unhandled exceptions are recorded as `Api.Error` usage events with the exception type only, never its message. `UsageEvents` and `AuditLogs` are both append-only.

## Support desk
Ticket visibility is decided from the tenant context only: a requester sees their own tickets, an `OrganizationAdmin` every ticket of their organization, a Platform Administrator the platform queue. A ticket of another organization is a 404, even by id. Status changes follow the fixed transition table (`TicketStateMachine`); anything else is rejected with a stable code. Attachments: 10 MB, extension allow-list (`pdf, png, jpg, jpeg, xlsx, docx, csv, txt, log, zip`), SHA-256 recorded, scanner hook (`IAttachmentScanner`), stored outside the web root under random keys and served only through the authorized download endpoint with `nosniff`; Platform Administrator downloads are audited (review C6). `SupportTicketEvents` is append-only. Notifications are readable and markable only by their recipient.

## Support access (scoped administrative assistance)
A Platform Administrator never changes an organization's configuration without an approved, unexpired, narrowly scoped `SupportAccessGrant`; a ticket alone grants nothing. Grants list typed operations with specific target ids (validated to belong to the ticket's organization); there is no global grant and no business-data operation. Approval requires an active `OrganizationAdmin` of that organization; `RequireSecondAdminApprovalForSupportAccess` forces a different administrator than the requester. Execution requires an explicit support-mode session of the assigned administrator (`SupportMode` policy) and passes `SupportAccessAuthorizer` per call: assignment, expiry (default 4 h, max 72 h), operation and target scope. Denials are audited (`SupportAccess.OperationDenied`). Any `OrganizationAdmin` of the organization or any Platform Administrator can revoke immediately, which ends the session. Every executed operation is written to the ticket timeline, `AuditLogs` and `UsageEvents` with administrator, organization, ticket, grant, session, operation, target and outcome; temporary passwords are emailed to the user and never appear in results or details.

## Application connections and metadata
Connection strings are encrypted with ASP.NET Core Data Protection (purpose-specific protector, file-system key ring outside the configuration database, automatic rotation with old keys retained) and are never returned by any endpoint; the API shows "configured" plus the hint typed by the administrator. A salted HMAC fingerprint makes a source unique across the platform, so two organizations can never register the same database (A7). Adapters open sources with read-only intent, read catalog views only and never execute anything but fixed discovery queries; the connection test reports whether the account is read-only and warns otherwise. Metadata is per organization through the tenant filter; Platform Administrators reach it only through an approved `RefreshMetadata` or `DiagnoseApplicationConnection` grant, and have no application endpoints. Business descriptions are user content (length-limited) and will be passed to the LLM as delimited data, never as instructions.

## Permissions (P6.2)
Business data access is decided only by the resolver: organization boundary first, then approved applications, approved objects and columns, then the union of the user's active roles. `Management` is automatic (all approved objects, all rows) and cannot be edited; `OrganizationAdmin` manages configuration and grants no data by itself. Row scopes `Department`, `Section` and `Self` require a mapped column on the object; without it the user sees the columns but no rows, never all rows. Grants are written only by Organization Administrators of that organization or, under an approved support grant, through the `UpdateTablePermissions` operation targeting a listed role; every change is audited (`Permission.Changed`, `Permission.RowMappingChanged`). Platform Administrators have no permission endpoints and see object names only (never rows) when proposing the operation.

## Audit
`AuditLogs` is append-only (application guard plus `scripts/sql/create-runtime-login.sql`). Records carry actor type/id, event code, entity, JSON details without secrets, correlation id and client IP.

## Not yet implemented (later phases)
Organization-user password reset by Organization Administrators (P2.2), role claims (P2.2), support-access UI and support-mode banner (P5.2), a real virus scanner behind `IAttachmentScanner`, request body size limits beyond uploads and security headers review (P12.1).
