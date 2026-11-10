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
- There is **one way to use it**: the platform hosted by the owner and reached over the internet, with the questions answered by an AI service on the internet. An organization that may not send its data outside its own network, not even the rows that accompany a prompt, can't use it.

**AI providers in version 1 (owner decision, 2026-11-10).** The feature that lets an organization add its own AI provider stays in the product, with the restriction added in P13.25 (an organization's provider must be a public `https` address). For version 1 the owner switches it off by configuration: a Platform Administrator empties the allowed provider types on the platform's AI providers page, so every organization uses the platform's subscription. No code change. Version 2 opens it again in a controlled way (G2).

## 3. What does not change

The architectural invariants of version 1 (see [`CLAUDE.md`](../CLAUDE.md) and specification §34) hold in version 2. In particular:

- **Three logical layers:** one Angular app, one ASP.NET Core API, and data and integrations. No microservices, message brokers or separate applications. API data sources are a new kind of integration in the third layer, not a new service. The service plans (G1) change **where** the layers run, not how many there are; every installation still has exactly one configuration database.
- **Organization isolation:** `OrganizationId` always comes from the signed-in user; every tenant-owned query is filtered and ownership-checked; Platform Administrators never see business data.
- **The model never writes what is executed.** In version 1 the model produces a typed plan and the server compiles it to read-only SQL. Version 2 extends the same rule to APIs: the model produces the same kind of plan, and the server turns it into requests to endpoints the organization registered. **The model never supplies a URL, a path, a header or a request body.**
- **Permissions:** table, column and row scopes (`ALL`, `DEPARTMENT`, `SECTION`, `SELF`) apply to every data source, of any kind.
- **Business users never see technical details** (queries, endpoints, row counts, tokens, cost).
- **The product stays generic:** no logic specific to one business domain.
- **Secrets** are encrypted, never returned to the browser, never written to logs.
- **An address inside a private network is never reachable just because an organization typed it** (P13.25). Version 2 lets an organization use a model at a private address only through an approval that names the organization and the host (G2).
- **Whoever proves the identity, Wabi Insight decides the access.** Microsoft Entra ID and Active Directory say who the person is; the organization, roles and scopes always come from the Wabi Insight user record linked to that identity.
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
| C11 | **Sign in with Active Directory** (classic, on-premises): a person signed in to their Windows computer opens Wabi Insight without typing a password | Owner, 2026-11-10 | Must | M |

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

### G. Service plans and private AI

| ID | Item | Source | Priority | Size |
|---|---|---|---|---|
| G1 | **Three service plans**, chosen per organization: **Cloud**, **On-premises** and **Hybrid** | Owner, 2026-11-10 | Must | L |
| G2 | **An organization's own AI model**, in its data centre, its private cloud or its own public-cloud account, so prompts and the data sent with them never go to a public AI service | Owner, 2026-11-10 | Must | M |
| G3 | **On-premises plan:** the whole platform (web app, API, database, files and the model) inside the organization's network, working without the internet | Owner, 2026-11-10 | Must | L |
| G4 | **Hybrid plan:** web app and API in the owner's cloud; files and the model at the organization's location; the platform database in the owner's cloud or at the organization's location | Owner, 2026-11-10 | Must | L |
| G5 | **An organization's own e-mail server (SMTP)** in every plan, the Cloud plan included; the platform's server is used when the organization sets none | Owner, 2026-11-10 | Must | M |
| G6 | **Reaching an organization's systems from the owner's cloud** (its business databases and APIs, and its own database, files, model and mail server): the supported ways, with a recommendation | Owner, 2026-11-10 | Must | L |

## 5. Requirements in detail

Only the items that need more than their line in the tables are described here.

### G1. Three service plans (owner decision, 2026-11-10)

**Goal.** From version 2 an organization chooses how it uses Wabi Insight. The reason is data sensitivity: for some organizations even the rows sent to the AI provider with a prompt are critical, and they do not accept a service reached over the internet in a public cloud.

"The organization's location" below means any place the organization controls: its data centre, its private cloud, or its own account in a public cloud.

| | **Cloud** | **Hybrid** | **On-premises** |
|---|---|---|---|
| Web app and API | Owner's cloud | Owner's cloud | Organization's location |
| Platform (configuration) database | Owner's cloud | Owner's cloud **or** organization's location | Organization's location |
| Files (reports, exports, attachments, logos) | Owner's cloud | Organization's location | Organization's location |
| AI model | The platform's subscription, or the organization's own model (G2) | Organization's location (G2) | Organization's location |
| Business databases and APIs that are asked about | Wherever the organization keeps them, as today | Same | Same |
| Shared with other organizations | Yes (one installation, many organizations) | See G4 | No: one installation for one organization |
| Platform Administrator | The owner | The owner | The organization's IT team |
| Sign-in | E-mail and password; Microsoft Entra ID (C1) | Same; Active Directory through federation (C11) | E-mail and password; Microsoft Entra ID (C1); Active Directory (C11) |
| Internet needed | Yes | Yes, between the owner's cloud and the organization | No |
| Who installs and updates | The owner | The owner (the organization provides the network path and its own parts) | The organization, with packages and guides from the owner |

**The organization's own, or the platform's default.** The plans are built from one rule, applied to each part separately: where an organization has set up its own, that one is used; where it has not, the platform's default is used.

| Part | Platform default | The organization's own | Item |
|---|---|---|---|
| E-mail server (SMTP) | The platform's server and sender | Its server, sender address and name | G5 |
| Files | The platform's file store | Its Azure Blob Storage, S3-compatible store | G4 |
| AI model | The platform's subscription | Its model, public or private | G2 |
| Sign-in | E-mail and password | Microsoft Entra ID, Active Directory | C1, C11 |
| Platform database | The shared database | Its own database, through a dedicated installation | G4 |

A Cloud-plan organization can therefore use its own e-mail server and nothing else of its own; a Hybrid-plan organization is one that keeps its files and model, and possibly its database, at its own location. What is set is shown to the Organization Administrator and to Platform Administrators (the fact, never the secret).

**Requirements common to the three plans.**
- **One product, one code base.** A plan is a way of deploying and configuring the same application, never a separate edition with its own code.
- The plan of an organization is recorded (in the organization record for a shared installation, in the installation's settings for a dedicated one) and shown to Platform Administrators.
- Every plan keeps all the rules of §3. In particular, an on-premises installation still has Platform Administrators without an organization and organization users with one, even when there is a single organization.
- The commercial side (price per plan, contract, service levels) is the owner's and is not part of this document.

### G2. An organization's own AI model (owner decision, 2026-11-10)

**Goal.** An organization's questions are answered by a model the organization runs or rents itself, at an address that can be private, so that the catalogue, the question and the result rows that accompany a prompt never reach a public AI service.

**What exists already.** Three provider protocols (Anthropic, OpenAI-compatible, Gemini); OpenAI-compatible covers the usual ways of serving a model privately (vLLM, Ollama, LM Studio, Azure OpenAI in the organization's subscription, a gateway). A Platform Administrator can already register a provider at a private address; an organization cannot (P13.25).

**Requirements.**
- **On-premises plan:** nothing new is needed for the address. The organization's IT team is the Platform Administrator and registers its model as the platform provider.
- **Cloud and Hybrid plans:** an organization may use a model at a private address **only after a Platform Administrator approved that host for that organization**. The approval names the organization, the host and the port; it is recorded in the audit log, and it can be withdrawn. Without it, the P13.25 rule stands unchanged. Another organization can never use the approved host.
- The connection to a private model from the owner's cloud needs a private network path (see G4); TLS is required, with the organization's own certificate authority accepted when the organization supplies it.
- **Fitness check.** Wabi Insight needs a model that returns answers in a fixed JSON structure and follows the planning rules. The provider test is extended into a short qualification run (a planning question, a monitoring-rule draft, an explanation) that says clearly whether the model is good enough, before the organization relies on it. The minimum is documented.
- **No silent fallback** (already a version 1 rule): if the organization's model is unavailable, the question fails with a clear message. It is never sent to the platform's provider instead.
- Cost figures for an organization's own model are optional (prices can be left at zero); token counts are still recorded for the organization.
- The owner re-enables organization-managed providers per plan through the existing allow-list.

### G3. On-premises plan (owner decision, 2026-11-10)

**Goal.** One organization runs the whole platform inside its own network: web app, API, configuration database, files and model. Nothing leaves that network.

**What exists already.** The deployment guides for Windows Server with IIS, Ubuntu with nginx, and Docker Compose; SQL Server (Express to Enterprise) for the configuration database; files on the server disk; e-mail through any SMTP server; a platform provider at a private address. The web app loads no fonts, icons or scripts from the internet.

**Requirements.**
- **Works with no internet at all**, proven by a test on a disconnected server: installation from a package carried in (no downloads during installation), start-up, sign-in, questions, reports, e-mail through the organization's SMTP server, scheduled jobs.
- **An installation package per release** (API, web app, database migration, guides, checksums) that the organization's IT team installs and updates by following a guide, without access to the owner's GitHub. Each release states what it changes and how to go back.
- **Set-up for one organization:** the first Platform Administrator, the organization and its first administrator are created in one guided step.
- **Sign-in:** e-mail and password, Microsoft Entra ID (C1) where the organization has it, and Active Directory (C11).
- **Support without access.** The owner has no account in an on-premises installation. Help is given from a diagnostic file the organization's IT team exports and sends (versions, settings without secrets, health checks, recent errors without business data), or in a session the organization hosts.
- **Backups, certificates and monitoring** are the organization's, with a guide.
- **Licence:** how the right to use an on-premises installation is expressed and checked is an open decision (§7).

### G4. Hybrid plan (owner decision, 2026-11-10)

**Goal.** The organization uses the web app and API in the owner's cloud, while what it considers sensitive stays at its own location: always its **files** and its **model**, and optionally the **platform database**.

**Requirements.**
- **Files at the organization's location.** Version 1 has one file store for the whole installation. Version 2 lets an organization have its own: at least Azure Blob Storage in the organization's subscription and S3-compatible storage (which covers most private clouds and on-premises object stores). Credentials are encrypted; a file of that organization is never written to the platform's store, not even temporarily on disk beyond what producing it requires.
- **Model at the organization's location:** G2.
- **Platform database, two variants** (the organization chooses):
  - *In the owner's cloud:* the organization is one of the organizations of the shared installation, with its own file store and model.
  - *At the organization's location:* because an installation has exactly one configuration database (§3), this is a **dedicated installation** of the web app and API in the owner's cloud for that one organization, pointing at the database the organization hosts. A shared installation with a different database per organization is not proposed (§7, question 11).
- **A private network path** (the ways and the recommendation are in G6) between the owner's cloud and the organization's location carries the model calls, the file store and, in the second variant, the database: a site-to-site VPN or a private link, with the organization's firewall opened only to the owner's fixed addresses. The guide states what the organization has to provide.
- **What still passes through the owner's cloud** must be said plainly to the customer: in a Hybrid plan the API in the owner's cloud reads the rows that answer a question and holds them in memory to build the answer, and the browser receives them from there. Hybrid keeps data *at rest* and the *AI processing* at the organization's location; only the On-premises plan keeps everything inside the organization's network.
- When the path is down, the organization's users get a clear message; other organizations are not affected.

### G5. An organization's own e-mail server (owner decision, 2026-11-10)

**Goal.** In every plan, an organization can have its e-mails (temporary passwords, shared analyses, alert digests, Periodic Reports, support notices) sent through its own SMTP server, from its own address and display name. An organization that sets none keeps the platform's server and sender, as in version 1 (`no-reply@…`, "Wabi Insight Notifications").

**Requirements.**
- The Organization Administrator enters the server, port, encryption (TLS required), user name and password, the sender address and the display name, and an optional reply-to address. The password is encrypted like every other secret and never shown again.
- **Send a test message** before the settings take effect; they are used only after one test succeeded.
- **Which messages.** Everything sent to that organization's users goes through its server. Messages about the platform itself (to Platform Administrators) always use the platform's server.
- **No silent fallback (proposed, §7 question 19).** When the organization's server refuses or is down, the message is retried and then shown as failed, with the reason, to the Organization Administrator; it is not sent through the platform's server instead, because the organization chose where its mail leaves from. A system alert tells the administrators.
- **The address is checked** like an organization's AI provider (P13.25): a public address, unless a Platform Administrator approved a private host for that organization (G6).
- The platform's default sender keeps working for everyone else; its domain needs correct SPF, DKIM and DMARC records so its mail is not marked as spam (a deployment step, not code).
- Usage and audit record which server sent a message (platform or organization), never the credentials.

### G6. Reaching an organization's systems from the owner's cloud (owner decision, 2026-11-10)

**The question.** When the web app and API run in the owner's cloud (Cloud and Hybrid plans), they have to reach things that sit in the organization's network: always the **business databases and APIs** that are asked about, and, for whatever the organization keeps as its own, its **database, files, model and mail server**. This section lists the ways to do that, from the most to the least recommended. **The owner chooses when this step is reached** (§7, question 10); more than one can be offered, and different organizations can use different ones.

| # | Way | How it works | For | Against |
|---|---|---|---|---|
| 1 | **Outbound connector** (recommended as the standard offer) | A small program inside the organization's network opens an encrypted connection **out** to the platform and carries the requests back in. The organization opens no inbound port. Either an existing product (WireGuard or Tailscale, Cloudflare Tunnel, Azure Relay Hybrid Connections) or a connector of our own. | Nothing is exposed to the internet; passes most firewalls without a change; the organization can stop it at any moment; one way for databases, APIs, files, model and mail. | Something to install and keep running at the organization. A connector of our own would be a new program beside the three layers (§3), so an existing product is preferred first. |
| 2 | **Site-to-site VPN** (recommended for larger organizations) | An IPsec or WireGuard tunnel between the owner's network and the organization's, limited to the needed hosts and ports. | Familiar to every network team; no application change; also the usual answer of auditors. | Set up per organization by both network teams; address ranges may overlap; the owner's side needs a VPN gateway (a cost). |
| 3 | **Private link in the same cloud** | When the organization's systems are in the same public cloud as the platform: a private endpoint or network peering. | No internet at all; managed by the cloud. | Only when both sides are in the same cloud. |
| 4 | **Firewall rule for the platform's fixed address** (the minimum, for a quick start) | The organization opens the database or API port **only** to the platform's fixed public address. | Nothing to install; minutes to set up; many organizations already do this for other suppliers. | The port faces the internet, even if filtered; depends on the platform's address never changing; one rule per system. |
| 5 | **A read-only API in front of the data** | The organization publishes a read-only API (an API gateway) and the platform uses it as an API data source (A1), with a key or mutual TLS. | The database itself is never reachable; the organization decides exactly what is offered. | Work for the organization; only as rich as the API; needs A1. |

**Never offered:** a database port open to the whole internet; a copy of the organization's data pushed to the owner's cloud to be asked there.

**Rules for every way.**
- The login the platform uses stays **read-only** on business data (version 1 already checks and warns), with only the objects it needs.
- **Encryption in transit is required**: TLS on the database or API connection even inside a tunnel; the server's certificate is checked (the organization's own certificate authority is accepted when it supplies it).
- **A private address is reachable only after approval**: a Platform Administrator approves the host and port for that organization (the rule of G2 and P13.25); no other organization can use it.
- Way 4 additionally needs: a fixed address for the platform (stated in the contract), the database's own address filter where it has one, a strong unique password or certificate, and a review of the rule every year.
- When the path is down, the organization's users get a clear message, the scheduler retries (P13.27), and other organizations are not affected.
- Each organization's page shows which way it uses and when the path last worked.

**Recommendation.** Offer **way 4 with its conditions** from the first Hybrid or Cloud customer, because it needs nothing new, and say plainly that it is the minimum. Offer **way 1 with an existing product** as the standard, and **way 2** when the organization's network team asks for it. Build a connector of our own only if several customers refuse both a VPN and an existing tunnel product.

### C11. Sign in with Active Directory (owner decision, 2026-11-10)

**Goal.** In organizations that use classic on-premises Active Directory, a person who is signed in to their Windows computer opens Wabi Insight and is signed in as that same Windows user, **without typing a password**. Their roles in Wabi Insight belong to that Windows user name.

**How it works.** The browser on a domain computer proves the Windows identity to the server by itself (Integrated Windows Authentication, Kerberos). Wabi Insight never sees or stores a password.

**Requirements.**
- **Only known users.** As with Microsoft sign-in, a person can enter only if an administrator created a Wabi Insight user for their Windows account. An Organization Administrator adds a user by Windows user name (`DOMAIN\name` or `name@domain`), chosen from the directory where the server can read it, and gives the roles, department and section in Wabi Insight as today. No password is set and none is e-mailed.
- **Linked by the directory's permanent identifier** (the account's security identifier), never by the name alone, so a renamed or re-created account can't take over someone else's access. The name is shown; the identifier decides.
- A disabled or deleted Windows account can no longer sign in; deactivating the user in Wabi Insight still ends access immediately.
- **Fallback:** from a computer that is not in the domain, the same user gets a sign-in form checked against Active Directory, or is refused, as the organization chooses.
- The organization's administrator decides which sign-in methods are allowed (password, Microsoft, Active Directory).
- Audit, lockout of the sign-in form, idle sign-out and the absolute session lifetime apply as for every other method.

**Where it is available.**

| Plan | Classic Active Directory | Microsoft Entra ID (C1) |
|---|---|---|
| On-premises | Directly: the server is in the organization's domain, so sign-in is silent on domain computers | Yes, when the server may reach Microsoft |
| Hybrid and Cloud | Not directly: a server in the owner's cloud is not in the organization's domain. Through the organization's federation service (AD FS), or through Microsoft Entra ID when the organization synchronizes its directory to it; both can still be silent on domain computers | Yes |

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
- A separate edition or code base per plan.
- Supplying, hosting or tuning the organization's AI model: in the On-premises and Hybrid plans the model is the organization's (the owner documents what it must be able to do).
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
| 10 | Reaching an organization's systems: which of the five ways in G6 are offered, and which first? (Recommended: the firewall rule with its conditions as the minimum, an outbound connector with an existing product as the standard, a site-to-site VPN on request.) | G2, G4, G5, G6 |
| 11 | Hybrid with the platform database at the organization's location: a dedicated installation per organization in the owner's cloud (proposed, keeps one database per installation), or a shared installation with a separate database per organization (a change to the architecture)? | G4 |
| 12 | Hybrid, files: which stores are supported first (Azure Blob Storage in the organization's subscription, S3-compatible storage, a network share)? | G4 |
| 13 | On-premises licence: contract only, or a licence file the installation checks (organization, expiry, number of users)? | G3 |
| 14 | On-premises updates: how often a release is packaged, how many past releases are supported, and who applies them. | G3 |
| 15 | Active Directory: are roles always given in Wabi Insight (proposed), or may an organization also map Active Directory groups to roles, so users are created at first sign-in? | C11 |
| 16 | Active Directory for Hybrid and Cloud organizations: offer federation (AD FS), or only Microsoft Entra ID for them? | C11 |
| 17 | The organization's own model: is one named model tested and recommended by the owner for Arabic and English, or is any model that passes the fitness check accepted? | G2 |
| 18 | Which plan does the first paying customer need? The order in §8 should follow it. | G1 |
| 19 | An organization's own e-mail server is down: show the message as failed (proposed), or send it through the platform's server instead? | G5 |
| 20 | May a Cloud-plan organization use its own e-mail server at no extra charge, or is it part of a paid plan? (Commercial; decides only where the setting is shown.) | G5 |

## 8. Proposed order

A proposal, in releases that can each be deployed on their own. Version 2 starts after the version 1 fixes (P13.25–P13.30) are merged. The pull-request numbers continue the two already planned for Microsoft sign-in.

| Release | Content | Pull requests |
|---|---|---|
| **2.0 — Sign-in** | C1, C11 | `V2.1` Microsoft sign-in: backend · `V2.2` Microsoft sign-in: interface and guides (as already planned) · `V2.3` Active Directory sign-in |
| **2.1 — Own model and On-premises plan** | G1, G2, G3 | `V2.4` plans recorded; approved private model addresses; fitness check · `V2.5` on-premises package, one-step set-up, disconnected test, diagnostic file, guide |
| **2.2 — Hybrid plan** | G4, G5, G6 | `V2.6` a file store and an e-mail server per organization (G5) · `V2.7` the chosen ways to reach an organization's systems (G6), dedicated installation with the database at the organization's location, network path and guide |
| **2.3 — API data sources** | A1 | `V2.8` registration, catalogue and secrets · `V2.9` executor, limits and row scopes · `V2.10` interface, sample API and guides |
| **2.4 — Drill-down** | B1, then B3 | `V2.11` plan, authorization and history · `V2.12` charts and interface · `V2.13` follow-up questions |
| **2.5 — Publishing** | D1 | `V2.14` connections and publishing · `V2.15` interface and guides |
| **Later** | The *Could* items the owner keeps (A4, A5, B4, B5, D2, E4–E6) | one or two pull requests each |

Reasons for this order:
- **Sign-in first:** Microsoft sign-in is already designed, and an on-premises customer will ask for Active Directory on the first day.
- **The plans before the features:** they decide which organizations can use the product at all; the features that follow are then built once and work in every plan.
- **On-premises before Hybrid:** it is the smaller step (the model address already works for a Platform Administrator, the deployment guides exist) and it is the plan that fully answers the data concern.
- **API sources before drill-down:** drill-down then works on both kinds of source from its first day.

If the first customer needs another plan, the order changes (§7, question 18).

## 9. How each item is accepted

As in version 1, every pull request:

- keeps the invariants of §3 and names any it had to extend;
- has unit tests and SQL Server integration tests with **two organizations**; API sources add a test API server, drill-down adds tests for each level's permissions;
- is checked in the browser in English and Arabic, with real screenshots;
- updates the specification, `ARCHITECTURE.md`, `SECURITY.md`, the deployment guides where a setting or a step changes, and `PROGRESS.md`;
- is verified live on the sample organizations (Gulf Aviation Services, Wabi Bank) where the feature reads data;
- states which plans it applies to, and is checked in each of them: a feature is not done while it works only in the Cloud plan. The On-premises check runs on a server with no internet and a privately served model.

## 10. Change log

| Date | Change |
|---|---|
| 2026-11-10 | First version: owner decisions of 2026-11-06 (Drive and OneDrive), 2026-11-08 (Sign in with Microsoft), 2026-11-09 (drill-down) and 2026-11-10 (API data sources); items carried over from the review of 2026-11-10; proposals. |
| 2026-11-10 | Owner decision: fixes belong to version 1. The 23 items carried over from the review were moved out, to P13.25–P13.30 in the implementation plan; the release "2.0 — Safe base" and the open decision on the audit page went with them. Version 2 keeps the four owner decisions and the proposals. |
| 2026-11-10 | Owner decisions: three service plans chosen per organization, Cloud, On-premises and Hybrid (G1, G3, G4); an organization's own AI model at a private address (G2); sign-in with classic Active Directory without a password (C11). For version 1, organization-managed AI providers are switched off by configuration. Open decisions 10–18 and a new proposed order. |
| 2026-11-10 | Owner decisions: an organization's own e-mail server in every plan, with the platform's as the default (G5); the rule "the organization's own, or the platform's default" stated for e-mail, files, model, sign-in and database (G1); the ways to reach an organization's systems from the owner's cloud, with a recommendation, for the owner to choose from at that step (G6). Open decision 10 rewritten; 19 and 20 added. |
