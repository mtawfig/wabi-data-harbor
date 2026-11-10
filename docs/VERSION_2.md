# Wabi Insight — Version 2: scope and requirements

| | |
|---|---|
| **Status** | Draft for the owner's approval. Nothing in this document is built yet. |
| **Date** | 2026-11-10 |
| **Applies after** | Version 1 (phases 1–14 of [`IMPLEMENTATION_PLAN.md`](./IMPLEMENTATION_PLAN.md)), feature-complete and merged |
| **Decided by** | The owner. Each item starts only after the owner approves it, one pull request at a time. |

## 1. Purpose

This document lists what version 2 of Wabi Insight should contain, why, and in what order. It has two kinds of items:

- **Owner decisions:** work the owner moved to the next version during version 1. These are part of version 2.
- **Proposed:** additions that fit the product and are suggested here. The owner accepts, changes or drops each one.

**Fixes are not version 2 (owner decision, 2026-11-10).** The first version of this document also carried the 23 defects and gaps that the code and documents review of 2026-11-10 found and that were not among the eight fixed in P13.23 and P13.24. They belong to version 1 and are fixed there, in pull requests P13.25–P13.30: see [`IMPLEMENTATION_PLAN.md`, Phase 13](./IMPLEMENTATION_PLAN.md). Those pull requests keep the identifiers the items had here (A2, A3, B2, B6, B7, C2–C10, E1–E3, E7, F1–F5), so the identifiers below are not consecutive.

