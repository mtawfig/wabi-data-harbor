# Wabi Insight — Deployment guide

Wabi Insight is three deployable parts: the **Angular build** (static files), the **ASP.NET Core API** (Kestrel) and **SQL Server** (the configuration database). Everything else is configuration: SMTP, the AI provider key, artifact storage, the Data Protection key ring and, optionally, a ClamAV daemon for attachment scanning. Three validated shapes are described here: Docker Compose, a Linux host with systemd + nginx, and Windows with IIS. Pick one; the configuration keys are the same everywhere.

**Step-by-step guides** (every step has a *Check*, *Do* and *Verify* command, from a fresh server to the live site, including SQL Server Express):

- [Ubuntu 24.04 LTS: nginx + systemd + SQL Server Express](DEPLOYMENT_LINUX.md)
- [Windows Server 2019: IIS + ARR + SQL Server Express](DEPLOYMENT_WINDOWS_SERVER_2019.md)

**CI/CD:** after the first setup, deploy from GitHub with one click (*Actions → Deploy (Linux) / Deploy (Windows)*: frontend, backend or both, optional database migration, automatic restore when the new backend isn't healthy). See [DEVOPS_LINUX.md](DEVOPS_LINUX.md) and [DEVOPS_WINDOWS.md](DEVOPS_WINDOWS.md).

**SQL Server Express** 2019, 2022 and 2025 are supported. The full integration suite passes on each. Express limits are 10 GB per database (50 GB on 2025), a 1,410 MB buffer pool and no SQL Server Agent (schedule backups with cron or Task Scheduler); see the guides.

## 1. Before you start

| Need | Notes |
|---|---|
| SQL Server 2019+ (2022 or 2025 recommended; Express is fine) | One database for the platform. Create a login for the API with `db_owner` on that database only (migrations run under it), or run migrations with a stronger login once and give the runtime login `db_datareader`/`db_datawriter` plus `scripts/sql/create-runtime-login.sql` (append-only tables). |
| A public HTTPS name | `Platform:BaseUrl` (e.g. `https://insight.example.com`) is used in every email link. TLS terminates at nginx / IIS / your load balancer; the API itself listens on plain HTTP behind it and emits HSTS when not in Development. |
| SMTP account | Temporary passwords, notifications and shared reports go out through it. Port 465 (implicit TLS) or 587 (STARTTLS). |
| AI provider key | `Ai:Anthropic:ApiKey` (Claude Opus 5.5 by default). Other providers are configured per organization in the UI after bootstrap. |
| Persistent disk | `Storage:RootPath` (reports, exports, attachments, logos — or a private Azure Blob Storage container instead, see [File storage](#file-storage-server-disk-or-azure-blob-storage)) and `DataProtection:KeyRingPath` (the keys that decrypt every stored connection string and provider key). **Losing the key ring makes every encrypted secret unreadable** — back it up with the database. |
| Optional ClamAV | `AttachmentScanning:Provider=ClamAv`, `Host`, `Port` (3310). The scanner fails closed: uploads are refused while clamd is unreachable unless `FailOpen=true`. |

Configuration keys can be given as environment variables with `__` as the section separator (`Email__Password`), through `appsettings.Local.json` next to the API (git-ignored), or with `dotnet user-secrets` in development. Never put real secrets in `appsettings.json` in a public repository.

### File storage: server disk or Azure Blob Storage

Every file the application keeps — study PDFs, stored answers (`analysis-results`), PDF/Word/Excel exports, periodic report files, support attachments and organization logos — goes through one storage component, so the whole set lives in one of two places, chosen by `Storage:Provider` in `appsettings.json`:

| `Storage:Provider` | Where | Settings |
|---|---|---|
| `FileSystem` (default, active) | The server disk, under `Storage:RootPath` (one folder per kind, files `yyyy/MM/<random>.bin`) | `Storage:RootPath` |
| `AzureBlob` | A private Azure Blob Storage container, same names as on the disk | `Storage:AzureBlob:ConnectionString` (secret), `Storage:AzureBlob:ContainerName` (default `wabi-insight-files`), `Storage:AzureBlob:ReadFromDiskWhenMissing` (default `true`) |

`appsettings.json` carries both lines; the Azure one is commented out:

```jsonc
"Provider": "FileSystem",
// "Provider": "AzureBlob",
```

**Switch to Azure Blob Storage:**

1. In the Azure portal, on the storage account: *Configuration* → **Allow Blob anonymous access: Disabled**, **Minimum TLS version: 1.2**; optionally *Networking* → allow only the server's public IP. Then *Security + networking → Access keys* → copy **Connection string** (key1).
2. On the server, put the connection string in `appsettings.Local.json` next to the API (or the environment variable `Storage__AzureBlob__ConnectionString`), never in `appsettings.json`:
   ```json
   { "Storage": { "AzureBlob": { "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=…;AccountKey=…;EndpointSuffix=core.windows.net" } } }
   ```
   (Merge it into the existing file; the Windows guide shows a PowerShell way.)
3. In `appsettings.json` **in the repository**, comment the `"Provider": "FileSystem"` line and uncomment `"Provider": "AzureBlob"`, then deploy (a deployment publishes `appsettings.json` again, so a change made only to the server's copy lasts until the next deployment). For a switch on one server only, set `"Storage": { "Provider": "AzureBlob" }` in that server's `appsettings.Local.json` instead: it overrides `appsettings.json` and no deployment touches it.
4. Run `dotnet WabiInsight.Api.dll storage-check` in the API folder: it writes, reads back and deletes a test file and prints `Storage check passed`, or the reason (HTTP 403: wrong key or network rules; 404: wrong account). It never prints the key.
5. Restart the API. The container is created on first use (private) when the key may create it.
6. Optional, once: `dotnet WabiInsight.Api.dll storage-copy` copies the files already on the disk to the container under the same names (safe to run again; it skips files already there). Until then — and as long as `ReadFromDiskWhenMissing` is `true` — files saved on the disk before the switch still open from the disk. The disk copies are left in place; delete them yourself once the copy is verified.

To switch back, swap the two lines again (files created while on Azure stay in the container; copy them down with `azcopy` if needed). The server needs outbound HTTPS to `<account>.blob.core.windows.net`. With Azure, back up the container with Azure's own protection (soft delete for blobs and containers, versioning, or geo-redundant storage) instead of the disk folder.

### Reverse proxy and client addresses

The API honours `X-Forwarded-For` and `X-Forwarded-Proto` from proxies on the same machine (loopback) only, so per-client rate limits and audit IP addresses see the real visitor behind nginx or IIS + ARR, and a forged header from anyone else is ignored. If the proxy runs on another host, list its address in `ReverseProxy:KnownProxies` (e.g. `ReverseProxy__KnownProxies__0=10.0.0.5`).

### Security settings worth knowing (P12.1)

| Key | Default | Effect |
|---|---|---|
| `Security:ApiRateLimitPerMinute` | 600 | Requests per minute per signed-in principal (per client address when anonymous). |
| `Security:AiRateLimitPerMinute` | 30 | `POST /api/ai/ask` and `/api/ai/plan` per user per minute. |
| `Security:LoginRateLimitPerMinute` / `BootstrapRateLimitPerMinute` | 10 / 3 | Per client address. |
| `Security:MaxRequestBodyBytes` | 1 MB | Default body limit; the logo (2 MB) and ticket attachment (`SupportDesk:MaxAttachmentBytes`, 10 MB) endpoints declare their own. Set the reverse proxy's limit a little higher (12 MB). |
| `Security:SessionIdleMinutes` / `SessionAbsoluteHours` | 30 / 12 | Session cookie lifetime; the cookie is `HttpOnly`, `SameSite=Lax` and `Secure` outside Development. |

The API sets `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, `Cross-Origin-Opener-Policy`, `Cross-Origin-Resource-Policy`, `Cache-Control: no-store` and a `Content-Security-Policy` on every response; the web server sets the policy for the Angular shell (see the nginx and IIS files below).

## 2. Docker Compose (recommended for a single host)

Files: `src/WabiInsight.Api/Dockerfile`, `src/web/Dockerfile` + `src/web/nginx.conf`, `deploy/docker/docker-compose.prod.yml`, `deploy/docker/.env.example`.

```bash
cp deploy/docker/.env.example deploy/docker/.env      # fill in the real values (chmod 600)
docker compose -f deploy/docker/docker-compose.prod.yml build
docker compose -f deploy/docker/docker-compose.prod.yml up -d sqlserver
docker compose -f deploy/docker/docker-compose.prod.yml run --rm api dotnet WabiInsight.Api.dll migrate
# first start only: create the first Platform Administrator
WI_BOOTSTRAP_ENABLED=true WI_BOOTSTRAP_EMAIL=admin@example.com WI_BOOTSTRAP_PASSWORD='…' WI_BOOTSTRAP_SECRET='…' \
  docker compose -f deploy/docker/docker-compose.prod.yml run --rm api dotnet WabiInsight.Api.dll bootstrap
docker compose -f deploy/docker/docker-compose.prod.yml up -d
# with attachment scanning:
docker compose -f deploy/docker/docker-compose.prod.yml --profile scanner up -d
```

The `web` container (nginx) listens on port 8080 and proxies `/api` and `/health` to the API container; put your TLS terminator in front of it (or add the certificate to `src/web/nginx.conf`). Volumes `wabi-artifacts`, `wabi-keys` and `wabi-mssql` hold the state. Health: `GET /health/live` and `/health/ready` on the API.

Both images build from the repository as-is (`docker build -f src/WabiInsight.Api/Dockerfile .` and `docker build -f src/web/Dockerfile src/web`); the CI workflow builds them on every pull request so a broken Dockerfile cannot reach `main`.

## 3. Linux host: systemd + nginx

Full walkthrough: [DEPLOYMENT_LINUX.md](DEPLOYMENT_LINUX.md). Summary:

Files: `deploy/systemd/wabi-insight-api.service`, `deploy/systemd/api.env.example`, `deploy/nginx/wabi-insight.conf`.

```bash
# API
sudo useradd --system --home /opt/wabi-insight --shell /usr/sbin/nologin wabi-insight
sudo mkdir -p /opt/wabi-insight/api /var/lib/wabi-insight/{artifacts,keys} /etc/wabi-insight
dotnet publish src/WabiInsight.Api -c Release -o /opt/wabi-insight/api
sudo cp deploy/systemd/api.env.example /etc/wabi-insight/api.env && sudo chmod 600 /etc/wabi-insight/api.env   # edit the values
sudo chown -R wabi-insight:wabi-insight /opt/wabi-insight /var/lib/wabi-insight
sudo cp deploy/systemd/wabi-insight-api.service /etc/systemd/system/ && sudo systemctl daemon-reload
sudo systemd-run --wait --pipe --uid=wabi-insight --gid=wabi-insight --property=EnvironmentFile=/etc/wabi-insight/api.env --setenv=ASPNETCORE_ENVIRONMENT=Production --working-directory=/opt/wabi-insight/api /usr/bin/dotnet WabiInsight.Api.dll migrate
# bootstrap: same command with a second, temporary EnvironmentFile holding Bootstrap__Enabled=true and the Bootstrap__* values, then `bootstrap`
sudo systemctl enable --now wabi-insight-api

# Web
cd src/web && npm ci && npm run build -- --configuration production
sudo mkdir -p /var/www/wabi-insight && sudo cp -r dist/wabi-insight-web/browser/* /var/www/wabi-insight/
sudo cp deploy/nginx/wabi-insight.conf /etc/nginx/sites-available/wabi-insight && sudo ln -s /etc/nginx/sites-available/wabi-insight /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
```

The unit runs as an unprivileged user with `ProtectSystem=strict` and only `/var/lib/wabi-insight` writable. Kestrel listens on `127.0.0.1:5080`; nginx terminates TLS, serves the Angular files with long-lived caching for hashed assets and `no-cache` for `index.html`, and proxies `/api` with a 600 s read timeout (studies with PDF generation take one to three minutes). Requires the .NET 10 ASP.NET Core runtime, `libfontconfig1` and `libfreetype6` (QuestPDF/SkiaSharp).

## 4. Windows: IIS

Full walkthrough: [DEPLOYMENT_WINDOWS_SERVER_2019.md](DEPLOYMENT_WINDOWS_SERVER_2019.md). Files: `deploy/iis/web.config` (API), `deploy/iis/web.spa.config` (Angular site with the reverse-proxy rules).

1. Install IIS (with Application Initialization), the .NET 10 Hosting Bundle (after IIS), URL Rewrite 2.1 and Application Request Routing 3.0. Enable the ARR proxy with `timeout=00:10:00`, `preserveHostHeader=true`, and allow the server variable `HTTP_X_FORWARDED_PROTO`.
2. API: `dotnet publish src/WabiInsight.Api -c Release -o C:\wabitex-apps\wabi-insight\web\api`, then copy `deploy/iis/web.config` over the generated one. Create the application pool `WabiInsightApi` (No Managed Code, `AlwaysRunning`, idle timeout 0, preload on) and a **site of its own bound to `127.0.0.1:5080`**. Server settings go in `C:\wabitex-apps\wabi-insight\web\api\appsettings.Local.json` (connection string, `Platform:BaseUrl`, `Storage:RootPath` = `D:\wabi-insight\files`, `DataProtection:KeyRingPath` = `C:\wabitex-apps\wabi-insight\keys`), with read access for the pool only.
3. Run `dotnet WabiInsight.Api.dll migrate`, then `bootstrap` with the `Bootstrap__*` environment variables, from that folder.
4. Web: copy `dist/wabi-insight-web/browser` to `C:\wabitex-apps\wabi-insight\web\app` and `deploy/iis/web.spa.config` there as `web.config`. Create the public site `WabiInsight` (pool `WabiInsightWeb`) on 80/443 for your host name. Its rules redirect HTTP to HTTPS (except ACME challenges), proxy `/api` and `/health` to `http://127.0.0.1:5080` with `X-Forwarded-Proto: https`, and fall back to `index.html` for Angular routes.
5. Bind the certificate (win-acme or a `.pfx`) with SNI.

Don't mount the API as an IIS sub-application named `api`. IIS would remove `/api` from the request path (it becomes the path base) and every API route would return 404.

## 5. After every upgrade

1. Stop the API, deploy the new build, run `dotnet WabiInsight.Api.dll migrate`, start the API (the API refuses to start the web host when the database is behind the migrations only if you run the command; the `/health/ready` check reports database reachability).
2. Deploy the new Angular build (hashed file names make the switch atomic; `index.html` is served with `no-cache`).
3. Check `/health/ready`, sign in, open *Platform → System Health*.

## 6. Operations checklist

- **Backups:** the database and `Storage:RootPath` (or, with `Storage:Provider=AzureBlob`, the container's soft delete / versioning) and `DataProtection:KeyRingPath` together — a restore without the key ring cannot decrypt connection strings or provider keys.
- **Logs:** the API writes to stdout (journald / Docker logs / IIS stdout when enabled); `Api.Error` usage events and the *System Health* page show error trends without exception text.
- **Email:** the outbox (`EmailMessages`) records every queued message with its status; failed deliveries are retried `Email:MaxAttempts` times. Attachments (shared reports) go through MimeKit with the SMTP account's size limit.
- **Rate limits:** 429 responses carry `Retry-After`; raise `Security:ApiRateLimitPerMinute` for automation clients.
- **Attachment scanning:** with `AttachmentScanning:Provider=ClamAv`, keep `freshclam` running in the ClamAV container or host; the API logs `scanner_unavailable` rejections when clamd is down.
- **Load check:** `node scripts/perf/load-check.js --base https://insight.example.com --users 20 --logins 5 --seconds 30` with a test user reproduces the numbers in `docs/PERFORMANCE.md` (set `Security__ApiRateLimitPerMinute` high for the run or the per-principal budget will throttle it).
