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

- **Three logical layers:** one Angular app, one ASP.NET Core API, and data and integrations. No microservices, message brokers or separate applications. API data sources are a new kind of integration in the third layer, not a new service. The service plans (G1) change **where** the layers run, not how many there are; every installation still has exactly one configuration database. **Two exceptions, both outside the platform itself and decided by the owner:** the *Wabi Data Gateway* (G7), a small read-only program installed in an organization's network for the Cloud plan, and the *Wabi Hub* (G8), the owner's central portal for licences, updates and support of on-premises installations. Neither holds business logic of the platform; the web app and the API stay one application.
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
| D4 | **Ask by WhatsApp:** a user sends a question from their confirmed WhatsApp number and receives the answer by WhatsApp | Owner, 2026-11-10 | Should | M |

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
| G7 | **Wabi Data Gateway:** a small program in the organization's network that connects out to the platform, runs the platform's checked plans read-only on the organization's own databases and returns the rows (Cloud plan) | Owner, 2026-11-10 | Must | L |
| G8 | **Wabi Hub:** the owner's central portal for on-premises installations: licences, update packages, support tickets and health, with operational data only, never business data | Owner, 2026-11-10 (with decision 24) | Must | L |

### H. Outputs: branding, language and templates

| ID | Item | Source | Priority | Size |
|---|---|---|---|---|
| H1 | **The organization's logo and name on every output**, the name in the output's language; the platform's logo with the organization's name when it has no logo | Owner, 2026-11-10 | Must | M |
| H2 | **The output's language and direction follow the question**, not the language the user's screen is set to | Owner, 2026-11-10 | Must | M |
| H3 | **A template gallery per kind of output** (study, Word, Excel, dashboard, e-mails): the organization chooses, and users may pick another at the moment they create an output | Owner, 2026-11-10 | Must | L |
| H4 | **"Powered by Wabi Insight by WabiTex Solutions FZE"** on every output | Owner, 2026-11-10 | Must | S |

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

**How the plans are positioned (owner decision 24).**
- **On-premises is the lead plan** for banks, government and large companies: it answers "where does our data go?" completely, matches the data-residency rules these buyers work under, and is sold the way they buy (licence plus a yearly support contract, §10).
- **Cloud is the second plan**, for small and mid-size organizations, trials and demonstrations: it is already built, and a new organization can start in days.
- **Hybrid is not advertised.** It is the hardest to build and support and the weakest promise (the answers still pass through the owner's cloud); it is offered only as a custom project to a customer who insists.

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
- **When the organization's server is down (owner decision, 2026-11-10).** The message is **not** sent through the platform's server instead: the organization chose where its mail leaves from. It waits and is **rescheduled until the organization's server accepts it** (every 5 minutes for the first hour, then every 30 minutes). Meanwhile the platform **notifies the organization's administrators automatically, through the platform's default server**:
  - once when the server stops accepting mail ("Your mail server is not accepting messages; N messages are waiting"), in the app and by e-mail;
  - a reminder once a day while it stays down, with the number of waiting messages;
  - once when it works again ("Your mail server is working again; the waiting messages were sent").

  The notice goes to every active Organization Administrator **and to the organization's contact e-mail** (organization profile), because an administrator whose mailbox is on the same broken server would not receive it; the contact address should therefore be on another domain, and the settings page says so.
  Waiting messages have to be kept until they are sent, encrypted, and erased once delivered; that is the E4 change (§7 question 8 is answered for this case).
- **How long a message may wait (owner decision, 2026-11-10).** It depends on how often the content comes: a newer one soon replaces it.

  | Message | Waits at most | Then |
  |---|---|---|
  | Daily alert digest, daily Periodic Report (and anything more frequent) | 12 hours | Given up |
  | Weekly Periodic Report | 48 hours | Given up |
  | Periodic Report less frequent than weekly (monthly) | 72 hours | Given up |

  A message given up is erased (it is not sent later through any server), recorded as *expired* in the delivery log with the reason, and listed in the next notice to the administrators ("3 messages could not be sent and were dropped"); a Periodic Report counts it as a failed run (P13.27 pausing rules).
- **Messages that do not repeat (owner decision, 2026-11-10).** A **temporary password** waits **one hour**; if the organization's server still refuses it, it is sent through the platform's default server, so a new user is not locked out, and the administrators' next notice says so ("1 temporary password was sent through the Wabi Insight mail server"). **Every other one-off message** (a shared analysis, an account message) waits **72 hours**, then is given up, logged and reported like the others.
- **Getting a mail server (owner decision, 2026-11-10).** An organization that wants its own sender needs **a domain of its own and a mailbox on it** (from any e-mail provider: Microsoft 365, Google Workspace, its hosting company, its own server), and the SMTP details of that mailbox. Its Organization Administrator then enters them on the settings page, **or asks the platform team for help** through a support ticket: the help is a support grant with a new typed operation, *configure the e-mail server*, which the organization approves like any other (specification §16), so the platform team enters the details without seeing anything else. The settings page explains the steps (domain, mailbox, SMTP details, SPF and DKIM records for that domain) in English and Arabic. Pricing of that help is commercial and not part of this document.
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

**In plain words** (for the choice in §7 question 10). The platform runs on the owner's server on the internet; the organization's database sits behind its own firewall, which by default lets nothing in. Something has to let the platform's questions through that firewall, safely. The five ways differ in **who opens what** and **what the organization's network team has to do**:

| # | Picture it as | What the organization does | What the owner does | Typical time to set up | Ongoing cost |
|---|---|---|---|---|---|
| 1 | A phone line the organization dials out on, and keeps open: the platform talks back on that same line | Installs one small program on one server inside its network | Runs the other end (a free or low-cost service, or a small relay) | Hours | Low |
| 2 | A private, encrypted pipe between the two buildings | Its network team configures its firewall or router | Configures a VPN gateway on the platform side, per organization | Days (two teams agree on settings) | A VPN gateway (tens of dollars a month), per organization |
| 3 | Both sit in the same cloud, joined by the cloud's own private road | Asks its cloud team for a private endpoint or peering | The same on the platform side | Hours to days | Small cloud charge |
| 4 | A door in the organization's wall that opens only for the platform's badge (its fixed address) | Opens one port on its firewall for one address | Keeps its address fixed forever and says so in the contract | Minutes | None |
| 5 | A reception desk: the organization answers set questions through its own API, the database stays inside | Builds or configures a read-only API | Registers it as an API data source (A1) | Weeks | Its API platform |

The more a way relies on the organization's firewall opening a door (4), the quicker it is and the more exposed the database is; the more it relies on a connection made from inside (1, 2, 3), the safer it is and the more set-up it needs.

**Never offered:** a database port open to the whole internet; a copy of the organization's data pushed to the owner's cloud to be asked there.

**Rules for every way.**
- The login the platform uses stays **read-only** on business data (version 1 already checks and warns), with only the objects it needs.
- **Encryption in transit is required**: TLS on the database or API connection even inside a tunnel; the server's certificate is checked (the organization's own certificate authority is accepted when it supplies it).
- **A private address is reachable only after approval**: a Platform Administrator approves the host and port for that organization (the rule of G2 and P13.25); no other organization can use it.
- Way 4 additionally needs: a fixed address for the platform (stated in the contract), the database's own address filter where it has one, a strong unique password or certificate, and a review of the rule every year.
- When the path is down, the organization's users get a clear message, the scheduler retries (P13.27), and other organizations are not affected.
- Each organization's page shows which way it uses and when the path last worked.

