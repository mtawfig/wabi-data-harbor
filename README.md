# Wabi Insight

**Ask Your Data. Trust the Answer.** · اسأل بياناتك. وثق بالإجابة.

Wabi Insight (platform code AP-AIR) is a secure, bilingual (English/Arabic), multi-organization enterprise AI intelligence platform. Employees ask natural-language questions about the enterprise data they are authorized to see, and receive verified results, charts and reports they can share under control. The company operating the platform has a separate administration and support environment with no access to customer business data.

## Documentation

- [Product specification](docs/WABI_INSIGHT_SPECIFICATION.md)
- [Review of the original prompt and owner decisions](docs/SPEC_REVIEW.md)
- [Implementation plan: phases and pull requests](docs/IMPLEMENTATION_PLAN.md)
- [Progress log](docs/PROGRESS.md)
- [Deployment guide (Docker, Linux, IIS)](docs/DEPLOYMENT.md) · step by step: [Windows Server 2019](docs/DEPLOYMENT_WINDOWS_SERVER_2019.md) · [Ubuntu 24.04](docs/DEPLOYMENT_LINUX.md)
- [Security](docs/SECURITY.md) · [Architecture](docs/ARCHITECTURE.md) · [Design system](docs/DESIGN_SYSTEM.md) · [Screens](docs/SCREENSHOTS.md) · [Performance](docs/PERFORMANCE.md)
- [Guide for Claude Code sessions](CLAUDE.md)

## Local configuration

`appsettings.json` carries the configuration-database connection string and the SMTP settings (owner decision for this private repository; the SMTP password is a placeholder). To override anything per machine (a local SQL Server, the File email sink, bootstrap values), copy `src/WabiInsight.Api/appsettings.Local.example.json` to `appsettings.Local.json` (git-ignored). Apply migrations to the configured database with `dotnet run --project src/WabiInsight.Api -- migrate` before the first run.

Sample data: `scripts/sql/samples` creates three realistic source databases (flight operations, HR/payroll/attendance, procurement/stores) and seeds a ready-to-use organization with an administrator, a management user and two role-scoped users into `WabiInsightDB`; see its [README](scripts/sql/samples/README.md).

## Status

Version 1 feature-complete: phases 1–12 of the implementation plan are merged, plus sample data (P13.1) (see `docs/PROGRESS.md`). Google Drive / OneDrive publishing (P10.2) is deferred to the next version by owner decision. CI runs the .NET and Angular suites, the dependency audits and the container image builds on every pull request.
