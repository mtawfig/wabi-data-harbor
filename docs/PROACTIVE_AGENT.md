# Wabi Insight — Proactive Agent (Phase 14): Monitoring Rules, Alerts, Periodic Reports

**Status:** design approved by the owner on 2026-11-07; implementation starts with P14.1.
**Specification:** [`WABI_INSIGHT_SPECIFICATION.md`](./WABI_INSIGHT_SPECIFICATION.md) §35. **Plan:** [`IMPLEMENTATION_PLAN.md`](./IMPLEMENTATION_PLAN.md) Phase 14.

Phase 14 turns Wabi Insight from an assistant that answers when asked into one that watches and acts on its own. Three features, with these names in the product (EN / AR):

| Feature | Arabic | What it does |
|---|---|---|
| **Monitoring Rules** | قواعد الرصد | Conditions the organization defines inside the tool, over its own business data and over how its users use the platform. Evaluated on a schedule or on every operation. |
| **Alerts** | التنبيهات | What a rule produces when it fires: an in-app notification, a daily digest email, an audit entry and a flag for human review. |
| **Periodic Reports** | التقارير الدورية | Saved analyses re-run on a schedule and emailed as PDF/XLSX to named users, each rendered under that recipient's own permissions. |

## 1. Owner decisions (2026-11-07)

| # | Decision |
|---|---|
| D1 | Feature names as in the table above. |
| D2 | **Human decision.** An alert informs and flags; it never suspends a user automatically. Suspending is a one-click action for the Organization Administrator from inside the alert, using the existing user status. |
| D3 | **Retention 24 months** for access facts, rule evaluations and alerts (organization setting within a platform maximum; a nightly job purges older rows). |
| D4 | **Daily digest.** Alert recipients get one digest email per day (organization time zone). High-severity alerts additionally produce an immediate in-app notification. No immediate emails in v1. |
| D5 | Rules must be **generic**: nothing in the engine knows about payroll, customers or budgets. A rule binds to whatever tables and columns the organization has catalogued; a rule template is offered only when the fields it needs exist in that organization's metadata. |
| D6 | Periodic Reports are created for others by the Organization Administrator; **Management** users may create them for themselves without the administrator. Other users only receive. |

## 2. Everything is generic: rules bind to the organization's own metadata

The engine never ships business logic. A rule is a typed document (like the query plan of §19) whose every reference is an `ObjectId` / `ColumnId` from `DatabaseObjects` / `DatabaseColumns` of the organization, or a platform usage signal. Three mechanisms make rules portable across businesses:

1. **Column roles** (new metadata field `DatabaseColumns.SemanticRole`, optional, set by the Organization Administrator, auto-suggested from column names and types): `Identifier`, `PersonIdentifier`, `AccountIdentifier`, `DeviceIdentifier`, `Timestamp`, `Amount`, `Quantity`, `Status`, `Location`, `Contact`, `Other`. Roles are stable codes with EN/AR labels. They are *hints* for templates and the planner, never permissions.
2. **Rule templates with required field roles.** A template declares the roles it needs (for example *AccountIdentifier + DeviceIdentifier + Timestamp on one table*). The template catalogue shows, per organization, only the templates whose roles can be satisfied by approved, permitted columns, and lets the administrator pick which table and columns to bind. If the organization's database has no device column, the "same account from multiple devices" template is simply not offered.
3. **Validation against metadata at save time and at every evaluation.** A rule whose object or column is no longer present (`IsPresent = 0`) or approved is paused with an alert of type `RuleInvalid` instead of silently running.

### Example: the owner's "same account used from multiple devices in one day"

Available when a table has columns with roles `AccountIdentifier`, `DeviceIdentifier` and `Timestamp` (for example `Logins(AccountId, DeviceId, LoginAt)` in a banking or e-commerce source):

```json
{
  "kind": "data",
  "name": "Same account used from multiple devices in one day",
  "schedule": { "type": "daily", "time": "06:00" },
  "query": {
    "source": { "objectId": "…Logins…" },
    "window": { "column": "…LoginAt…", "lastDays": 1 },
    "groupBy": ["…AccountId…"],
    "measure": { "aggregate": "count_distinct", "column": "…DeviceId…" }
  },
  "condition": { "operator": "gt", "value": 1 },
  "severity": "High",
  "recipients": { "roles": ["…CyberSecurity role…"], "users": [] },
  "delivery": { "digest": true, "immediateInApp": true },
  "output": { "includeGroupKeys": true, "maxRows": 200 }
}
```