**Owner decision (2026-11-10).** *On-premises:* nothing to solve, the platform runs inside the organization's network. *Cloud (and Hybrid):* the recommendation below is accepted, **plus** the option of installing the **Wabi Data Gateway** in the organization's network (G7), which is the owner's form of way 1 and the preferred one when the organization accepts to run a small program.

**Recommendation.** Offer **way 4 with its conditions** from the first Hybrid or Cloud customer, because it needs nothing new, and say plainly that it is the minimum. Offer **way 1 with an existing product** as the standard, and **way 2** when the organization's network team asks for it. Build a connector of our own only if several customers refuse both a VPN and an existing tunnel product.

### G7. Wabi Data Gateway (owner decision, 2026-11-10)

**Goal.** A Cloud-plan organization whose databases are in its own network lets the platform ask them without opening any port, by installing a small program of the owner's: the **Wabi Data Gateway**. It is the same idea as the data gateways of other analytics products.

**Requirements.**
- **It connects out, it never listens.** The gateway opens an encrypted connection (HTTPS or WebSocket) from the organization's network to the platform and waits for work on it. The organization opens no inbound port; its firewall needs one outbound rule to the platform's address.
- **It receives the checked plan, never SQL.** The platform sends the validated and authorized plan (the same typed plan as today, with the row scopes already applied) and the gateway compiles it to read-only SQL with the same dialect compilers, on the organization's side. If the platform were ever compromised, nobody could send arbitrary SQL through the gateway.
- **It runs with a read-only login** the organization creates, on the databases and objects it chooses; it refuses anything else even if asked.
- **It returns structured rows, not plain text:** column names, types and values, so charts, totals, number and date formatting in English and Arabic, and the exports work as with a direct connection. Row, byte and time limits apply as on the platform.
- **The organization sees everything:** every request is logged on the gateway's machine (who asked, which object, how many rows, never the values), and the organization can stop the gateway at any moment.
- **Trust:** the gateway is registered once with a one-time code from the organization's settings page; each request is signed by the platform and checked by the gateway; the gateway's identity is a certificate that the Organization Administrator can revoke.
- **Platforms:** a Windows service and a Linux service (and a container image), installed and updated with a short guide; it reports its version and health to the organization's settings page.
- **Size of the change:** the gateway reuses the compilers and the executor of the API, so it is a small program built from the same code, not a second product. It is one of the two exceptions to "no separate applications" (§3).