It is a requirements document, not a design. Each accepted item gets its design (data, API, interface, tests) in the pull request that starts it, as version 1 did. The detailed design that already exists for one item, *Sign in with Microsoft*, stays in [`IMPLEMENTATION_PLAN.md`, *Version 2*](./IMPLEMENTATION_PLAN.md#version-2-planned-not-started).

## 2. Where version 1 stands

Version 1 lets the users of several organizations ask business questions in English or Arabic about their organization's **SQL Server and Oracle** databases, and get verified figures, charts, tables, written explanations, studies (PDF, Word, Excel), sharing, history, Monitoring Rules, Alerts and Periodic Reports, under table, column and row permissions. It is deployed with IIS, with nginx on Linux, with Docker Compose, or on Azure App Service, from GitHub with one click.

What version 1 does not do, and version 2 addresses:

- It reads **databases only**. Data that an organization can reach only through an **API** (a cloud HR system, an accounting service, a ticketing tool) can't be asked about.
- A chart is a picture of one level. A user can't click a branch to see its tellers.
- People sign in with an e-mail address and a password kept by Wabi Insight.
- Reports are downloaded or e-mailed, not published to Google Drive or OneDrive.

## 3. What does not change

The architectural invariants of version 1 (see [`CLAUDE.md`](../CLAUDE.md) and specification §34) hold in version 2. In particular:

- **Three logical layers:** one Angular app, one ASP.NET Core API, and data and integrations. No microservices, message brokers or separate applications. API data sources are a new kind of integration in the third layer, not a new service.
- **Organization isolation:** `OrganizationId` always comes from the signed-in user; every tenant-owned query is filtered and ownership-checked; Platform Administrators never see business data.
- **The model never writes what is executed.** In version 1 the model produces a typed plan and the server compiles it to read-only SQL. Version 2 extends the same rule to APIs: the model produces the same kind of plan, and the server turns it into requests to endpoints the organization registered. **The model never supplies a URL, a path, a header or a request body.**
- **Permissions:** table, column and row scopes (`ALL`, `DEPARTMENT`, `SECTION`, `SELF`) apply to every data source, of any kind.
- **Business users never see technical details** (queries, endpoints, row counts, tokens, cost).
- **The product stays generic:** no logic specific to one business domain.
- **Secrets** are encrypted, never returned to the browser, never written to logs.
- Everything is bilingual (English and Arabic, right-to-left), tested with at least two organizations, and shown with real screenshots.

## 4. Scope at a glance

**Source:** *Owner* = decided by the owner (date); *Proposed* = suggested in this document.
**Priority** (a proposal for the owner): **Must** = version 2 is not version 2 without it; **Should** = important, planned; **Could** = only if time allows.
**Size:** S = about a day, M = several days, L = a week or more (one or more pull requests).

### A. Data sources

| ID | Item | Source | Priority | Size |
|---|---|---|---|---|
| A1 | **API data sources:** ask questions about data that is reached through an API, not only SQL Server and Oracle databases | Owner, 2026-11-10 | Must | L |
| A4 | More database types: PostgreSQL, MySQL / MariaDB | Proposed | Could | M each |
| A5 | Files as a source: an uploaded Excel or CSV file becomes a table that can be asked about | Proposed | Could | M |

### B. Analysis experience

| ID | Item | Source | Priority | Size |
|---|---|---|---|---|
| B1 | **Drill-down in charts:** click a bar or slice to open the next level (branch → tellers of that branch) | Owner, 2026-11-09 | Must | L |
| B3 | Follow-up questions that refine the last answer ("now only for Dubai", "by month instead") | Proposed | Should | M |
| B4 | Results larger than 1,000 rows: a full export to Excel or CSV prepared in the background | Proposed | Could | M |
| B5 | Saved dashboards: pin panels of answers to a personal or role dashboard that refreshes | Proposed | Could | L |

### C. Identity and access

| ID | Item | Source | Priority | Size |
|---|---|---|---|---|
| C1 | **Sign in with Microsoft** (Microsoft Entra ID), with two-step sign-in done by Microsoft | Owner, 2026-11-08 | Must | L |

### D. Sharing and publishing

| ID | Item | Source | Priority | Size |
|---|---|---|---|---|
| D1 | **Publish reports to Google Drive and OneDrive** | Owner, 2026-11-06 | Must | L |
| D2 | Alerts and Periodic Reports to more channels than e-mail (Microsoft Teams, a webhook) | Proposed | Could | M |

### E. Reliability and operations

| ID | Item | Source | Priority | Size |
|---|---|---|---|---|
| E4 | E-mails survive a restart (the message is kept, encrypted, until it is sent) | Proposed | Could | M |
| E5 | Running more than one copy of the API (shared request limits, e-mail queue, scheduler) | Proposed | Could | L |
| E6 | Docker deployment on Azure, next to the existing Deploy (Azure) | Proposed | Could | M |

## 5. Requirements in detail

Only the items that need more than their line in the tables are described here.

### A1. API data sources (owner decision, 2026-11-10)

**Goal.** An organization registers an API as a data source, next to its databases. Its users then ask questions about that data in the same way, and get the same answers: figures, charts, tables, explanations, studies, alerts and scheduled reports.

**What the Organization Administrator registers.**
- A new kind of application, *API*, beside *SQL Server* and *Oracle*.
- **Connection:** the base address (HTTPS), and how to sign in: an API key in a header, a user name and password, or OAuth 2.0 client credentials. Secrets are encrypted like connection strings, never shown again, and tested with **Test connection**.
- **Endpoints, as tables.** Each endpoint that returns a list becomes one *object* in the catalogue, and each field of its items one *column*, with English and Arabic names, descriptions, data types and classifications, exactly as for database tables. The list of endpoints and fields is read from the API's description (OpenAPI) where one exists, or from a sample answer, and is then reviewed and approved by the administrator. Nothing is queryable until it is approved.
- **Per endpoint:** which request parameters filter the data on the API's side and which field each one filters, how paging works, and the field that holds the row owner's employee, department or section code (for row scopes).

**How a question is answered.**
1. The model receives the catalogue (objects, columns, descriptions), as today, and returns the same typed plan. It doesn't know whether an object is a table or an endpoint.
2. The server validates and authorizes the plan with the same rules.
3. A new executor for API sources builds the requests: the registered endpoint, only its registered parameters, with typed values. Filters the API supports are sent to it; the remaining filters, grouping, totals, sorting and limits are calculated by the server on the rows it received.
4. Results go through the same result model, so charts, exports, sharing, history, Monitoring Rules and Periodic Reports work without change.

**Rules.**
- **Read only.** Only requests that read data. An endpoint that needs another method to *query* (some APIs search with POST) must be registered as such by the administrator; nothing that creates, changes or deletes is ever called.
- **Nothing from the model reaches the request line.** Addresses, paths, parameter names and headers come from the registration only.
- **Row scopes are enforced or the object is refused.** For a user limited to a department, section or own records, the server either sends the scope as a registered parameter or filters the returned rows on the mapped field. An endpoint with neither can be read only by roles with `ALL`.
- **Limits protect the API and the server:** a maximum number of pages, rows, bytes and seconds per question; the API's own rate limit is respected; when a limit cuts the data, the answer says that it is partial (as version 1 does for long lists).
- **Network safety:** HTTPS only; internal and loopback addresses are refused unless a Platform Administrator allows the host; redirects to another host are not followed.
- **Audit and usage:** registration, changes, tests and each question are recorded as for databases, without secrets and without response bodies.

**Not in the first release of A1:** combining a database table and an API endpoint in one query; APIs that are not JSON over HTTPS (SOAP, GraphQL); writing data back.

**Open questions for the owner:** see §7, questions 1–3.

### B1. Drill-down in charts (owner decision, 2026-11-09)

**Goal.** In an answer or dashboard, a user clicks a bar, a slice or a point and sees the next level of detail for it, for example from *branches* to *the tellers of the branch they clicked*, and can go back.

**Requirements.**
- The plan can carry a **drill path** for a panel: an ordered list of levels (branch, then teller). The model proposes it when the question asks for it ("by branch, then by teller") or when the catalogue's relationships make a next level obvious; the user can also choose the next level from the columns they may read.
- A click runs the next level **as a new authorized query** with the clicked value as a filter. It goes through validation, permissions and row scopes like any question; a level the user may not read isn't offered.
- The panel shows where the user is (*All branches › Deira*), with a way back to each earlier level.
- Each level can be exported and shared like any panel. PDF and Word show the levels that were open when the study was made; they stay static documents.
- Works in English and Arabic (right-to-left), with keyboard access, on desktop and mobile.
- History keeps the drill steps, so reopening an analysis restores the level.

### C1. Sign in with Microsoft (owner decision, 2026-11-08)

The goal, the rules, the data, the interface and the tests are already written in [`IMPLEMENTATION_PLAN.md`, *Version 2*](./IMPLEMENTATION_PLAN.md#version-2-planned-not-started) and are unchanged. In short: Microsoft proves who the person is, including the second step; Wabi Insight still decides what they may see; only users an administrator created or invited can sign in; accounts are linked by Microsoft's permanent identifiers, never by e-mail alone.

### D1. Google Drive and OneDrive publishing (owner decision, 2026-11-06)

The scope is the one written for P10.2 in the implementation plan: an organization connects its Google Drive or OneDrive account (tokens encrypted), and a user who may share a report can publish it there with read-only access for chosen people; every publication is recorded and can be withdrawn. The same rules as e-mail sharing apply: only what the owner of the analysis was authorized to see, and only to recipients the organization allows.

### B3. Follow-up questions (proposed)

A user who has just received an answer can refine it in words ("only 2025", "split by product", "as a line chart") without retyping the question. The server sends the previous plan with the new request; the model returns a changed plan, which is validated and authorized again. Each step is a new version in history.

### E4 and E5. E-mail durability and several API copies (proposed)

Version 1 keeps e-mails in memory until they are sent, on purpose: a temporary password is never stored. Since P13.24 an e-mail lost at a restart is shown as failed and has to be sent again. E4 would keep the message encrypted in the database until it is delivered, then erase it. E5 is only needed if one server is no longer enough; it depends on E4 and moves the request limits and the scheduler's bookkeeping to the database.

## 6. Out of scope for version 2

- Writing to source systems, in databases or through APIs. Wabi Insight stays read-only on business data.
- Executing SQL, addresses or code written by the model.
- Logic for one industry (banking rules, aviation rules): the engine keeps working from each organization's own catalogue.
- Automatic actions on people (suspending a user because of an alert): a person decides.
- Microservices, message brokers, a separate mobile app.
- Combining data from two different sources in one query (may be reconsidered after A1).

## 7. Open decisions for the owner

| # | Question | Affects |
|---|---|---|
| 1 | Which API will be the first real one (its name, how it signs in, whether it has an OpenAPI description)? The first release of A1 should be built against a real case. | A1 |
| 2 | May an Organization Administrator register any public API address, or must a Platform Administrator approve each host first? | A1 |
| 3 | Are the three sign-in methods enough for the first release (API key, user name and password, OAuth 2.0 client credentials)? | A1 |
| 4 | Drill-down: should the user be able to pick any next level themselves, or only the levels the answer proposes? | B1 |
| 5 | Sign in with Microsoft: keep passwords as a second option for organizations, or Microsoft only once it is switched on? | C1 |
| 6 | When a user has two roles, their permissions are added together today (a sensitive column from one role becomes visible for the rows of the other). Keep this, or evaluate each role separately? | Permissions |
| 7 | Sharing sends the owner's stored result to the recipient, whatever the recipient's own permissions. Keep this (the owner decides), or check the recipient's permissions too? | Sharing |
| 8 | E-mails: accept storing the message encrypted until it is sent (E4), or keep the version 1 rule that bodies are never stored? | E4 |
| 9 | Which of the *Could* items should move up or be dropped? | All |

## 8. Proposed order

A proposal, in releases that can each be deployed on their own. Version 2 starts after the version 1 fixes (P13.25–P13.30) are merged. The pull-request numbers continue the two already planned for Microsoft sign-in.

| Release | Content | Pull requests |
|---|---|---|
| **2.0 — Sign in with Microsoft** | C1 | `V2.1` backend · `V2.2` interface and guides (as already planned) |
| **2.1 — API data sources** | A1 | `V2.3` registration, catalogue and secrets · `V2.4` executor, limits and row scopes · `V2.5` interface, sample API and guides |
| **2.2 — Drill-down** | B1, then B3 | `V2.6` plan, authorization and history · `V2.7` charts and interface · `V2.8` follow-up questions |
| **2.3 — Publishing** | D1 | `V2.9` connections and publishing · `V2.10` interface and guides |
| **Later** | The *Could* items the owner keeps (A4, A5, B4, B5, D2, E4–E6) | one or two pull requests each |

Reasons for this order:
- **Microsoft sign-in first:** it is already designed, it is small, and it removes password handling for the first customers.
- **API sources before drill-down:** drill-down then works on both kinds of source from its first day.

## 9. How each item is accepted

As in version 1, every pull request:

- keeps the invariants of §3 and names any it had to extend;
- has unit tests and SQL Server integration tests with **two organizations**; API sources add a test API server, drill-down adds tests for each level's permissions;
- is checked in the browser in English and Arabic, with real screenshots;
- updates the specification, `ARCHITECTURE.md`, `SECURITY.md`, the deployment guides where a setting or a step changes, and `PROGRESS.md`;
- is verified live on the sample organizations (Gulf Aviation Services, Wabi Bank) where the feature reads data.

## 10. Change log

| Date | Change |
|---|---|
| 2026-11-10 | First version: owner decisions of 2026-11-06 (Drive and OneDrive), 2026-11-08 (Sign in with Microsoft), 2026-11-09 (drill-down) and 2026-11-10 (API data sources); items carried over from the review of 2026-11-10; proposals. |
| 2026-11-10 | Owner decision: fixes belong to version 1. The 23 items carried over from the review were moved out, to P13.25–P13.30 in the implementation plan; the release "2.0 — Safe base" and the open decision on the audit page went with them. Version 2 keeps the four owner decisions and the proposals. |
