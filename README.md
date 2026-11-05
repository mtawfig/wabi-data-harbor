# Wabi Insight

**Ask Your Data. Trust the Answer.** · اسأل بياناتك. وثق بالإجابة.

Wabi Insight (platform code AP-AIR) is a secure, bilingual (English/Arabic), multi-organization enterprise AI intelligence platform. Employees ask natural-language questions about the enterprise data they are authorized to see, and receive verified results, charts and reports they can share under control. The company operating the platform has a separate administration and support environment with no access to customer business data.

## Documentation

- [Product specification](docs/WABI_INSIGHT_SPECIFICATION.md)
- [Review of the original prompt and owner decisions](docs/SPEC_REVIEW.md)
- [Implementation plan: phases and pull requests](docs/IMPLEMENTATION_PLAN.md)
- [Progress log](docs/PROGRESS.md)
- [Guide for Claude Code sessions](CLAUDE.md)

## Local configuration

Copy `src/WabiInsight.Api/appsettings.Local.example.json` to `appsettings.Local.json` (git-ignored) and set your SQL Server connection string and bootstrap values there. Never commit real credentials to `appsettings.json`.

## Status

Phase 0 (planning) complete. Application code starts with PR P1.1.
