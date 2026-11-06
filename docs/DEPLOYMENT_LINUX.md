# Wabi Insight — Linux deployment, step by step (Ubuntu 24.04 LTS)

This guide takes a fresh Ubuntu 24.04 LTS server to a live Wabi Insight at `https://<your domain>`. Every step has three parts:

- **Check:** a command that tells you whether the step is already done. If it is, skip **Do**.
- **Do:** the commands that make the change.
- **Verify:** a command whose output proves the step worked. Don't continue until it does.

The result:

```
 browser ──HTTPS 443──► nginx ──┬─ /            Angular files (/var/www/wabi-insight)
                                └─ /api, /health ─► Kestrel 127.0.0.1:5080 (systemd: wabi-insight-api)
                                                        │
                                                        └─► SQL Server Express (localhost:1433, WabiInsightDB)
```

The Windows equivalent is [DEPLOYMENT_WINDOWS_SERVER_2019.md](DEPLOYMENT_WINDOWS_SERVER_2019.md). The Docker Compose option and a summary of all configuration keys are in [DEPLOYMENT.md](DEPLOYMENT.md).

## SQL Server Express works

Wabi Insight runs on **SQL Server Express 2019, 2022 and 2025**. The full integration suite (120 tests: migrations, multi-tenant isolation, row scopes, the support desk, the sample-database scripts) passes on each of these Express versions. The app uses no Standard- or Enterprise-only feature and needs no SQL Server Agent. Express limits you should know:

| Limit | Express 2019 / 2022 | Express 2025 | What it means here |
|---|---|---|---|
| Database size | 10 GB per database | 50 GB per database | `WabiInsightDB` is about 5 MB after setup and grows with history, audit and usage events, so several years fit easily. The three sample databases are under 50 MB each. Every database has its own limit. |
| Memory | 1,410 MB buffer pool | 1,410 MB buffer pool | Enough for the configuration database. Large source databases are better on Standard. |
| CPU | 1 socket or 4 cores (whichever is lower) | same | Fine for the API's short queries. |
| SQL Server Agent | not included | not included | Backups are scheduled with cron instead (step 21). |
| Backup compression | not supported | not supported | Backups run without `COMPRESSION`. |

**Install SQL Server 2025 Express** (step 6) unless you already have a SQL Server. If your SQL Server Express is on another machine (for example the server in `appsettings.json`), skip step 6 and run the step 7 commands against that server.

## 0. Before you start

| You need | Example | Notes |
|---|---|---|
| Ubuntu 24.04 LTS server, x64 | 4 vCPU, 8 GB RAM, 60 GB disk | Minimum is 2 vCPU / 4 GB with swap, since building the Angular app and running SQL Server need memory. |
| A DNS name pointing at the server | `insight.example.com` → server public IP | Needed for the Let's Encrypt certificate and for email links. |
| SSH access with sudo | | |
| Access to the GitHub repository | a personal access token with *Contents: read* | The repository is private. |
| Outbound internet | apt, npm, NuGet, GitHub, `api.anthropic.com:443`, your SMTP host on 587/465 | |

Open a root shell and keep using it, because the variables below live in that shell:

```bash
sudo -i
```

Set the values for your installation. Save them so you can `source` them again in a new shell:

```bash
cat > /root/wabi-insight.vars <<'EOF'
export WI_DOMAIN="insight.example.com"          # public DNS name of the site
export WI_CERT_EMAIL="it@example.com"           # Let's Encrypt expiry notices
export WI_ADMIN_EMAIL="support@wabitex.com"     # first Platform Administrator (a real mailbox)
export WI_REPO="https://github.com/mtawfig/wabi-data-harbor.git"
export WI_BRANCH="main"
EOF
chmod 600 /root/wabi-insight.vars
nano /root/wabi-insight.vars        # edit the values
source /root/wabi-insight.vars
```

**Verify:**

```bash
echo "$WI_DOMAIN / $WI_ADMIN_EMAIL / $WI_BRANCH"     # your values, not the examples
```

## 1. Check the server

```bash
lsb_release -ds            # Ubuntu 24.04.x LTS
uname -m                   # x86_64
nproc; free -h; df -h /    # CPUs, memory, free disk (>= 20 GB free)
```

**Verify** that the DNS name points at this server. Both commands must print the same address:

```bash
getent ahostsv4 "$WI_DOMAIN" | awk 'NR==1{print $1}'
curl -4 -s https://ifconfig.me; echo
```

## 2. Update the system and install base packages

**Check:**

```bash
apt list --upgradable 2>/dev/null | tail -n +2 | wc -l          # pending updates
dpkg -s curl gnupg ca-certificates git rsync unzip libfontconfig1 libfreetype6 2>/dev/null | grep -c '^Status: install ok installed'   # 8 = all present
```

**Do:**