The daily digest to the Cyber Security team lists the accounts (group keys) and the distinct device counts; raw rows are never attached. The same template works for *same card used in two cities within an hour*, *same badge at two gates* or *same patient file opened by many users*: only the bound columns change.

## 3. Rule kinds

| Kind | Evaluated over | Examples (all templates, all bound to the organization's own columns) |
|---|---|---|
| **Data** | The organization's business data through the existing secure path: typed rule → validation → authorization → dialect compiler → parameterized read-only SQL, executed under the **rule owner's** permissions (table, column and row scope). Supports threshold, change against a comparison window (same period last week/month), and *pattern* queries (group-by with a measure). | Same account on multiple devices in a day; restricted-column value accessed outside working hours (when the source logs it); delays above 20%; sales down 15% versus last week; payments to a supplier above its average; stock below reorder level for more than N days. |
| **Access behaviour** | Platform usage facts (§4): who queried which objects and columns, with which filters, how many rows, which exports. Compared to fixed limits or to **baselines** (the user's own 28-day pattern and the pattern of users with the same role). | A user repeatedly querying tables tagged with a classification or role; filtered access to `Restricted` columns by identity values (specific customers' accounts); exports above baseline; first-week users touching sensitive data; queries outside working hours; repeated unauthorized attempts. |
| **System** | Platform signals of the organization | Source connection test failures; AI spend above the monthly budget; email delivery failures; metadata drift (objects removed). |

Every data rule and access rule carries: scope, signal, condition, schedule, severity (`Info`, `Medium`, `High`), recipients (users and/or roles of the organization), delivery (digest and, for High, immediate in-app), cooldown (no second alert for the same rule and group key within N hours), and an active flag.

## 4. Data-access facts (the ledger behaviour rules read)

Today each analysis stores its `QueryPlanJson`; evaluating behaviour rules from JSON would be slow and fragile. P14.1 adds an append-only ledger written in the same transaction as the analysis version:

`AnalysisAccessFacts` — `FactId`, `OrganizationId`, `UserId`, `RoleCodesSnapshot`, `DepartmentId`, `ApplicationId`, `HistoryVersionId`, `ObjectId`, `AccessedAt`, `RowCount`, `HighestClassification`, `ColumnIdsJson` (selected and filtered), `FilterColumnIdsJson`, `FilterValueCount` (number of literal values in filters on identifier-role columns), `IsExport`, `ExportFormat`, `Outcome` (`Succeeded`, `Rejected`, `Unauthorized`).

Rejected and unauthorized plans are recorded too (object references the user was not allowed to use), because repeated attempts are a signal. Facts are tenant-owned, never editable, purged by the retention job (D3). Baselines (`AccessBaselines`: per user and per role, rolling 28 days, per day: query count, row count, restricted-column touches, export count; median and p95) are recomputed nightly.

## 5. Alerts

`Alerts` — `AlertId`, `OrganizationId`, `RuleId`, `Severity`, `Status` (`Open`, `Acknowledged`, `Resolved`, `Dismissed`), `GroupKey` (for example the account id or the user id the alert is about), `SubjectUserId` (behaviour alerts), `Title`, `SummaryJson` (figures, group keys, comparison), `FirstSeenAt`, `LastSeenAt`, `Occurrences`, `AcknowledgedBy/At`, `ResolvedBy/At`, `Note`. Append-only `RuleEvaluations` record every run (rule, started/finished, rows examined, fired or not, error).

Delivery (D4):
- **Immediate in-app notification** to each recipient for `High`, through the existing `Notifications`.
- **Daily digest email** per recipient at the organization's digest time (default 07:00 local), grouped by severity and rule, through the existing `EmailMessages` outbox. The digest includes only alerts whose rule objects the recipient is permitted to read; others appear as title and count only. Nothing in an email is raw business data beyond the configured group keys and aggregates.
- **Audit**: every alert and every status change is written to `AuditLogs`.
- **Human action (D2)** from the alert: acknowledge, resolve with a note, dismiss, open the subject user, **suspend user** (existing `OrganizationUsers.Status`, confirmation dialog, audited). No rule action can suspend.

## 6. Periodic Reports

`ReportSubscriptions` — `SubscriptionId`, `OrganizationId`, `HistoryId` (a saved analysis), `CreatedByUserId`, `Name`, `Schedule` (`Daily`/`Weekly`/`Monthly`, time, weekday or day-of-month, organization time zone), `Format` (`Pdf`, `Xlsx`), `Language` (`en`/`ar` or recipient preference), `IsActive`, `PausedReason`, `NextRunAt`, `LastRunAt`. `ReportSubscriptionRecipients` — recipient users of the same organization. `ReportDeliveries` — one row per recipient per run: status, artifact id, email message id, error.

Execution: at `NextRunAt` the agent re-runs the analysis once **per recipient under that recipient's permissions** (the existing re-run path of §23: same prompt and plan, current permissions, new version owned by the recipient's delivery, never overwriting the owner's history), renders it with the existing exporters, stores the artifact through `IArtifactStorage` and queues the email with the attachment and a link to open it in the app. A recipient without permission to the underlying objects receives a short "no data available for your permissions" message, never someone else's figures. Three consecutive failures pause the subscription and alert its creator (`System` rule).

Permissions (D6): the Organization Administrator creates, edits, pauses and deletes any subscription of the organization and chooses recipients among active users; a `Management` user creates subscriptions for **themselves only**; other users only receive, and can see "Periodic reports sent to me" with the delivered files in My Reports.

## 7. Permissions summary

| Role | Monitoring Rules | Alerts | Periodic Reports |
|---|---|---|---|
| OrganizationAdmin | Create/edit all rules; enable templates; set column roles; set recipients | Alert centre for all alerts; acknowledge/resolve/dismiss; suspend user | Create for any users; manage all |
| Management | Data rules for themselves (recipients: themselves) | Own alerts | Create for themselves |
| Other organization users | — | Receive alerts only when named as recipients by the administrator (for example a Cyber Security role) | Receive |
| Platform Administrator | Never sees rules, alerts or subscriptions of an organization; Platform Insights gets counts only (rules active, alerts per severity, deliveries) | — | — |

Rules evaluate under their **owner's** permissions and never widen them; recipients see details only for objects they may read in full (the object, its columns and every row; a recipient limited to their department, section or own records sees the title only, unless they own the rule). Everything is tenant-filtered and ownership-checked (architectural invariants unchanged).

## 8. Natural-language rule authoring

A new structured output schema `wabi_monitoring_rule` lets a user describe a rule in Arabic or English ("alert the security team when an account signs in from more than one device in a day"). The planner receives the same authorized metadata snapshot as §19 plus the column roles, returns a candidate rule, which is **shown to the user for confirmation** (bound table and columns, condition, schedule, recipients) and then goes through the same validation and authorization as every plan. Nothing the model produces is executed or saved without that confirmation. Templates remain the no-AI path.

## 9. Scheduler

One `BackgroundService` in the API (like `EmailOutboxWorker`), ticking every minute: it takes due rows from `ScheduledJobs` (`JobId`, `JobType`, `OrganizationId`, `TargetId`, `NextRunAt`, `LeaseOwner`, `LeaseUntil`, `LastRunAt`, `LastOutcome`) with an atomic lease, so several API instances never run the same job twice. Missed runs (the API was down) run once on start, not once per missed slot. Organization time zone and the UTC `datetime2` convention apply. Rule evaluations of kind *access* on *every operation* are not scheduled: they run inline when a fact is written, with the cooldown preventing floods.

## 10. Data model additions (one SQL Server database)

`MonitoringRules`, `MonitoringRuleRecipients`, `RuleEvaluations` (append-only), `Alerts`, `AlertEvents` (append-only), `AnalysisAccessFacts` (append-only), `AccessBaselines`, `ReportSubscriptions`, `ReportSubscriptionRecipients`, `ReportDeliveries`, `ScheduledJobs`; new column `DatabaseColumns.SemanticRole`; organization settings `Monitoring:RetentionMonths` (default 24, platform maximum), `Monitoring:DigestTime`, `Monitoring:WorkingHours`. Conventions as §27 (uuid v7, UTC, `RowVersion`, `(OrganizationId, …)` indexes, runtime login without UPDATE/DELETE on append-only tables).

## 11. API additions

`/api/monitoring/rules` (CRUD, test-run, pause/resume), `/api/monitoring/templates` (catalogue filtered by the organization's metadata), `/api/monitoring/rules/draft` (natural-language draft), `/api/monitoring/alerts` (list, detail, acknowledge, resolve, dismiss, suspend-user), `/api/monitoring/digest-preview`, `/api/metadata/columns/{id}/role`, `/api/subscriptions` (CRUD, run-now, deliveries), `/api/subscriptions/mine`, platform counts in `/api/platform/insights`.

## 12. Screens (EN/AR, LTR/RTL)

Organization Administration → **Monitoring Rules** (list, template catalogue with "available for your data" filter, rule editor with bound-field pickers, test run with sample result), **Alerts** (alert centre: filters by severity/status/rule/user, detail drawer with timeline and actions), **Periodic Reports** (subscriptions, recipients, delivery log). Management → **My Rules**, **My Periodic Reports**. All users → alerts they were named on in the existing notification centre; delivered reports in My Reports. Metadata → column role picker. Platform → counts on System Health / Insights.

## 13. Security, privacy, auditing

- Rules and subscriptions are tenant-owned; evaluation and rendering run under the owner's or recipient's permissions respectively; no path widens access.
- Alert and digest content: aggregates and configured group keys only; no raw rows in emails; recipients without permission get titles only.
- Behaviour monitoring observes platform usage the organization already audits (`AuditLogs`, `UsageEvents`); facts are derived from those operations, retained 24 months (D3), visible to the Organization Administrator only, and never to the Platform Administrator.
- Suspension is human-only (D2) and audited; alerts cannot modify users or data.
- Natural-language drafts follow the §19 injection safeguards; no model output runs unconfirmed.
- New usage events: `Monitoring.RuleEvaluated`, `Monitoring.AlertRaised`, `Monitoring.DigestSent`, `Monitoring.RuleDrafted`, `Reports.SubscriptionDelivered`, `Reports.SubscriptionFailed`.

## 14. Delivery plan

| PR | Scope | Acceptance |
|---|---|---|
| **P14.1 — Access ledger and scheduler** | `AnalysisAccessFacts` written with every analysis (including rejected plans), `AccessBaselines` nightly job, `ScheduledJobs` with leases, retention job, `DatabaseColumns.SemanticRole` + role suggestions, migrations, usage events. | Facts appear for every analysis in two organizations and never cross; baseline job and purge job run under lease; two API instances share the schedule without duplicates. |
| **P14.2 — Monitoring Rules and Alerts (data + access kinds)** | Typed rule document, validation and authorization, data-rule compiler on the dialect compilers, access-rule evaluator (limits and baselines), template catalogue filtered by metadata (incl. "same account, multiple devices", "restricted filtered access", "export above baseline", "outside working hours", "threshold", "change versus period"), `Alerts`/`AlertEvents`, cooldown, immediate in-app for High, daily digest email, human actions incl. suspend, API, Angular pages (rules, templates, editor, test run, alert centre), EN/AR, Platform counts. | Owner's example fires on sample data; a rule can never read beyond its owner; recipients without permission see titles only; digest arrives once per day per recipient; suspension requires confirmation and is audited; Platform Administrator cannot read any alert. |
| **P14.3 — Periodic Reports** | Subscriptions, per-recipient execution under the recipient's permissions, PDF/XLSX rendering, outbox delivery with attachment and link, delivery log, pause after three failures, admin pages and Management self-service, "sent to me" in My Reports. | Two recipients of one subscription receive different figures according to their row scopes; a recipient without permission gets the "no data" message; Management can only subscribe themselves. |
| **P14.4 — Natural-language rules and system rules** | `wabi_monitoring_rule` schema and prompt, draft → confirm → save flow, system rules (connection failures, AI budget, email failures, metadata drift), rule-health alerts (`RuleInvalid`), screenshots and docs. | A rule described in Arabic binds to the right columns and needs confirmation; an invalid draft is rejected with a clear message; removing a column pauses dependent rules with an alert. |

Each PR updates `PROGRESS.md`, keeps the full test matrix green (two organizations in every multi-tenant test), and adds screens to `SCREENSHOTS.md`.

## 15. Implementation notes (P14.2)

What shipped, and where it differs from the design above:

- **Access rules are scheduled, not inline.** §9 planned inline evaluation for access rules "on every operation". P14.2 evaluates them on a schedule like data rules (interval of at least 15 minutes, daily or weekly), which keeps the analysis path fast and makes the cooldown simpler; the templates that need speed run hourly.
- **Rule evaluations are not ledger facts.** A data rule reads data as its owner, but its run is recorded in `RuleEvaluations`, not in `AnalysisAccessFacts`, so automated runs do not distort the owner's behaviour baseline.
- **Rule health.** A rule whose owner is no longer active, or whose object, columns or the owner's permissions no longer hold, is paused and raises a rule-health alert (`GroupKey = rule_invalid`) to its owner and author. An application that is not (yet) approved fails the run without pausing the rule (an administrator's pending step). Resuming re-validates.
- **Templates in v1:** data — same account, several devices; many events for one record; amount above a threshold; change versus the previous period. Access — targeted look-ups on restricted data; repeated unauthorized attempts; exports above the usual; rows read above the usual; sensitive data outside working hours; new user reading restricted data. Custom data definitions are accepted by the API with the same validation; the editor works from templates (natural-language drafts follow in P14.4).
- **Recipients.** The owner always receives the rule's alerts; administrators add users and roles. Management rules go to their owner only.
- **API.** `/api/monitoring/templates`, `/owners`, `/rules` (CRUD, `pause`, `resume`, `test`, draft `rules/test`), `/settings`, `/alerts` (list, detail, `acknowledge`, `resolve`, `dismiss`, `reopen`, `suspend-user`, `digest-preview`), `/api/platform/monitoring/summary`. Field roles stay on the existing column update (`PUT /api/metadata/columns/{id}`).
- **Sample.** `HumanResourcesDB.EmployeePortalSignIns` and the view `vw_EmployeePortalSignInsLive` (last day = today in Asia/Dubai) let the owner's example fire on the sample organization; see `scripts/sql/samples/README.md`.

### P14.3 (Periodic Reports)

- **Stored plan, not a new AI call.** §6 speaks of the §23 re-run path; P14.3 re-executes the analysis's latest completed plan for each recipient after re-validating and re-authorizing it for that recipient (`AuthorizeStoredAsync`). This gives the same queries at no model cost, deterministically. The owner's narrative and written study are excluded from delivered files, because they describe the owner's figures.
- **Which analyses.** The author may schedule their own analyses or ones shared with them (P10.1 shares), which records the owner's consent.
- **Failures.** Three failed scheduled runs pause the subscription and send an in-app notification to its creator. The system-rule alert follows with the system rules in P14.4.
- **Deliveries are not ledger facts.** The delivery log records who received what, when and how many rows. Behaviour baselines stay based on interactive use.

### P14.4 (Natural-language rules and system rules)

- **Drafts are data rules only.** A description becomes one data rule on one catalogued object (the shape of §5). Anything that needs several tables, how people use the platform, or system health is answered with `draft_not_expressible` and the user is pointed to the templates. Access rules stay template-based.
- **The model sees the owner's catalogue, returns names.** The prompt lists only what the rule owner may read (approved, present, permitted columns; up to 60 objects ordered by how many field roles they carry, 60 columns each), with display names in the description's language, all fenced and sanitized like the §19 planner. Names are bound server-side to catalogue ids; an unknown or unpermitted name rejects the draft. The bound draft then goes through the same validation and owner authorization as a saved rule before it is shown.
- **Confirmation is the save.** The draft endpoint never writes a rule. The page shows the bindings in words; the user saves through the normal endpoint (and may adjust window and condition; the columns are fixed so the confirmed bindings are what runs). An administrator drafting for another owner picks the owner first; the draft is bound to that owner's permissions.
- **System rules.** Kind `System`, administrators only, five signals: `connection_failures` (failed executions at the execute stage plus a failed connection test, per application), `email_failures` (the organization's outbox), `ai_budget` (month-to-date estimated cost of analyses in the organization's time zone against `Organizations.MonthlyAiBudget`, threshold in percent; skipped while no budget is set), `metadata_drift` (objects and columns removed in the window, per object) and `report_failures` (failed periodic-report deliveries, per report). They count the organization's own platform events; no business data is read, so the alert is never redacted.
- **Rule health on metadata change.** Besides the pause at run time (P14.2), `RuleHealthChecker` checks the organization's active data rules immediately after every catalogue change (refresh, object or column update, approval) and pauses those whose object or referenced columns are missing, removed or unapproved, with the same `rule_invalid` alert and notification. Permission changes are still caught at the next run.

## 16. Why this matters

- **Platform:** daily, unattended value (alerts and reports arrive without anyone asking) and a governance layer most analytics tools lack: the platform that grants data access also watches how that access is used.
- **Competition (Create AI Agents Championship, Best Business AI Agent):** matches the agent definition (multi-step plans, tools, integration with the organization's systems, autonomous work toward an outcome), turns "protect data and prevent leakage" into a product feature, and gives measurable value (incidents detected, reports delivered, analyst hours saved). The live demo on the sample databases: an analyst queries colleagues' payslips; the administrator's alert appears in front of the jury. Recorded in P14.5 (`docs/demo/p14-5-proactive-agent-alert.gif`, script and story in `scripts/demo/proactive-agent/README.md`): the analyst's `SELF` row scope shows him none of his colleagues' pay, the look-up is still in the ledger, and the access rule on identifier look-ups raises the alert that the administrator acknowledges.
- **Target audience:** CIOs, CISOs, compliance and data-protection officers (UAE PDPL alignment), senior management (KPIs in the inbox), government entities (complete audit trail).
