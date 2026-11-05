# Wabi Insight — Architecture

Status: skeleton written in P1.1; each phase extends the relevant section.

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

## Request pipeline (P1.1)

`UseExceptionHandler` (RFC 9457 problem details) → `UseStatusCodePages` → HSTS (non-development) → HTTPS redirection → health endpoints (`/health/live`, `/health/ready`) → controllers.

Authentication, antiforgery, tenant context resolution, rate limiting and authorization middleware are added in P1.3.

## Endpoints (P1.1)

| Endpoint | Purpose |
|---|---|
| `GET /health/live` | Process liveness; JSON with per-check status. |
| `GET /health/ready` | Readiness; later phases add database, storage, email and AI provider checks. |
| `GET /api/platform/info` | Public product identity (name, code, tagline, version, languages). Never exposes configuration or bootstrap state. |

## Angular (P1.1)

Standalone components with signals, `provideHttpClient(withFetch())`, `proxy.conf.json` forwarding `/api` and `/health` to the API on port 5080 during `ng serve`. Vitest unit tests, angular-eslint. The design system, Material theme, Transloco i18n and layout shells arrive in P1.4.

## Tenant model, security, data model

See `docs/WABI_INSIGHT_SPECIFICATION.md` §4, §10, §27 and `docs/SECURITY.md` (written in P1.3).
