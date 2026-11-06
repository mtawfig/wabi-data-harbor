# Wabi Insight — Deployment guide

Wabi Insight is three deployable parts: the **Angular build** (static files), the **ASP.NET Core API** (Kestrel) and **SQL Server** (the configuration database). Everything else is configuration: SMTP, the AI provider key, artifact storage, the Data Protection key ring and, optionally, a ClamAV daemon for attachment scanning. Three validated shapes are described here: Docker Compose, a Linux host with systemd + nginx, and Windows with IIS. Pick one; the configuration keys are the same everywhere.

## 1. Before you start

| Need | Notes |
|---|---|
| SQL Server 2019+ (2022 recommended) | One database for the platform. Create a login for the API with `db_owner` on that database only (migrations run under it), or run migrations with a stronger login once and give the runtime login `db_datareader`/`db_datawriter` plus `scripts/sql/create-runtime-login.sql` (append-only tables). |
| A public HTTPS name | `Platform:BaseUrl` (e.g. `https://insight.example.com`) is used in every email link. TLS terminates at nginx / IIS / your load balancer; the API itself listens on plain HTTP behind it and emits HSTS when not in Development. |
| SMTP account | Temporary passwords, notifications and shared reports go out through it. Port 465 (implicit TLS) or 587 (STARTTLS). |
| AI provider key | `Ai:Anthropic:ApiKey` (Claude Opus 5.5 by default). Other providers are configured per organization in the UI after bootstrap. |
| Persistent disk | `Storage:RootPath` (reports, exports, attachments, logos) and `DataProtection:KeyRingPath` (the keys that decrypt every stored connection string and provider key). **Losing the key ring makes every encrypted secret unreadable** — back it up with the database. |
| Optional ClamAV | `AttachmentScanning:Provider=ClamAv`, `Host`, `Port` (3310). The scanner fails closed: uploads are refused while clamd is unreachable unless `FailOpen=true`. |

Configuration keys can be given as environment variables with `__` as the section separator (`Email__Password`), through `appsettings.Local.json` next to the API (git-ignored), or with `dotnet user-secrets` in development. Never put real secrets in `appsettings.json` in a public repository.

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

Files: `deploy/systemd/wabi-insight-api.service`, `deploy/systemd/api.env.example`, `deploy/nginx/wabi-insight.conf`.

```bash
# API
sudo useradd --system --home /opt/wabi-insight --shell /usr/sbin/nologin wabi-insight
sudo mkdir -p /opt/wabi-insight/api /var/lib/wabi-insight/{artifacts,keys} /etc/wabi-insight
dotnet publish src/WabiInsight.Api -c Release -o /opt/wabi-insight/api
sudo cp deploy/systemd/api.env.example /etc/wabi-insight/api.env && sudo chmod 600 /etc/wabi-insight/api.env   # edit the values
sudo chown -R wabi-insight:wabi-insight /opt/wabi-insight /var/lib/wabi-insight
sudo cp deploy/systemd/wabi-insight-api.service /etc/systemd/system/ && sudo systemctl daemon-reload
sudo -u wabi-insight env $(grep -v '^#' /etc/wabi-insight/api.env | xargs) dotnet /opt/wabi-insight/api/WabiInsight.Api.dll migrate
sudo -u wabi-insight env $(grep -v '^#' /etc/wabi-insight/api.env | xargs) Bootstrap__Enabled=true Bootstrap__AdministratorEmail=… Bootstrap__AdministratorPassword=… Bootstrap__InitializationSecret=… dotnet /opt/wabi-insight/api/WabiInsight.Api.dll bootstrap
sudo systemctl enable --now wabi-insight-api

# Web
cd src/web && npm ci && npm run build -- --configuration production
sudo mkdir -p /var/www/wabi-insight && sudo cp -r dist/wabi-insight-web/browser/* /var/www/wabi-insight/
sudo cp deploy/nginx/wabi-insight.conf /etc/nginx/sites-available/wabi-insight && sudo ln -s /etc/nginx/sites-available/wabi-insight /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
```

The unit runs as an unprivileged user with `ProtectSystem=strict` and only `/var/lib/wabi-insight` writable. Kestrel listens on `127.0.0.1:5080`; nginx terminates TLS, serves the Angular files with long-lived caching for hashed assets and `no-cache` for `index.html`, and proxies `/api` with a 600 s read timeout (studies with PDF generation take one to three minutes). Requires the .NET 10 ASP.NET Core runtime, `libfontconfig1` and `libfreetype6` (QuestPDF/SkiaSharp).

## 4. Windows: IIS

Files: `deploy/iis/web.config` (API), `deploy/iis/web.spa.config` (Angular site).

1. Install the .NET 10 Hosting Bundle and the IIS URL Rewrite module; restart IIS.
2. API: `dotnet publish src/WabiInsight.Api -c Release -o C:\inetpub\wabi-insight\api`, copy `deploy/iis/web.config` there, create an application pool (No Managed Code, identity with read access to the folder and modify access to the artifacts and key-ring folders) and a site or application bound to it. Set the configuration as environment variables on the application pool (`Email__Password`, `ConnectionStrings__ConfigurationDatabase`, `Storage__RootPath=D:\wabi-insight\artifacts`, `DataProtection__KeyRingPath=D:\wabi-insight\keys`, …) or in `appsettings.Local.json` with restricted ACLs.
3. Run migrations and the bootstrap once from an elevated prompt in that folder: `dotnet WabiInsight.Api.dll migrate`, then `dotnet WabiInsight.Api.dll bootstrap` with the `Bootstrap__*` variables set.
4. Web: build with `npm run build -- --configuration production`, copy `dist/wabi-insight-web/browser` to `C:\inetpub\wabi-insight\web`, copy `deploy/iis/web.spa.config` there as `web.config`, and mount the API as an application named `api` under the same site (so `/api/...` and the session cookie stay first-party) or proxy it with ARR.
5. Bind HTTPS with your certificate and enable HSTS on the site.

## 5. After every upgrade

1. Stop the API, deploy the new build, run `dotnet WabiInsight.Api.dll migrate`, start the API (the API refuses to start the web host when the database is behind the migrations only if you run the command; the `/health/ready` check reports database reachability).
2. Deploy the new Angular build (hashed file names make the switch atomic; `index.html` is served with `no-cache`).
3. Check `/health/ready`, sign in, open *Platform → System Health*.

## 6. Operations checklist

- **Backups:** the database and `Storage:RootPath` and `DataProtection:KeyRingPath` together — a restore without the key ring cannot decrypt connection strings or provider keys.
- **Logs:** the API writes to stdout (journald / Docker logs / IIS stdout when enabled); `Api.Error` usage events and the *System Health* page show error trends without exception text.
- **Email:** the outbox (`EmailMessages`) records every queued message with its status; failed deliveries are retried `Email:MaxAttempts` times. Attachments (shared reports) go through MimeKit with the SMTP account's size limit.
- **Rate limits:** 429 responses carry `Retry-After`; raise `Security:ApiRateLimitPerMinute` for automation clients.
- **Attachment scanning:** with `AttachmentScanning:Provider=ClamAv`, keep `freshclam` running in the ClamAV container or host; the API logs `scanner_unavailable` rejections when clamd is down.
- **Load check:** `node scripts/perf/load-check.js --base https://insight.example.com --users 20 --logins 5 --seconds 30` with a test user reproduces the numbers in `docs/PERFORMANCE.md` (set `Security__ApiRateLimitPerMinute` high for the run or the per-principal budget will throttle it).