**Not in the gateway:** the model, the files, the catalogue editing, the users. It only answers questions about data.

### G8. Wabi Hub (owner decision 24, 2026-11-10)

**Goal.** On-premises installations are run by each organization's IT team and the owner has no account in them (G3). The owner still needs to issue licences, ship updates, receive support requests and see whether installations are healthy. The **Wabi Hub** is the owner's own central portal for that.

**Requirements.**
- **Operational data only, never business data**, sent by each installation **with the customer's consent** (a setting the customer can switch off): version, health checks, licence status, counts (users, questions, AI spend), errors without their content.
- **Licences:** the hub issues the signed licence file (organization, expiry, number of users) that the installation checks offline (§10).
- **Updates:** the hub publishes the release packages with their checksums and notes; an installation shows "a new version is available"; the customer's IT applies it (G3).
- **Support:** the customer's administrators open tickets to the owner's team from inside their installation; the ticket and the conversation live in the hub, with the diagnostic file attached when the customer chooses. The support desk of version 1 is the starting point.
- **An installation works without the hub** (offline customers); the hub is a convenience and a support channel, never a dependency for answering questions.

### D4. Ask by WhatsApp (owner decision, 2026-11-10)

**Goal.** A user sends a question from WhatsApp and receives the answer there, as long as their WhatsApp number is registered and confirmed.

**Requirements.**
- **Provider (owner decision 25): Twilio**, on top of Meta's WhatsApp Business Platform (Cloud API). Every provider ends at Meta's Cloud API, so availability is Meta's in all cases; Twilio adds the simplest integration (a maintained .NET library, a test sandbox, one webhook format, clear delivery statuses), a published service level and support, for a small fee per message on top of Meta's. The code talks to an interface, so moving to Meta directly later, when volumes make the fee matter, changes one adapter. It needs a verified Meta business account, a dedicated number and message templates approved by Meta. Messages arrive at an HTTPS webhook in the API and answers go out through Meta's API. The platform's number is the default; an organization can use its own (the same rule as G5).
- **Who is asking:** the user registers their number in their profile and confirms it with a one-time code sent by WhatsApp; only confirmed numbers of active users are accepted; the administrator can switch a number off at once (numbers are recycled and phones lost); an optional PIN for sensitive applications.
- **The answer (owner decision 25):**
  - **a short summary** of the answer (WhatsApp allows 4,096 characters; Arabic shows correctly), in the question's language (H2);
  - **a link to download** the study, the Excel file or the analysis. The link is personal: it opens only for that user, expires after 24 hours, and for Restricted data asks the user to sign in;
  - **a dashboard is also sent as a large, high-quality image** (rendered at twice the screen resolution as PNG or high-quality JPEG; up to 5 MB it is sent as an image, above that as a document so WhatsApp does not compress it), with the organization's template and logo (H1, H3).
- **Rules of WhatsApp:** free replies within 24 hours of the user's last message; after that only approved templates, which matters for alerts and Periodic Reports sent by WhatsApp.
- **Where the data goes:** with the Cloud API, Meta's servers process the messages on the business side, and Meta no longer offers an on-premises version. D4 is therefore for the **Cloud plan and organizations that accept Meta's processing**; the settings page says so before it is switched on.
- **Cost:** Meta charges per conversation or message by country, plus the provider's fee; shown to the organization before it is switched on.
- **Order:** for corporate customers, Microsoft Teams (D2) keeps messages inside their own Microsoft tenant and may suit them better.

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

### H1. The organization's logo and name on every output (owner decision, 2026-11-10)

**Today (version 1).** PDF studies and Word files carry the organization's logo top-left when one was uploaded, and the organization's English name as text when none was. Excel files, e-mails, dashboards and printed pages carry neither.

**Version 2.**
- Every output carries a header with **the logo and the organization's name**: PDF studies, Word, Excel (first sheet header and the printed header), Periodic Reports, shared analyses, alert digests and every e-mail to organization users, and the printed dashboard.
- **The name is in the output's language** (H2): the Arabic name in an Arabic output, the English name in an English one. In an Arabic output the header is mirrored (logo on the right).
- **The organization's logo when it has one, otherwise the platform's logo** next to the organization's name. Nothing is ever left blank.
- Optional second logo for dark backgrounds (e-mail headers, cover pages); when it is missing, the normal logo is placed on a light panel.
- The logo rules of P13.25 stay: PNG or JPEG, 1 MB, checked against its content. Each output records which logo it used, so an output made last year still opens with last year's logo.

**How it will be done.** One branding object built per output from the organization's record (logo, Arabic and English names, colours from H3) and the output's language, given to every renderer (PDF, Word, Excel, e-mail). The renderers already receive a branding object for the logo; it gains the name, the language and the fallback to the platform's logo, and the e-mail and Excel renderers start using it.

### H2. The output's language follows the question (owner decision, 2026-11-10)

**Today (version 1).** An answer, its explanation and its files are written in the language the user's screen is set to, whatever language the question was asked in.