```bash
apt-get update && apt-get -y full-upgrade
apt-get install -y curl gnupg ca-certificates git rsync unzip libfontconfig1 libfreetype6
[ -f /var/run/reboot-required ] && echo "REBOOT REQUIRED: run 'reboot', reconnect, sudo -i, source /root/wabi-insight.vars"
```

`libfontconfig1` and `libfreetype6` are needed by the PDF renderer (QuestPDF/SkiaSharp).

**Verify:**

```bash
dpkg -s curl gnupg ca-certificates git rsync unzip libfontconfig1 libfreetype6 | grep -c '^Status: install ok installed'   # 8
git --version
```

## 3. Time zone and clock

The application stores UTC. The server clock must be correct for TLS, cookies and support-access expiry.

**Check:**

```bash
timedatectl | grep -E 'Time zone|synchronized|NTP service'
```

**Do** (only if the clock isn't synchronized, or you want local time in logs):

```bash
timedatectl set-timezone Asia/Dubai        # optional, any IANA zone
timedatectl set-ntp true
```

**Verify:** `timedatectl | grep 'System clock synchronized: yes'`

## 4. Swap (servers with less than 8 GB RAM)

**Check:** `swapon --show` (no output means no swap).

**Do:**

```bash
fallocate -l 4G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

**Verify:** `swapon --show` lists `/swapfile` with size 4G.

## 5. Firewall

Only SSH, HTTP and HTTPS are open. SQL Server (1433) and Kestrel (5080) stay private.

**Check:** `ufw status verbose`

**Do:**

```bash
ufw allow OpenSSH          # first, so you don't lock yourself out
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable
```

**Verify:**

```bash
ufw status | grep -E '^(OpenSSH|80/tcp|443/tcp) +ALLOW'      # three lines
ufw status | grep -c 1433                                    # 0: SQL Server not exposed
```

## 6. SQL Server 2025 Express

Skip this step if the database runs on another server.

### 6.1 Microsoft package repositories

**Check:**

```bash
ls /etc/apt/sources.list.d/ | grep -E 'mssql-server-2025|mssql-release|prod'
```

**Do:**

```bash
curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg
curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/mssql-server-2025.list -o /etc/apt/sources.list.d/mssql-server-2025.list
curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/prod.list -o /etc/apt/sources.list.d/mssql-release.list
apt-get update
```

**Verify:**

```bash
apt-cache policy mssql-server | grep Candidate        # 17.0.x
apt-cache policy mssql-tools18 | grep Candidate       # 18.x
```

### 6.2 Install the engine

**Check:** `dpkg -s mssql-server 2>/dev/null | grep -E '^(Status|Version)'`

**Do:**

```bash
apt-get install -y mssql-server
```

**Verify:** `dpkg -s mssql-server | grep '^Version: 17\.'`

### 6.3 Configure it as Express and set the `sa` password

**Check:** this prints `configured` once setup has run:

```bash
systemctl is-active mssql-server && [ -f /var/opt/mssql/mssql.conf ] && echo configured
```

**Do:** choose a strong `sa` password: at least 12 characters with upper case, lower case, a digit and a symbol. Don't use `"`, `'`, `\` or `$`.

```bash
read -rsp 'New sa password: ' SA_PASSWORD; echo
MSSQL_PID=Express MSSQL_SA_PASSWORD="$SA_PASSWORD" ACCEPT_EULA=Y /opt/mssql/bin/mssql-conf -n setup
systemctl enable --now mssql-server
```

Keep the `sa` password in your password manager. The application never uses it.

**Verify:**

```bash
systemctl is-active mssql-server          # active
systemctl is-enabled mssql-server         # enabled
ss -ltnp | grep ':1433 '                  # sqlservr listening
```

### 6.4 Command-line tools (`sqlcmd`)

**Check:** `ls /opt/mssql-tools18/bin/sqlcmd 2>/dev/null`

**Do:**

```bash
ACCEPT_EULA=Y apt-get install -y mssql-tools18 unixodbc-dev
echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' > /etc/profile.d/mssql-tools.sh
export PATH="$PATH:/opt/mssql-tools18/bin"
```

Define a helper for the rest of this guide. `sqlcmd` reads the password from `SQLCMDPASSWORD`, so it never appears on the command line:

```bash
[ -n "$SA_PASSWORD" ] || { read -rsp 'sa password: ' SA_PASSWORD; echo; }
sqlsa() { SQLCMDPASSWORD="$SA_PASSWORD" sqlcmd -S 127.0.0.1,1433 -U sa -C -b "$@"; }
```

**Verify** the edition and version:

```bash
sqlsa -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(64)), CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(32));"
# Express Edition (64-bit) 17.0.xxxx.x
```

### 6.5 Listen on this machine only (when the API is on the same server)

**Check:** `grep -A3 '^\[network\]' /var/opt/mssql/mssql.conf` (no `ipaddress` line means all interfaces).

**Do:**

```bash
/opt/mssql/bin/mssql-conf set network.ipaddress 127.0.0.1
systemctl restart mssql-server
```

**Verify:** `ss -ltn | grep ':1433 '` shows `127.0.0.1:1433` only. Skip this step if other machines must reach the database. The firewall still blocks 1433 from the internet.

## 7. Configuration database and its login

The API uses one SQL login that owns `WabiInsightDB` (`db_owner` on that database only, no server-wide rights). Migrations run under it.

**Check:**

```bash
sqlsa -h -1 -W -Q "SET NOCOUNT ON; SELECT 'db:' + ISNULL(CAST(DB_ID(N'WabiInsightDB') AS varchar(10)), 'missing'), 'login:' + ISNULL(CAST(SUSER_ID(N'wabiinsightdbowner') AS varchar(10)), 'missing');"
```

If both exist already (an earlier installation), keep the existing password and skip to **Verify**.

**Do:** generate a password (letters, digits, `-`, safe for connection strings and environment files) and create the database, login and user. The script only creates what's missing:

```bash
WI_DB_PASSWORD="Wi-$(openssl rand -hex 4)-$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 20)"
echo "Store this password for wabiinsightdbowner in your password manager: $WI_DB_PASSWORD"
cat > /tmp/wi-db.sql <<'EOF'
IF DB_ID(N'WabiInsightDB') IS NULL CREATE DATABASE [WabiInsightDB];
GO
IF SUSER_ID(N'wabiinsightdbowner') IS NULL
    CREATE LOGIN [wabiinsightdbowner] WITH PASSWORD = N'$(DbPassword)', DEFAULT_DATABASE = [WabiInsightDB], CHECK_POLICY = ON;
GO
USE [WabiInsightDB];
IF DATABASE_PRINCIPAL_ID(N'wabiinsightdbowner') IS NULL CREATE USER [wabiinsightdbowner] FOR LOGIN [wabiinsightdbowner];
ALTER ROLE db_owner ADD MEMBER [wabiinsightdbowner];
GO
EOF
sqlsa -v DbPassword="$WI_DB_PASSWORD" -i /tmp/wi-db.sql && rm -f /tmp/wi-db.sql
```

**Verify** by signing in as the new login:

```bash
SQLCMDPASSWORD="$WI_DB_PASSWORD" sqlcmd -S 127.0.0.1,1433 -U wabiinsightdbowner -d WabiInsightDB -C -b -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT DB_NAME(), IS_ROLEMEMBER('db_owner'), IS_SRVROLEMEMBER('sysadmin');"
# WabiInsightDB 1 0
```

## 8. .NET 10 SDK (includes the ASP.NET Core runtime)

**Check:**

```bash
dotnet --list-sdks 2>/dev/null | grep '^10\.'
dotnet --list-runtimes 2>/dev/null | grep 'Microsoft.AspNetCore.App 10\.'
```

**Do:** .NET 10 comes from Ubuntu's own archive on 24.04:

```bash
apt-get install -y dotnet-sdk-10.0
```

If apt reports `Unable to locate package dotnet-sdk-10.0` (mirror not updated yet), use Microsoft's install script and link it to `/usr/bin/dotnet`, which the service unit expects:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet
ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
```

**Verify:**

```bash
readlink -f /usr/bin/dotnet                               # the dotnet binary
dotnet --list-sdks | grep '^10\.'                          # 10.0.x
dotnet --list-runtimes | grep 'Microsoft.AspNetCore.App 10\.'
```

## 9. Node.js 22 (building the web app)

Angular 22 needs Node `^22.22.3`, `^24.15` or newer. Ubuntu's own `nodejs` package is too old.

**Check:** `node --version 2>/dev/null` (`v22.22.3` or later is fine).

**Do:**

```bash
curl -fsSL https://deb.nodesource.com/setup_22.x -o /tmp/nodesource_setup.sh
bash /tmp/nodesource_setup.sh
apt-get install -y nodejs
```

**Verify:**

```bash
node --version      # v22.x (>= v22.22.3)
npm --version
```

## 10. Service account and folders

| Path | Owner | Contents |
|---|---|---|
| `/opt/wabi-insight/src` | root | Git checkout used for building |
| `/opt/wabi-insight/api` | root, group `wabi-insight`, read-only | Published API |
| `/var/lib/wabi-insight/artifacts` | `wabi-insight` | Reports, exports, attachments, logos |
| `/var/lib/wabi-insight/keys` | `wabi-insight` | Data Protection key ring. **Losing it makes every stored connection string and provider key unreadable.** |
| `/etc/wabi-insight/api.env` | root, mode 600 | Server-specific settings |
| `/var/www/wabi-insight` | root, world-readable | Angular build served by nginx |

**Check:**

```bash
id wabi-insight 2>/dev/null
ls -ld /opt/wabi-insight /var/lib/wabi-insight/{artifacts,keys} /etc/wabi-insight /var/www/wabi-insight 2>&1
```

**Do:**

```bash
id wabi-insight >/dev/null 2>&1 || useradd --system --home-dir /opt/wabi-insight --no-create-home --shell /usr/sbin/nologin wabi-insight
mkdir -p /opt/wabi-insight/api /var/lib/wabi-insight/artifacts /var/lib/wabi-insight/keys /etc/wabi-insight /var/www/wabi-insight
chown -R wabi-insight:wabi-insight /var/lib/wabi-insight
chmod 750 /var/lib/wabi-insight /var/lib/wabi-insight/artifacts
chmod 700 /var/lib/wabi-insight/keys
chmod 750 /etc/wabi-insight
```

**Verify:**

```bash
id wabi-insight                                           # uid=… gid=… groups=wabi-insight
stat -c '%U:%G %a %n' /var/lib/wabi-insight/artifacts /var/lib/wabi-insight/keys
# wabi-insight:wabi-insight 750 …/artifacts
# wabi-insight:wabi-insight 700 …/keys
```

## 11. Get the source code

**Check:**

```bash
git -C /opt/wabi-insight/src rev-parse --abbrev-ref HEAD 2>/dev/null && git -C /opt/wabi-insight/src log -1 --oneline
```

**Do:** git asks for a username and a password. Give your GitHub user and the personal access token. They aren't saved:

```bash
git clone --branch "$WI_BRANCH" "$WI_REPO" /opt/wabi-insight/src
```

If the checkout already exists, update it instead:

```bash
git -C /opt/wabi-insight/src fetch origin "$WI_BRANCH" && git -C /opt/wabi-insight/src checkout "$WI_BRANCH" && git -C /opt/wabi-insight/src pull --ff-only
```

**Verify:**

```bash
git -C /opt/wabi-insight/src log -1 --oneline        # the commit you expect
ls /opt/wabi-insight/src/WabiInsight.slnx
```

## 12. Build the API and the web app

### 12.1 API

**Check:** `ls -l /opt/wabi-insight/api/WabiInsight.Api.dll 2>/dev/null` (present means built before. Rebuilding is safe).

**Do:**

```bash
cd /opt/wabi-insight/src
dotnet publish src/WabiInsight.Api/WabiInsight.Api.csproj -c Release -o /opt/wabi-insight/api
chown -R root:wabi-insight /opt/wabi-insight/api
find /opt/wabi-insight/api -type d -exec chmod 750 {} + && find /opt/wabi-insight/api -type f -exec chmod 640 {} +
```

`appsettings.json` in the publish folder holds the committed configuration (database connection string, SMTP, AI key: owner decision G). The permissions above let only root and the service read it.

**Verify:**

```bash
ls -l /opt/wabi-insight/api/WabiInsight.Api.dll
ls /opt/wabi-insight/api/appsettings*.json                         # only appsettings.json
stat -c '%U:%G %a' /opt/wabi-insight/api/appsettings.json         # root:wabi-insight 640
```

The publish never includes `appsettings.Local.json` or `appsettings.Development.json`. Don't create `appsettings.Local.json` on this server: it loads after environment variables and would override `/etc/wabi-insight/api.env`.

### 12.2 Web

**Check:** `ls /var/www/wabi-insight/index.html 2>/dev/null`

**Do:**

```bash
cd /opt/wabi-insight/src/src/web
npm ci
npm run build -- --configuration production
rsync -a --delete dist/wabi-insight-web/browser/ /var/www/wabi-insight/
chown -R root:root /var/www/wabi-insight && chmod -R a+rX /var/www/wabi-insight
```

**Verify:**

```bash
ls /var/www/wabi-insight/index.html /var/www/wabi-insight/brand >/dev/null && echo web-files-ok
grep -o '<base href="[^"]*"' /var/www/wabi-insight/index.html       # <base href="/"
```

## 13. Server-specific configuration

`appsettings.json` already holds the SMTP account and the AI key. This file sets what differs on this server: the database on this machine (`127.0.0.1`), the public URL and the storage paths. Environment variables use `__` between sections.

**Check:** `ls -l /etc/wabi-insight/api.env 2>/dev/null && grep -c . /etc/wabi-insight/api.env`

**Do:** if `WI_DB_PASSWORD` is no longer set in this shell, enter it again. The file is written only when it doesn't exist yet:

```bash
[ -n "$WI_DB_PASSWORD" ] || { read -rsp 'Password of wabiinsightdbowner: ' WI_DB_PASSWORD; echo; }
[ -f /etc/wabi-insight/api.env ] || cat > /etc/wabi-insight/api.env <<EOF
# /etc/wabi-insight/api.env — read by systemd (EnvironmentFile). Mode 600, root only.
ConnectionStrings__ConfigurationDatabase=Server=127.0.0.1,1433;Database=WabiInsightDB;User Id=wabiinsightdbowner;Password=${WI_DB_PASSWORD};Encrypt=True;TrustServerCertificate=True;
Platform__BaseUrl=https://${WI_DOMAIN}
Storage__RootPath=/var/lib/wabi-insight/artifacts
DataProtection__KeyRingPath=/var/lib/wabi-insight/keys
Bootstrap__Enabled=false
# Optional overrides of appsettings.json, e.g.:
# Email__Password=...
# Ai__Anthropic__ApiKey=...
EOF
chmod 600 /etc/wabi-insight/api.env && chown root:root /etc/wabi-insight/api.env
```

If the database is on another server, change the `ConnectionStrings__…` line to that server. To use the committed connection string from `appsettings.json` unchanged, delete the line.

**Verify** (values masked):

```bash
stat -c '%U:%G %a' /etc/wabi-insight/api.env                           # root:root 600
sed -E 's/(Password=)[^;]*/\1***/' /etc/wabi-insight/api.env | grep -v '^#'
```

## 14. Create the database schema (migrations)

The command runs once as the service user, with the same settings the service uses.

**Check:** this lists applied migrations. An error or empty output means none:

```bash
SQLCMDPASSWORD="$WI_DB_PASSWORD" sqlcmd -S 127.0.0.1,1433 -U wabiinsightdbowner -d WabiInsightDB -C -h -1 -W \
  -Q "SET NOCOUNT ON; IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NULL SELECT 'no migrations yet' ELSE SELECT COUNT(*), MAX(MigrationId) FROM dbo.__EFMigrationsHistory;"
```

**Do:**

```bash
systemd-run --wait --pipe --quiet --uid=wabi-insight --gid=wabi-insight \
  --property=EnvironmentFile=/etc/wabi-insight/api.env --setenv=ASPNETCORE_ENVIRONMENT=Production \
  --working-directory=/opt/wabi-insight/api /usr/bin/dotnet WabiInsight.Api.dll migrate
```

**Verify:** run the same command again. It must print `Configuration database is up to date.` The **Check** query now shows the migration count and the newest migration (e.g. `…_AddAnalysisSharing`).

## 15. Create the first Platform Administrator (once)

**Check:** `0` means not done yet:

```bash
SQLCMDPASSWORD="$WI_DB_PASSWORD" sqlcmd -S 127.0.0.1,1433 -U wabiinsightdbowner -d WabiInsightDB -C -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.Administrators;"
```

**Do:** choose the administrator's initial password: at least 12 characters, mixed case, a digit and a symbol, no `"` or `\`. You change it at first sign-in. The initialization secret is any long random string, used once.

```bash
read -rsp 'Initial password for the Platform Administrator: ' WI_ADMIN_PASSWORD; echo
umask 077 && cat > /etc/wabi-insight/bootstrap.env <<EOF
Bootstrap__Enabled=true
Bootstrap__AdministratorEmail=${WI_ADMIN_EMAIL}
Bootstrap__AdministratorFullName=Platform Administrator
Bootstrap__AdministratorPassword=${WI_ADMIN_PASSWORD}
Bootstrap__InitializationSecret=$(openssl rand -hex 32)
EOF
systemd-run --wait --pipe --quiet --uid=wabi-insight --gid=wabi-insight \
  --property=EnvironmentFile=/etc/wabi-insight/api.env --property=EnvironmentFile=/etc/wabi-insight/bootstrap.env \
  --setenv=ASPNETCORE_ENVIRONMENT=Production --working-directory=/opt/wabi-insight/api /usr/bin/dotnet WabiInsight.Api.dll bootstrap
shred -u /etc/wabi-insight/bootstrap.env; unset WI_ADMIN_PASSWORD
```

**Verify:** the command printed `Bootstrap completed. AdministratorId=…`. The **Check** query returns `1`, and the temporary file is gone:

```bash
ls /etc/wabi-insight/bootstrap.env 2>/dev/null || echo bootstrap-file-removed
```

## 16. Run the API as a service

**Check:**

```bash
systemctl is-enabled wabi-insight-api 2>/dev/null; systemctl is-active wabi-insight-api 2>/dev/null
```

**Do:**

```bash
cp /opt/wabi-insight/src/deploy/systemd/wabi-insight-api.service /etc/systemd/system/wabi-insight-api.service
systemd-analyze verify /etc/systemd/system/wabi-insight-api.service
systemctl daemon-reload
systemctl enable --now wabi-insight-api
```

The unit runs as `wabi-insight` with `ProtectSystem=strict` (only `/var/lib/wabi-insight` writable) and listens on `127.0.0.1:5080`.

**Verify:**

```bash
systemctl is-active wabi-insight-api                   # active
ss -ltnp | grep '127.0.0.1:5080'                        # dotnet listening
curl -s http://127.0.0.1:5080/health/ready; echo        # {"status":"Healthy",…"configuration-database"…"Healthy"…}
journalctl -u wabi-insight-api -n 30 --no-pager         # no errors; "Now listening on: http://127.0.0.1:5080"
curl -s -o /dev/null http://127.0.0.1:5080/api/auth/csrf && ls /var/lib/wabi-insight/keys   # key-….xml (created on first use)
```

## 17. nginx and the HTTPS certificate

### 17.1 Install nginx and certbot

**Check:** `nginx -v 2>&1; certbot --version 2>/dev/null`

**Do:**

```bash
apt-get install -y nginx certbot
```

**Verify:** `systemctl is-active nginx` prints `active`, and `nginx -v` shows 1.24 or later.

### 17.2 Obtain the certificate

**Check:** `ls /etc/letsencrypt/live/$WI_DOMAIN/fullchain.pem 2>/dev/null`

**Do:** serve the ACME challenge folder on port 80 temporarily, then request the certificate:

```bash
mkdir -p /var/www/letsencrypt
cat > /etc/nginx/sites-available/wabi-insight-acme <<EOF
server {
    listen 80;
    server_name ${WI_DOMAIN};
    location /.well-known/acme-challenge/ { root /var/www/letsencrypt; }
    location / { return 404; }
}
EOF
ln -sf /etc/nginx/sites-available/wabi-insight-acme /etc/nginx/sites-enabled/wabi-insight-acme
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx
certbot certonly --webroot -w /var/www/letsencrypt -d "$WI_DOMAIN" -m "$WI_CERT_EMAIL" --agree-tos --non-interactive \
  --deploy-hook "systemctl reload nginx"
```

If you have a purchased certificate instead, copy the full chain and key to `/etc/ssl/wabi-insight/` (key mode 600) and use those paths in 17.3.

**Verify:**

```bash
ls -l /etc/letsencrypt/live/$WI_DOMAIN/{fullchain,privkey}.pem
openssl x509 -in /etc/letsencrypt/live/$WI_DOMAIN/fullchain.pem -noout -subject -enddate
```

### 17.3 The Wabi Insight site

**Check:** `ls -l /etc/nginx/sites-enabled/ | grep wabi-insight`

**Do:** install the site from the repository with your domain filled in, and remove the temporary site:

```bash
sed "s/insight\.example\.com/${WI_DOMAIN}/g" /opt/wabi-insight/src/deploy/nginx/wabi-insight.conf > /etc/nginx/sites-available/wabi-insight
ln -sf /etc/nginx/sites-available/wabi-insight /etc/nginx/sites-enabled/wabi-insight
rm -f /etc/nginx/sites-enabled/wabi-insight-acme
nginx -t && systemctl reload nginx
```

The site redirects HTTP to HTTPS except for `/.well-known/acme-challenge/` (so renewals keep working). It serves the Angular files with long caching for hashed assets and `no-cache` for `index.html`, proxies `/api` and `/health` to Kestrel with a 600 s timeout (study reports take 1–3 minutes) and a 12 MB body limit, and sets the security headers.

**Verify:**

```bash
nginx -t                                                                   # syntax is ok / test is successful
curl -s -o /dev/null -w '%{http_code} %{redirect_url}\n' http://$WI_DOMAIN/            # 301 https://…/
curl -s https://$WI_DOMAIN/health/ready; echo                                          # Healthy
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' https://$WI_DOMAIN/login      # 200 text/html
curl -sI https://$WI_DOMAIN/ | grep -iE '^(strict-transport-security|content-security-policy|x-frame-options)'
certbot renew --dry-run 2>&1 | tail -2                                                 # "Congratulations, all simulated renewals succeeded"
systemctl list-timers | grep certbot                                                   # renewal timer scheduled
```

## 18. Go-live checks

**Verify** from the server:

```bash
# Outbound SMTP (host and port from appsettings.json)
python3 -c "import json;e=json.load(open('/opt/wabi-insight/api/appsettings.json'))['Email'];print(e['Host'],e['Port'])" | \
  { read h p; timeout 5 bash -c "</dev/tcp/$h/$p" && echo "SMTP $h:$p reachable"; }
# AI provider endpoint (401 without a key means reachable)
curl -s -o /dev/null -w 'api.anthropic.com %{http_code}\n' https://api.anthropic.com/v1/models
# Real client addresses reach the API (rate limits and audit use them)
grep -n 'X-Forwarded-For' /etc/nginx/sites-available/wabi-insight
```

**Verify** in a browser, from your own computer:

1. Open `https://<your domain>`. The landing page loads, the padlock shows a valid certificate, and the **العربية** switch flips the layout to RTL.
2. Sign in with `WI_ADMIN_EMAIL` and the initial password. You're asked to change it, so do so.
3. **Platform → System Health**: database and API are healthy.
4. **Platform → AI providers**: the default Anthropic provider is listed. **Test** succeeds.
5. Create an organization with a real organization-administrator mailbox. The temporary password **email** arrives, which proves SMTP. Sign in as that administrator in a private window and change the password.
6. As that administrator, register an application or load the samples (step 19), then ask a question on **Ask Wabi Insight**.

## 19. Optional: sample databases and sample users

To fill the platform with realistic data (flight operations, HR/payroll/attendance, procurement/stores) and four users of different roles, follow [`scripts/sql/samples/README.md`](../scripts/sql/samples/README.md).

**Check:**

```bash
sqlsa -h -1 -W -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE name IN (N'FlightOperationsDB', N'HumanResourcesDB', N'ProcurementStoresDB');"
```

**Do:**

```bash
cd /opt/wabi-insight/src/scripts/sql/samples
for f in 01-create-flightoperationsdb.sql 02-create-humanresourcesdb.sql 03-create-procurementstoresdb.sql 04-seed-wabiinsightdb.sql; do
  echo "== $f"; sqlsa -I -i "$f" || break
done
```

**Verify:** the last script prints the four users and the three applications. Then, signed in as `faisal.alshamsi@gulfaviation.example`, open **Applications** and for each application **Replace connection** with `Server=127.0.0.1,1433;Database=…;User Id=…_reader;Password=…;Encrypt=True;TrustServerCertificate=True;`, **Test connection** (*read-only verified*), then **Approve**. Change the sample passwords (README) before anyone else uses the server.

## 20. Optional hardening: dedicated insights login

Platform Insights can run under a login confined to the `insights` schema. Without it, the API logs a warning and uses the configuration login.

**Check:** `grep -c '^Insights__ConnectionString' /etc/wabi-insight/api.env` (0 means not configured).

**Do:**

```bash
WI_INSIGHTS_PASSWORD="Wi-$(openssl rand -hex 4)-$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 20)"
sed -e 's/USE \[WabiInsight\];/USE [WabiInsightDB];/' -e "s/<replace-with-strong-secret>/${WI_INSIGHTS_PASSWORD}/" \
  /opt/wabi-insight/src/scripts/sql/create-insights-login.sql > /tmp/insights-login.sql
sqlsa -i /tmp/insights-login.sql && shred -u /tmp/insights-login.sql
echo "Insights__ConnectionString=Server=127.0.0.1,1433;Database=WabiInsightDB;User Id=wabi_insight_insights;Password=${WI_INSIGHTS_PASSWORD};Encrypt=True;TrustServerCertificate=True;" >> /etc/wabi-insight/api.env
systemctl restart wabi-insight-api
```

**Verify:**

```bash
journalctl -u wabi-insight-api --since '-2 min' --no-pager | grep -c 'Insights:ConnectionString is not configured'   # 0
```

**Platform → Platform Insights Configuration** shows *confined to the insights schema*.

## 21. Backups (SQL Server Express has no Agent, so use cron)

A restore needs **three things together**: the database, `/var/lib/wabi-insight/keys` and `/var/lib/wabi-insight/artifacts`.

**Check:** `ls /etc/cron.d/wabi-insight-backup /usr/local/sbin/wabi-insight-backup.sh 2>/dev/null`

**Do:** database backups go to `/var/opt/mssql/backup` (SQL Server writes there as `mssql`). Key ring and artifacts go to `/var/backups/wabi-insight`. The job runs as the database owner login, so `sa` is never stored on disk:

```bash
install -d -o mssql -g mssql -m 750 /var/opt/mssql/backup
install -d -m 700 /var/backups/wabi-insight
umask 077 && printf 'SQLCMDPASSWORD=%s\n' "$WI_DB_PASSWORD" > /root/.wabi-insight-backup.env
cat > /usr/local/sbin/wabi-insight-backup.sh <<'EOF'
#!/bin/bash
# Nightly backup of WabiInsightDB + key ring + artifacts; keeps 14 days.
set -euo pipefail
set -a; . /root/.wabi-insight-backup.env; set +a
stamp=$(date +%Y%m%d-%H%M)
/opt/mssql-tools18/bin/sqlcmd -S 127.0.0.1,1433 -U wabiinsightdbowner -C -b -Q \
  "BACKUP DATABASE [WabiInsightDB] TO DISK = N'/var/opt/mssql/backup/WabiInsightDB-${stamp}.bak' WITH INIT, CHECKSUM;"
tar -czf /var/backups/wabi-insight/files-${stamp}.tar.gz -C /var/lib wabi-insight
find /var/opt/mssql/backup -name 'WabiInsightDB-*.bak' -mtime +14 -delete
find /var/backups/wabi-insight -name 'files-*.tar.gz' -mtime +14 -delete
EOF
chmod 700 /usr/local/sbin/wabi-insight-backup.sh
echo '30 2 * * * root /usr/local/sbin/wabi-insight-backup.sh >> /var/log/wabi-insight-backup.log 2>&1' > /etc/cron.d/wabi-insight-backup
```

**Verify:** run it once, then check the backup with `sa` (verifying a backup needs server-level permission):

```bash
/usr/local/sbin/wabi-insight-backup.sh && ls -lh /var/opt/mssql/backup /var/backups/wabi-insight
sqlsa -Q "RESTORE VERIFYONLY FROM DISK = N'$(ls -t /var/opt/mssql/backup/WabiInsightDB-*.bak | head -1)' WITH CHECKSUM;" | tail -1
# The backup set on file 1 is valid.
tar -tzf "$(ls -t /var/backups/wabi-insight/files-*.tar.gz | head -1)" | grep -c 'wabi-insight/keys/key-'   # >= 1
```

Copy both folders off the server regularly (rsync, object storage). To restore: `RESTORE DATABASE [WabiInsightDB] FROM DISK = N'…' WITH REPLACE`, then unpack the tarball into `/var/lib` with the service stopped.

## 22. Updating to a new version

```bash
sudo -i; source /root/wabi-insight.vars
cd /opt/wabi-insight/src && git fetch origin "$WI_BRANCH" && git checkout "$WI_BRANCH" && git pull --ff-only && git log -1 --oneline
/usr/local/sbin/wabi-insight-backup.sh                                     # backup first
systemctl stop wabi-insight-api
dotnet publish src/WabiInsight.Api/WabiInsight.Api.csproj -c Release -o /opt/wabi-insight/api
chown -R root:wabi-insight /opt/wabi-insight/api && find /opt/wabi-insight/api -type d -exec chmod 750 {} + && find /opt/wabi-insight/api -type f -exec chmod 640 {} +
systemd-run --wait --pipe --quiet --uid=wabi-insight --gid=wabi-insight --property=EnvironmentFile=/etc/wabi-insight/api.env \
  --setenv=ASPNETCORE_ENVIRONMENT=Production --working-directory=/opt/wabi-insight/api /usr/bin/dotnet WabiInsight.Api.dll migrate
systemctl start wabi-insight-api
cd src/web && npm ci && npm run build -- --configuration production && rsync -a --delete dist/wabi-insight-web/browser/ /var/www/wabi-insight/
cmp -s /opt/wabi-insight/src/deploy/systemd/wabi-insight-api.service /etc/systemd/system/wabi-insight-api.service || echo "unit file changed: review and copy it, then daemon-reload"
```

**Verify:** `curl -s https://$WI_DOMAIN/health/ready` reports Healthy. Sign in, and **System Health** is green.

## 23. Troubleshooting

| Symptom | Check | Fix |
|---|---|---|
| `502 Bad Gateway` from nginx | `systemctl status wabi-insight-api`, `journalctl -u wabi-insight-api -n 100` | Service stopped or crashing. Read the first exception in the journal. |
| Service restarts in a loop with `Login failed for user` | Step 7 **Verify** | Wrong password in `api.env`, or the login has no user in `WabiInsightDB`. |
| `/health/ready` reports the database `Unhealthy` | `systemctl is-active mssql-server`, `ss -ltn \| grep 1433` | Start SQL Server. With 6.5 applied, the API must use `127.0.0.1`. |
| Sign-in works but you're signed out immediately | Is the site opened over `https://`? | Session cookies are `Secure`, so plain HTTP drops them. |
| Everyone is rate-limited together (`429`) | `grep X-Forwarded-For /etc/nginx/sites-available/wabi-insight` | The nginx site must set `X-Forwarded-For`. The API trusts it only from loopback. |
| Uploads fail with `413` | `grep client_max_body_size` in the site | Keep `12m` (ticket attachments are up to 10 MB). |
| PDF generation fails (`DllNotFoundException`, fontconfig) | `dpkg -s libfontconfig1 libfreetype6` | Install both (step 2) and restart the service. |
| No emails arrive | `sqlsa -d WabiInsightDB -Q "SELECT TOP 10 Status, RecipientEmail, LastError, CreatedAt FROM dbo.EmailMessages ORDER BY CreatedAt DESC"` | Fix SMTP settings or outbound port (step 18). Failed messages are retried `Email:MaxAttempts` times. |
| Stored connection strings fail after a restore | `ls /var/lib/wabi-insight/keys` | Restore the key ring from the same backup as the database. |
| Journal shows `No XML encryptor configured` and `Failed to determine the https port for redirect` | | Expected. The key ring is protected by folder permissions (700, service user only), and TLS ends at nginx. |
| Settings in `api.env` seem ignored | `ls /opt/wabi-insight/api/appsettings.Local.json` | Delete that file: it overrides environment variables. |
| Database close to the Express limit | `sqlsa -d WabiInsightDB -Q "EXEC sp_spaceused"` | Archive old usage events, or move to Express 2025 (50 GB) or Standard. |
