# Wabi Insight — Performance notes (P12.2)

## Method

`scripts/perf/load-check.js` signs in a handful of sessions (sign-ins are rate limited per address) and keeps a fixed number of virtual users firing the hot read endpoints round-robin for a fixed duration, then prints throughput and latency percentiles per endpoint. It uses only Node 22's built-in `fetch`, never touches the AI endpoints (they cost money and seconds) and exits non-zero if any response is not 2xx.

```bash
node scripts/perf/load-check.js --base http://localhost:5080 --users 20 --logins 5 --seconds 30
```

For a measurement run, lift the per-principal budget (`Security__ApiRateLimitPerMinute=1000000`), otherwise the global limiter correctly answers 429 after 600 requests per principal per minute — which the first run demonstrated.

## Environment

Sandbox container shared with SQL Server 2022 (Developer) on the same host, API in Release configuration (`dotnet run -c Release --no-build`), the demo organization (ACME Logistics: one approved SQL Server application with three approved objects, four users, four saved analyses, ten report files, one share). Numbers are indicative of relative cost, not of production capacity.

## Results (20 virtual users, 30 s, idle machine, after the P12.2 fixes)

| Endpoint | Requests | Errors | req/s | p50 ms | p95 ms | p99 ms | max ms |
|---|---|---|---|---|---|---|---|
| `/api/auth/me` | 3473 | 0 | 115.8 | 4.8 | 12.0 | 17.1 | 29.3 |
| `/api/notifications/unread-count` | 3475 | 0 | 115.8 | 8.6 | 18.1 | 23.8 | 43.5 |
| `/api/history?pageSize=25` | 3477 | 0 | 115.9 | 13.6 | 25.7 | 33.0 | 53.8 |
| `/api/history/reports?pageSize=25` | 3480 | 0 | 116.0 | 13.6 | 24.9 | 33.7 | 57.9 |
| `/api/shares/with-me` | 3483 | 0 | 116.1 | 13.5 | 25.0 | 33.0 | 44.2 |
| `/api/ai/applications` | 3486 | 0 | 116.2 | 70.6 | 99.5 | 116.4 | 150.0 |
| `/api/permissions/me` | 3477 | 0 | 115.9 | 40.4 | 61.6 | 73.2 | 117.4 |

811 requests/second overall with every response 2xx.

## What the first run showed and what changed

1. **`/api/ai/applications` was the slowest endpoint by far** (p50 ≈ 280 ms in the first measurement, about four times the cost of `/api/permissions/me`): it built a complete planner snapshot — permission resolution plus metadata, columns, relationships and row-scope mappings — for *every* approved application just to count the objects the user may query. It now asks the planner for permitted-object counts once (`IQueryPlanner.CountPermittedObjectsAsync`: one permission resolution, grouped by application; the built-in Insights application keeps its own light snapshot). After the change the endpoint costs the permission resolution plus one application query (p50 ≈ 1.7 × `/api/permissions/me`).
2. **`PermissionResolver`** (the heart of every ask, of the permissions preview and of the list above) issued twelve sequential queries and then scanned the column, table-grant, column-grant and row-grant lists once per object. It now loads the user together with its department and section codes in one projection, the active roles through one join, and indexes the grants by object (`ToLookup` / dictionaries), so the per-object work is O(1) lookups. Semantics are unchanged (the permission and analysis test suites pass).
3. **Index review**: every hot query is covered by an existing index — `AnalysisHistories (OrganizationId, OwnerUserId, IsDeleted, IsArchived, UpdatedAt)`, `Notifications (RecipientType, RecipientId, ReadAt, CreatedAt)`, `AnalysisShares (OrganizationId, RecipientUserId, RevokedAt, ExpiresAt)`, `AnalysisArtifacts (OrganizationId, HistoryId)`, `AuditLogs` / `UsageEvents (OrganizationId, CreatedAt)` and `(EventType, CreatedAt)`, `EmailMessages (Status, CreatedAt)`, `SupportTickets (OrganizationId, Status)` and `(OrganizationId, CreatedByUserId)`, `OrganizationUsers (OrganizationId, Status)`. No migration was needed.

## Not measured here

Analysis runs (`/api/ai/ask`) are dominated by the model: a ten-query dashboard takes about 50 s and a study with PDF two to three minutes against Claude Opus 5.5 (P8.3 notes); the read-only source queries run with the application's command timeout and the row caps described in `docs/ARCHITECTURE.md`. Report rendering (QuestPDF) and exports are CPU-bound per request and run inside the request; a 10-page study renders in about a second.