**Version 2.** The language and direction of everything produced for a question come from **the question itself**: a question written in Arabic gives an Arabic answer, Arabic explanation, right-to-left PDF, Word and Excel, Arabic chart labels and an Arabic e-mail draft, even when the user's screen is in English, and the other way round. The screen's own labels (menus, buttons) keep following the user's choice.

**Rules.**
- **Detection without the model:** the server counts the letters of the question (numbers, codes and punctuation are ignored): mostly Arabic letters gives Arabic, mostly Latin gives English. A question with no letters at all (only a code or a number) uses the user's screen language.
- **An explicit request wins:** "answer in English" / «أجب بالعربية» in the question sets the language.
- **The language is stored with the analysis**, so a re-run, an export, a share and a Periodic Report keep it. A Periodic Report's language option becomes *as asked* (default), *English*, *Arabic* or *each recipient's language*.
- The user can still change it per answer with one click (the answer is written again in the other language; the figures do not change).
- Names from the catalogue (tables, columns, values) are shown in the output's language where the catalogue has both names (the Arabic headers of P13.28 are the basis).

**How it will be done.** A small language detector in the server is applied to the question before planning; its result replaces the language the screen sends today, is passed to the planner, the writer and every renderer (they all already take a language), and is saved with the analysis.

### H3. A template gallery for every kind of output (owner decision, 2026-11-10)

**Goal.** Each organization chooses how its outputs look from a gallery of ready designs, for every kind of output, instead of the one design of version 1.

| Kind of output | What a template decides |
|---|---|
| PDF study | Cover page (with or without), header and footer, colours, fonts, the order and style of the sections (summary, findings, figures, tables, recommendations), table style, chart palette |
| Word file | The same as the study, as an editable document |
| Excel file | Sheet header, table style, a summary sheet or not, chart sheets or not |
| Dashboard | Arrangement of the panels (KPI row on top, two columns, one column), card style, chart palette |
| E-mails | One design for each message: shared analysis, Periodic Report, alert digest, account messages (new account, password reset) |

**Requirements.**
- **Templates are designs, not code.** A template is a stored description (layout, colours, fonts, sections, options) that the existing renderers read. Organizations never upload HTML, scripts or documents to be filled in: an e-mail cannot be made to carry something the platform does not produce, and a study cannot leave out the permission and data rules.
- **The gallery** starts with four or five designs per kind (for example *Classic*, *Modern*, *Executive*, *Compact*, *Minimal*), each shown with a real preview in English and Arabic.
- **The organization's colours:** the Organization Administrator sets a main and a second colour (and optionally picks them from the logo); every template uses them. Contrast is checked, so text stays readable.
- **Who chooses:** the Organization Administrator sets the default template for each kind; when creating an output (exporting a study, scheduling a Periodic Report, sharing by e-mail) a user may pick another template from the gallery, unless the administrator locked the default.
- **Every template works in both languages and directions**, and always includes H1 (logo and name) and H4 (the "Powered by" line); a template can place them but not remove them.
- An output records the template and version it was made with; changing a template later does not change outputs already made.
- **Organizations choose; they do not design (owner decision 23).** An organization picks a template for each kind of output and sets its colours; there is no template editor. New designs are added to the gallery by the platform team, and on request the platform team can make an organization's own design as a private template for that organization.

**How it will be done.** A `ReportTemplates` table (kind, code, names in both languages, version, design as JSON, preview images, platform-wide or private to one organization) and an organization setting per kind. The PDF, Word, Excel and e-mail renderers, which today have one design written in code, are changed to read the design from the template; the existing design becomes the first template (*Classic*), so nothing changes for an organization that chooses nothing. A *Templates* page in the organization's settings shows the gallery with previews and the colours.

### H4. "Powered by Wabi Insight by WabiTex Solutions FZE" on every output (owner decision, 2026-11-10)

- Every output carries the line **Powered by Wabi Insight by WabiTex Solutions FZE**: the footer of every page of a PDF study and a Word file, the printed footer and the last line of an Excel file, the bottom of every e-mail sent to organization users, and the footer of a printed or shared dashboard.
- In an Arabic output the line is in Arabic, with the names kept in Latin letters: «مدعوم من Wabi Insight من WabiTex Solutions FZE» (owner decision 22).
- It is shown in outputs only (files, e-mails, printed and shared pages), not in the application's own pages (owner decision 22).
- It is part of every template and cannot be removed by an organization or a template, in every plan.
- It is small, in the template's muted colour, and links to the platform's website in e-mails and PDFs.

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
- Business data in the Wabi Hub (G8): installations send operational data only.
- WhatsApp inside an organization's network: Meta offers only its Cloud API (D4).

**Moved to version 3 (owner decision, 2026-11-10).** Asking by e-mail (formerly D3); version 2 keeps asking by WhatsApp (D4) only. A `VERSION_3.md` will be started from the text below when version 3 is planned.

### Ask by e-mail (moved to version 3 by the owner, 2026-11-10)

