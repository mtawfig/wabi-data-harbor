# Wabi Insight

**Ask Your Data. Trust the Answer.** · اسأل بياناتك. وثق بالإجابة.

Wabi Insight (platform code AP-AIR) is a secure, bilingual (English/Arabic), multi-organization enterprise AI intelligence platform. Employees ask natural-language questions about the enterprise data they are authorized to see, and receive verified results, charts and reports they can share under control. The company operating the platform has a separate administration and support environment with no access to customer business data.

## Documentation

- [Product specification](docs/WABI_INSIGHT_SPECIFICATION.md)
- [Review of the original prompt and owner decisions](docs/SPEC_REVIEW.md)
- [Implementation plan: phases and pull requests](docs/IMPLEMENTATION_PLAN.md)
- [Version 2: scope and requirements (planned, not built)](docs/VERSION_2.md)
- [Progress log](docs/PROGRESS.md)
- [Proactive agent design (Phase 14): Monitoring Rules, Alerts, Periodic Reports](docs/PROACTIVE_AGENT.md)
- [Deployment guide (Docker, Linux, IIS, Azure)](docs/DEPLOYMENT.md) · step by step for beginners: [Windows Server 2019 + SQL Server Express](docs/DEPLOYMENT_WINDOWS_SERVER_2019.md) · [Windows Server 2019 VPS + SQL Server Express, files in Azure Blob Storage](docs/DEPLOYMENT_WINDOWS_AZURE.md) · [Ubuntu with Docker](docs/DEPLOYMENT_LINUX_DOCKER.md) · [Ubuntu 24.04](docs/DEPLOYMENT_LINUX.md) · [Azure App Service](docs/DEPLOYMENT_AZURE.md)
- CI/CD (deploy from GitHub Actions with one click: Deploy (Windows), Deploy (Linux), Deploy (Azure)): [Windows](docs/DEVOPS_WINDOWS.md) · [Linux](docs/DEVOPS_LINUX.md) · [Azure](docs/DEPLOYMENT_AZURE.md)
- Build, test and run commands for developers: [CLAUDE.md, *Essential commands*](CLAUDE.md#essential-commands)
- [Security](docs/SECURITY.md) · [Architecture](docs/ARCHITECTURE.md) · [Design system](docs/DESIGN_SYSTEM.md) · [Screens](docs/SCREENSHOTS.md) · [Performance](docs/PERFORMANCE.md)
- [Guide for Claude Code sessions](CLAUDE.md)

## Local configuration

`appsettings.json` carries the configuration-database connection string and the SMTP settings (owner decision for this private repository). To override anything per machine (a local SQL Server, the File email sink, bootstrap values), copy `src/WabiInsight.Api/appsettings.Local.example.json` to `appsettings.Local.json` (git-ignored). Apply migrations to the configured database with `dotnet run --project src/WabiInsight.Api -- migrate` before the first run.

Sample data: `scripts/sql/samples` creates three realistic source databases (flight operations, HR/payroll/attendance, procurement/stores) and seeds a ready-to-use organization with an administrator, a management user and two role-scoped users into `WabiInsightDB`; see its [README](scripts/sql/samples/README.md). A second sample organization, the fictional **Wabi Bank** (Personnel, Core Banking, Finance, Digital Banking and IT Help Desk databases, eight role-scoped users, Monitoring Rules on planted findings), is in [`scripts/sql/samples/wabi-bank`](scripts/sql/samples/wabi-bank/README.md).

## Product walkthrough

A narrated walkthrough of the application on the Wabi Bank sample, with subtitles and chapters, in English ([`docs/demo/wabi-insight-walkthrough-en.mp4`](docs/demo/wabi-insight-walkthrough-en.mp4), script [`…-en.md`](docs/demo/wabi-insight-walkthrough-en.md)) and Arabic ([`docs/demo/wabi-insight-walkthrough-ar.mp4`](docs/demo/wabi-insight-walkthrough-ar.mp4), script [`…-ar.md`](docs/demo/wabi-insight-walkthrough-ar.md)); generator in [`scripts/demo/walkthrough`](scripts/demo/walkthrough/README.md).

A walkthrough of the **deployed site**, with the sample organization GAS and the company's contact card at the end, in Arabic ([`docs/demo/wabi-insight-live-ar.mp4`](docs/demo/wabi-insight-live-ar.mp4), about ten minutes, script [`…-ar.md`](docs/demo/wabi-insight-live-ar.md)) and English ([`docs/demo/wabi-insight-live-en.mp4`](docs/demo/wabi-insight-live-en.mp4), about seven minutes, script [`…-en.md`](docs/demo/wabi-insight-live-en.md)); generator in [`scripts/demo/live-walkthrough`](scripts/demo/live-walkthrough/README.md).

## Current version

Version 1, as of 14 November 2026 (latest pull request: P13.50). The most recent additions, newest first:

| Pull request | What it added |
|---|---|
| P13.50 | **Long lists.** A table shows its first 100 rows on the page, in PDF and in Word; a longer list says *First 100 rows of N* and offers **View all in Excel**, which reads every row again from the source under the permissions of the person exporting. The count and totals of a long list are calculated by the database over the whole list. |
| P13.49 | **No "partial list" wording.** Answers, reports and exported documents no longer tell the reader that a list is partial. A "top five" is a complete answer; a list without a requested number is fetched up to 1,000 rows (was 100); a list that still stops there carries no totals. |
| P13.48 | **AI usage ledger.** Every request sent to a model is recorded once, where it leaves the application, in the append-only table `AiCalls`: tokens, input, output and total cost, the prices used and what the call was for. The month's AI spending is the sum of that table, so rule drafts and provider tests count as well as questions. The *AI Spending* page shows input and output tokens per employee. Migration `AddAiCalls`. |
| P13.47 | **AI spending for the Organization Administrator.** Ordinary users see one short line when the monthly AI limit is reached, with no amounts. The organization's administrators are alerted by e-mail and in the application at 80% of the limit and at the limit, once each per month, and get an *AI Spending* page: spending and requests per employee with filters, and the same month by month. |
| P13.46 | **Plans with a monthly AI spending limit.** The Platform Administrator defines plans and gives one to each organization; when the month's AI spending reaches the plan's limit, new AI questions are refused until the next month or a plan change, and saved answers stay available. The Ask page warns from 80% of the limit. |
| P13.45 | English walkthrough video of the deployed site. |
| P13.44 | Arabic walkthrough video recorded again with the Arabic logo. |
| P13.43 | Phone layout: every list table is shown as cards on a phone; Organization Insights readable again on servers whose application login is a member of `db_owner`. |
| P13.42 | The log file is written by its own thread. |
| P13.41 | Arabic logo, shown according to the interface language. |

A deployment of this version needs **Apply database migrations** (migrations `AddServicePlans` and `AddAiSpendingNotices`). Existing organizations start with no plan, so nothing is limited until a plan is chosen for them. The full history is in [`docs/PROGRESS.md`](docs/PROGRESS.md).

## Status

Version 1 feature-complete: phases 1–13 of the implementation plan are merged (see `docs/PROGRESS.md`). Phase 14, the proactive agent (design in `docs/PROACTIVE_AGENT.md`), is merged too (P14.1–P14.5): the access ledger and scheduler, Monitoring Rules and Alerts, Periodic Reports, and natural-language and system rules. A recorded demo of the agent on the sample organization is in `docs/demo/p14-5-proactive-agent-alert.gif` (how to run it: `scripts/demo/proactive-agent/README.md`). Google Drive / OneDrive publishing (P10.2) is deferred to the next version by owner decision. CI runs the .NET and Angular suites, the dependency audits and the container image builds on every pull request; deployments are started by hand from the Actions tab (*Deploy (Linux)* / *Deploy (Windows)*: frontend, backend or both, with or without database migration).