The requirements worked out for version 2, kept so that version 3 starts from them:

**Goal.** A user sends a question by e-mail and receives the answer by reply, as long as their e-mail address is registered.

**Requirements.**
- **The question address (owner decision 26):** the organization's own address when it has its own mail server (G5, for example `ask@` its domain), otherwise the platform's question address. Either way each user also has the personal question address below. The API reads it with a background worker (IMAP or the mail provider's API) or receives it from the provider's inbound webhook: no new service.
- **Who is asking (the critical part; an e-mail's sender is easy to fake).** A question is accepted only when the message passes the mail system's sender checks (SPF, DKIM, DMARC), the sender is an **active user** of that organization (matched to their sign-in address), and it is sent to the user's **personal question address**, a random address shown on their profile that they can renew (for example `ask+k7f3q@…`). The organization, roles and scopes come from the user record, never from the message.
- **The question:** the subject and the first part of the body; quoted replies, signatures and attachments are ignored; the language comes from the text (H2); the application is the user's default or named in the subject (`[Finance] …`). Request limits and the AI budget apply.
- **The answer:** a reply in the same thread with a short summary and the key figures, the PDF study or Excel attached (with the organization's template, H1 to H4), and a link to the analysis in My History. A reply in the thread is a follow-up question (B3).
- **Safety:** the answer goes **only to the user's own registered address**, whatever is in CC; restricted data follows the e-mail sharing rules (a link that needs sign-in instead of figures); every request is audited and recorded in the access ledger like a typed question; the e-mail's text is a question only, never instructions to the platform (the model still produces a checked plan).
- **Switched on by the Organization Administrator; each user opts in.** Works in every plan.

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
| 11 | Hybrid with the platform database at the organization's location: a dedicated installation per organization in the owner's cloud (proposed, keeps one database per installation), or a shared installation with a separate database per organization (a change to the architecture)? | G4 |
| 12 | Hybrid, files: which stores are supported first (Azure Blob Storage in the organization's subscription, S3-compatible storage, a network share)? | G4 |
| 13 | On-premises licence: contract only, or a licence file the installation checks (organization, expiry, number of users)? | G3 |
| 14 | On-premises updates: how often a release is packaged, how many past releases are supported, and who applies them. | G3 |
| 15 | Active Directory: are roles always given in Wabi Insight (proposed), or may an organization also map Active Directory groups to roles, so users are created at first sign-in? | C11 |
| 16 | Active Directory for Hybrid and Cloud organizations: offer federation (AD FS), or only Microsoft Entra ID for them? | C11 |
| 17 | The organization's own model: is one named model tested and recommended by the owner for Arabic and English, or is any model that passes the fitness check accepted? | G2 |
| 18 | Which plan does the first paying customer need? The order in §8 should follow it. | G1 |


**Decided** (kept here for reference):

| # | Question | Decision (owner, 2026-11-10) |
|---|---|---|
| 19 | An organization's own e-mail server is down | The messages are rescheduled until its server accepts them, never sent through the platform's server; the organization's administrators are notified automatically through the platform's default server (G5). |
| 20 | How an organization gets its own e-mail server | It buys a domain, creates a mailbox on it and obtains its SMTP details; its administrator configures them, or asks the platform team for help through a support ticket (G5). |
| 21 | How long a waiting message may wait | Daily alerts and reports (and more frequent): 12 hours; weekly: 48 hours; less frequent than weekly: 72 hours. A temporary password: 1 hour, then sent through the platform's default server. Other one-off messages: 72 hours. Then given up, logged and reported to the administrators (G5). |
| 22 | The "Powered by" line | As recommended: Arabic wording «مدعوم من Wabi Insight من WabiTex Solutions FZE», in outputs only, not in the application's pages (H4). |
| 23 | Templates | As recommended: organizations choose a template per kind of output and set their colours; no editor; designs are made by the platform team, privately for an organization on request (H3). |
| 10 | Reaching an organization's systems | On-premises: nothing to solve. Cloud and Hybrid: the recommendation of G6 (firewall rule as the minimum, outbound connector as the standard, VPN on request) plus the Wabi Data Gateway (G7) as an option. |
| 24 | Plans and prices | Adopted: On-premises as the lead plan, Cloud second, Hybrid only as a custom project (G1); the business model in §10: paid analysts, each adding 50 calls a month to a pool shared by the organization, free viewers, all amounts in AED (owner, 2026-11-10). |
| 25 | Ask by WhatsApp | The answer is a summary and a personal download link; a dashboard is also sent as a large, high-quality image. Provider: Twilio, as recommended (D4). |
| 26 | Ask by e-mail: the question address | As recommended (the organization's address when it has its own mail server, otherwise the platform's); asking by e-mail was then moved to version 3 with this decision (§6). |

## 8. Proposed order

A proposal, in releases that can each be deployed on their own. Version 2 starts after the version 1 fixes (P13.25–P13.30) are merged. The pull-request numbers continue the two already planned for Microsoft sign-in.

| Release | Content | Pull requests |
|---|---|---|
| **2.0 — Sign-in** | C1, C11 | `V2.1` Microsoft sign-in: backend · `V2.2` Microsoft sign-in: interface and guides (as already planned) · `V2.3` Active Directory sign-in |
| **2.0b — Outputs** | H1, H2, H4, then H3 | `V2.16` logo and name on every output, language from the question, the "Powered by" line · `V2.17` template gallery: storage, renderers read the design, the existing design as *Classic* · `V2.18` template page, previews, more designs |
| **2.1 — Own model and On-premises plan** | G1, G2, G3 | `V2.4` plans recorded; approved private model addresses; fitness check · `V2.5` on-premises package, one-step set-up, disconnected test, diagnostic file, guide |
| **2.2 — Hybrid plan** | G4, G5, G6 | `V2.6` a file store and an e-mail server per organization (G5) · `V2.7` the chosen ways to reach an organization's systems (G6), dedicated installation with the database at the organization's location, network path and guide |
| **2.3 — API data sources** | A1 | `V2.8` registration, catalogue and secrets · `V2.9` executor, limits and row scopes · `V2.10` interface, sample API and guides |
| **2.4 — Drill-down** | B1, then B3 | `V2.11` plan, authorization and history · `V2.12` charts and interface · `V2.13` follow-up questions |
| **2.5 — Publishing** | D1 | `V2.14` connections and publishing · `V2.15` interface and guides |
| **2.6 — Gateway and Hub** | G7, G8 | `V2.19` Wabi Data Gateway (outbound connection, plans compiled at the organization, structured rows, registration) · `V2.20` Wabi Hub (licences with the tier, analysts and viewers, the shared call meter with its warnings, update packages, support tickets, health, with consent) |
| **2.7 — Ask by WhatsApp** | D4 | `V2.21` ask by WhatsApp: number confirmation, Twilio adapter, summary, personal download links, dashboard images |
| **Later** | The *Could* items the owner keeps (A4, A5, B4, B5, D2, E4–E6) | one or two pull requests each |

Reasons for this order:
- **Sign-in first:** Microsoft sign-in is already designed, and an on-premises customer will ask for Active Directory on the first day.
- **The plans before the features:** they decide which organizations can use the product at all; the features that follow are then built once and work in every plan.
- **On-premises before Hybrid:** it is the smaller step (the model address already works for a Platform Administrator, the deployment guides exist) and it is the plan that fully answers the data concern.
- **API sources before drill-down:** drill-down then works on both kinds of source from its first day.
- **Outputs early (2.0b):** every customer sees them in every report and e-mail, they do not depend on the plans, and H3 is easier before more kinds of output (drill-down levels, published reports) exist. Their pull-request numbers (`V2.16`–`V2.18`) follow the others so the numbers already given do not change.

- **Gateway and Hub (2.6):** the gateway is needed as soon as a Cloud customer keeps its databases in its own network; the hub as soon as there are several on-premises installations to support. If the first customer needs one of them, it moves up.

If the first customer needs another plan, the order changes (§7, question 18).

## 9. How each item is accepted

As in version 1, every pull request:

- keeps the invariants of §3 and names any it had to extend;
- has unit tests and SQL Server integration tests with **two organizations**; API sources add a test API server, drill-down adds tests for each level's permissions;
- is checked in the browser in English and Arabic, with real screenshots;
- updates the specification, `ARCHITECTURE.md`, `SECURITY.md`, the deployment guides where a setting or a step changes, and `PROGRESS.md`;
- is verified live on the sample organizations (Gulf Aviation Services, Wabi Bank) where the feature reads data;
- states which plans it applies to, and is checked in each of them: a feature is not done while it works only in the Cloud plan. The On-premises check runs on a server with no internet and a privately served model.

## 10. Business model (adopted, not part of the technical scope)

Adopted by the owner (§7 decision 24). **Pricing model (owner decision, 2026-11-10): analysts with a pooled allowance of calls, and free viewers.**
- **Analysts** are the users who ask questions; the organization pays per analyst.
- **Each analyst adds 50 calls a month to one pool shared by the whole organization.** A heavy user can use what light users leave, so nobody is capped individually and no one has a reason to share an account.
- **Viewers are free and unlimited:** users who only read what others produce (Periodic Reports they receive, analyses shared with them, alerts). They need no paid seat; the calls a Periodic Report uses come from the pool.

This was chosen over two alternatives: calls only (hard for a buyer to estimate) and a fixed allowance per user (unfair to heavy users, and it pushes customers to share logins, which breaks permissions and the audit trail).

All amounts are in **UAE dirhams (AED)**, excluding VAT; they are starting points to be confirmed with the first customers, not market facts.

### Analysts and viewers

| | Analyst (paid) | Viewer (free) |
|---|---|---|
| Asks questions (in the application or by WhatsApp), runs saved analyses again, drafts Monitoring Rules from a description | Yes | No |
| Reads analyses shared with them, receives Periodic Reports and alerts, opens My Reports | Yes | Yes |
| Exports and prints what they can read | Yes | Yes |
| Counts towards the price | Yes | No |

- The **Organization Administrator** decides who is an analyst and can change it at any time (the change counts from the next billing month on Cloud, at the next true-up on-premises). Organization Administrators and Management users are analysts when they ask questions; an administrator who only administers can be a viewer.
- A viewer who tries to ask a question sees a clear message ("Ask your administrator for analyst access"), in English and Arabic.

### What counts as a call

A **call** is one answer the platform produces with the AI, which is what costs (in model use and in source queries) and what gives the value.

| Counted as one call | Not counted |
|---|---|
| A question answered in the application (a new question or a follow-up) | A question refused before planning (out of scope, not permitted) or that failed |
| A saved analysis run again | Opening history, favourites, shared analyses and received reports |
| A Periodic Report delivery: one call per recipient (each recipient's report is produced under their permissions) | Exporting or printing an answer that exists (PDF, Word, Excel) |
| A question asked by WhatsApp (D4) | Monitoring Rules and Alerts (they run without the AI) |
| A Monitoring Rule drafted from a description (it uses the AI) | Administration (users, permissions, catalogue, settings) |

**Best practice applied:**
- **The pool is the organization's**: 50 calls a month per analyst, added together. On-premises it is **a yearly pool** (12 times the monthly figure), which absorbs the busy months (year-end, budget season) without overage; on Cloud it is **a monthly pool**.
- **A usage meter** for the Organization Administrator, by month, application and user, with warnings at **80%** and **100%**.
- **Nobody is stopped mid-task:** after 100%, a grace of 10% continues to work. On Cloud a top-up pack is then needed; on-premises the overage is reported to the Wabi Hub (G8) and settled at renewal or by moving up a tier. Periodic Reports are never cut off mid-run.
- **Unused calls do not roll over** (Cloud: one month's carry-over at most).
- **Counting is local and tamper-evident:** the installation counts its own calls and analysts, and the signed licence file (G8) states the tier (number of analysts and the pool).
- **Typical use, for sizing:** an active business user asks 15 to 30 questions a month, a manager 5 to 10; Periodic Reports add one call per recipient per run.

### On-premises (lead plan: banks, government, large companies)

| Tier | Analysts | Pooled calls a year | Perpetual licence | SMC per year (20%) | Or a yearly licence (support included) |
|---|---|---|---|---|---|
| **Starter** | up to 20 | 12,000 | AED 110,000 | AED 22,000 | AED 45,000 |
| **Business** | up to 100 | 60,000 | AED 275,000 | AED 55,000 | AED 100,000 |
| **Enterprise** | up to 400 | 240,000 | AED 550,000 | AED 110,000 | AED 200,000 |
| **Group** | unlimited, several companies of one group | unlimited | from AED 900,000 | from AED 180,000 | By quotation |

- **Viewers, applications and organizations inside one installation:** unlimited in every tier.
- **More analysts than the tier:** additional analysts at **AED 5,500 each** (licence; 20% a year in SMC), each adding 600 calls a year to the pool, or moving up a tier with the licence difference credited.
- **Extra calls** (above the yearly pool and its grace) without more analysts: packs of 10,000 calls at **AED 25,000**.
- **Setup and implementation:** from **AED 35,000**, plus **AED 11,000 to 18,000 per connected application** (database or API) for its catalogue, descriptions and permissions, plus training days at **AED 5,500** a day. Usually 25 to 40% of the licence.
- **SMC levels:** *Standard* at 20% a year (business hours, response the next working day, all updates); *Premium* at 25% (extended hours, response within 4 hours for severe issues, a named engineer). The SMC starts after a warranty of 3 to 6 months included with the licence. No SMC, no updates.
- **Paid by the customer:** servers, SQL Server licence, AI subscription, domain, e-mail, DNS and certificates. DNS, e-mail and certificate set-up can be sold as a service.
- **First customers:** 40 to 50% off the licence for the first two or three, in exchange for a reference and a case study.

### Cloud (second plan: small and mid-size organizations, trials)

| Analysts | Price per analyst a month | Pooled calls a month | Example |
|---|---|---|---|
| 5 to 24 (minimum 5) | AED 175 | 50 × analysts | 5 analysts: AED 875 a month, 250 calls |
| 25 to 99 | AED 150 | 50 × analysts | 30 analysts: AED 4,500 a month, 1,500 calls |
| 100 and more | AED 125 | 50 × analysts | 120 analysts: AED 15,000 a month, 6,000 calls |

- **Yearly payment:** two months free (ten months' price for twelve).
- **Viewers:** free and unlimited.
- **The AI, the platform's mail server and the file storage are included** (the platform's subscription). An organization that brings its own AI model or mail server (G2, G5) pays the same; the pool still counts calls.
- **Top-up packs:** 500 calls for **AED 1,200**, priced from the measured cost per call (the platform records tokens and cost per answer) with a margin. A call costs the customer about AED 3 to 3.50 inside the pool.
- **WhatsApp (D4):** 1,000 WhatsApp messages a month included for every organization; beyond that, Meta's and Twilio's charges are passed on at cost plus 15%.
- **Trial:** 14 to 30 days, up to 5 analysts and 100 calls, on a demonstration organization.

### Points to price in

- **AI for on-premises customers** is the largest practical cost. A model run entirely on the customer's own hardware that follows the planning rules well in Arabic needs serious GPU servers (well over AED 100,000). Most on-premises buyers in the region accept their own Azure OpenAI or Anthropic subscription in an in-country cloud region: that is the default AI option for the plan, a local model the premium option (G2).
- **Support without access:** the owner cannot sign in to on-premises installations, so support works from the diagnostic file and shared-screen sessions, and several versions are in the field at once. Roughly half a support engineer per 5 to 10 on-premises customers; the SMC price has to cover it.
- **The cost of a call on Cloud** has to be checked every quarter against the usage records (tokens and cost per answer) and the packs repriced when it moves.
- **Counting is new code:** the analyst or viewer setting on each user, the shared call meter, the 80% and 100% warnings and the tier in the licence file. It belongs with the Wabi Hub (G8, release 2.6).

## 11. Change log

| Date | Change |
|---|---|
| 2026-11-10 | First version: owner decisions of 2026-11-06 (Drive and OneDrive), 2026-11-08 (Sign in with Microsoft), 2026-11-09 (drill-down) and 2026-11-10 (API data sources); items carried over from the review of 2026-11-10; proposals. |
| 2026-11-10 | Owner decision: fixes belong to version 1. The 23 items carried over from the review were moved out, to P13.25–P13.30 in the implementation plan; the release "2.0 — Safe base" and the open decision on the audit page went with them. Version 2 keeps the four owner decisions and the proposals. |
| 2026-11-10 | Owner decisions: three service plans chosen per organization, Cloud, On-premises and Hybrid (G1, G3, G4); an organization's own AI model at a private address (G2); sign-in with classic Active Directory without a password (C11). For version 1, organization-managed AI providers are switched off by configuration. Open decisions 10–18 and a new proposed order. |
| 2026-11-10 | Owner decisions: an organization's own e-mail server in every plan, with the platform's as the default (G5); the rule "the organization's own, or the platform's default" stated for e-mail, files, model, sign-in and database (G1); the ways to reach an organization's systems from the owner's cloud, with a recommendation, for the owner to choose from at that step (G6). Open decision 10 rewritten; 19 and 20 added. |
| 2026-11-10 | Owner decisions: decision 19 (an organization's mail server down: messages rescheduled, administrators notified through the platform's server) and 20 (a domain and mailbox of its own, configured by its administrator or with the platform team's help) recorded in G5; decision 10 explained in plain words in G6. New group H: the organization's logo and name on every output (H1), the output's language from the question (H2), a template gallery per kind of output (H3), the "Powered by Wabi Insight by WabiTex Solutions FZE" line (H4), each with how it will be done. Open decisions 21–23. Release 2.0b. |
| 2026-11-10 | Owner decisions 21 (waiting time of digests and Periodic Reports by frequency: 12, 48, 72 hours), 22 (the "Powered by" line as recommended) and 23 (choose templates, no editor). Question 21 stays open for messages that do not repeat. |
| 2026-11-10 | Owner decision 21 completed: a waiting temporary password goes through the platform's default server after one hour; other one-off messages wait 72 hours. |
| 2026-11-10 | Owner decision 10 (On-premises: nothing to solve; Cloud: the G6 recommendation plus the Wabi Data Gateway). New items: Wabi Data Gateway (G7), Wabi Hub (G8, proposed), ask by e-mail (D3), ask by WhatsApp (D4). Plan positioning (G1) and business model (§10) proposed. Invariants: the gateway and the hub named as the two exceptions to "no separate applications". Open decisions 24–26; releases 2.6 and 2.7. |
| 2026-11-10 | Owner decisions 24 (plans and business model adopted; proposed prices added in §10; the Wabi Hub becomes a Must), 25 (WhatsApp: summary, personal download link, dashboards as a high-quality image; Twilio) and 26 (e-mail question address as recommended). |
| 2026-11-10 | Owner decisions: asking by e-mail moved to version 3 (its requirements kept in §6), version 2 keeps WhatsApp only; plans sized by calls instead of users, with what counts as a call and the usual rules (yearly pool on-premises, monthly allowance on Cloud, warnings at 80% and 100%, a 10% grace, top-ups); all prices in AED. |
| 2026-11-10 | Owner decision: pricing by **analysts with a pooled allowance** (50 calls a month per analyst, shared by the organization) and **free viewers**, chosen over calls only and over a fixed allowance per user; on-premises tiers by analysts (20, 100, 400) with the same licence prices; Cloud at AED 175 / 150 / 125 per analyst a month by volume, minimum 5. |
