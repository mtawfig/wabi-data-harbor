#!/usr/bin/env bash
# wabi-docker — installs, runs, updates, backs up, restores and moves Wabi Insight on a Linux server with Docker.
# Guide: docs/DEPLOYMENT_LINUX_DOCKER.md. Installed by `sudo bash deploy/docker/server/wabi-docker.sh install` as
# /usr/local/sbin/wabi-docker (owned by root); every command runs as root (sudo wabi-docker <command>).
#
# Commands (sudo wabi-docker help shows them with examples):
#   install <repository folder>   copy this script, the stack files and the sample scripts into place
#   init                          start SQL Server, create the database and the application's login
#   deploy <version> [--no-pull]  install a version: pull, back up, migrate, start, check, roll back on failure
#   bootstrap                     create the first Platform Administrator (asks for the e-mail and password)
#   samples [gas|bank|all]        load the sample organizations and their databases
#   sample-connections            show again the read-only connection strings of the sample applications
#   storage-check                 write, read and delete a test file where the files are kept (disk or Azure Blob)
#   insights-login                create the read-only login of Platform Insights
#   backup [--no-upload]          back up databases, logins, key ring and settings into one encrypted file
#   backups                       list the backups on this server and in Azure Blob Storage
#   restore <file or name>        restore a backup (on this server or a new one)
#   registry-login                sign in to the image registry (GitHub) to pull images by hand
#   start | stop | restart | status | logs [service] | version | help
set -Eeuo pipefail

readonly SCRIPT_VERSION=1
WI_HOME="${WI_HOME:-/opt/wabi-insight}"
WI_BIN="${WI_BIN:-/usr/local/sbin/wabi-docker}"
readonly SQLCMD=/opt/mssql-tools18/bin/sqlcmd
readonly API_DB=WabiInsightDB
readonly APP_LOGIN=wabi_app
readonly INSIGHTS_LOGIN=wabi_insights

say() { printf '%s\n' "$*"; }
step() { printf '\n==> %s\n' "$*"; }
warn() { printf 'WARNING: %s\n' "$*" >&2; }
fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
trap 'fail "the command failed on line $LINENO (see the messages above)."' ERR

need_root() { [ "$(id -u)" -eq 0 ] || fail "run it as root: sudo wabi-docker $*"; }

# A random secret with upper case, lower case, a digit and a dash: accepted by SQL Server's password policy and
# safe in connection strings and settings files.
random_secret() { printf '%sAa1-%s' "$(openssl rand -hex 12)" "$(openssl rand -hex 4)"; }

# Reads one setting (KEY=value) of a settings file; empty when missing.
setting() { local file=$1 key=$2; [ -f "$file" ] || return 0; grep -E "^${key}=" "$file" | tail -n 1 | cut -d= -f2- || true; }

# Sets KEY=value in a settings file, replacing the line when it exists. The value is never printed.
set_setting() {
  local file=$1 key=$2 value=$3 tmp
  tmp=$(mktemp)
  if grep -qE "^${key}=" "$file"; then
    awk -v k="$key" -v v="$value" 'BEGIN { FS = OFS = "=" } $1 == k { print k "=" v; next } { print }' "$file" > "$tmp"
  else
    cp "$file" "$tmp"; printf '%s=%s\n' "$key" "$value" >> "$tmp"
  fi
  cat "$tmp" > "$file"; rm -f "$tmp"
}

load_env() {
  [ -f "$WI_HOME/.env" ] || fail "$WI_HOME/.env is missing: run 'sudo bash deploy/docker/server/wabi-docker.sh install <repository folder>' first (step 16)."
  set -a; # shellcheck disable=SC1091
  . "$WI_HOME/.env"; set +a
  WI_IMAGE_TAG="$(cat "$WI_HOME/state/image-tag" 2>/dev/null || echo none)"
  export WI_IMAGE_TAG
}

dc() { docker compose --project-name wabi-insight --project-directory "$WI_HOME" -f "$WI_HOME/compose.yaml" "$@"; }

# Runs sqlcmd inside the SQL Server container as sa. The password travels in the environment, never on a command line.
sql() { SQLCMDPASSWORD="$WI_SA_PASSWORD" dc exec -T -e SQLCMDPASSWORD sqlserver "$SQLCMD" -S localhost -U sa -C -b -I -f 65001 "$@"; }

wait_for_sql() {
  dc up -d sqlserver >/dev/null
  local _
  for _ in $(seq 1 60); do
    if sql -h -1 -Q "SET NOCOUNT ON; SELECT 1" >/dev/null 2>&1; then return 0; fi
    sleep 3
  done
  dc logs --tail 30 sqlserver || true
  fail "SQL Server did not start within 3 minutes (its last log lines are above)."
}

db_exists() { [ "$(sql -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$1'" | tr -d '[:space:]')" = "1" ]; }

# The API's readiness, asked from inside the private network (the web container has wget). Prints the JSON.
api_ready() { dc exec -T web wget -q -T 5 -O - http://api:8080/health/ready 2>/dev/null; }

wait_for_api() {
  local _ report=""
  for _ in $(seq 1 60); do
    report=$(api_ready || true)
    if printf '%s' "$report" | grep -q '"status":"Healthy"'; then return 0; fi
    sleep 3
  done
  say "Last health report: ${report:-none (the API does not answer)}"
  return 1
}

# ---------------------------------------------------------------------------------------------------------------- install
cmd_install() {
  need_root install
  local source=${1:-}
  [ -n "$source" ] && [ -f "$source/deploy/docker/server/compose.yaml" ] \
    || fail "give the folder of the downloaded repository, for example: sudo bash deploy/docker/server/wabi-docker.sh install ~/wabi-src"
  source=$(cd "$source" && pwd)
  command -v docker >/dev/null || fail "Docker is not installed (step 12)."
  docker compose version >/dev/null 2>&1 || fail "the Docker Compose plugin is missing (step 12)."
  command -v openssl >/dev/null || fail "openssl is missing: apt-get install -y openssl"

  step "Installing the wabi-docker command as $WI_BIN"
  install -o root -g root -m 755 "$source/deploy/docker/server/wabi-docker.sh" "$WI_BIN"

  step "Installing the stack files in $WI_HOME"
  install -d -o root -g root -m 755 "$WI_HOME"
  install -d -o root -g root -m 700 "$WI_HOME/state" "$WI_HOME/backups"
  install -o root -g root -m 644 "$source/deploy/docker/server/compose.yaml" "$WI_HOME/compose.yaml"
  install -o root -g root -m 644 "$source/deploy/docker/server/Caddyfile" "$WI_HOME/Caddyfile"
  install -o root -g root -m 644 "$source/deploy/docker/server/env.example" "$WI_HOME/env.example"
  install -o root -g root -m 644 "$source/deploy/docker/server/api.env.example" "$WI_HOME/api.env.example"
  printf '%s\n' "$SCRIPT_VERSION" > "$WI_HOME/state/bundle-version"

  step "Copying the SQL scripts (sample organizations, Platform Insights login)"
  rm -rf "$WI_HOME/samples" "$WI_HOME/sql"
  install -d -m 755 "$WI_HOME/samples" "$WI_HOME/sql"
  cp "$source"/scripts/sql/samples/0*.sql "$WI_HOME/samples/"
  install -d -m 755 "$WI_HOME/samples/wabi-bank"
  cp "$source"/scripts/sql/samples/wabi-bank/0*.sql "$WI_HOME/samples/wabi-bank/"
  cp "$source"/scripts/sql/create-insights-login.sql "$WI_HOME/sql/"
  chmod -R a+rX "$WI_HOME/samples" "$WI_HOME/sql"

  if [ ! -f "$WI_HOME/.env" ]; then
    step "Creating $WI_HOME/.env with a new SQL Server password and backup passphrase"
    install -o root -g root -m 600 "$source/deploy/docker/server/env.example" "$WI_HOME/.env"
    set_setting "$WI_HOME/.env" WI_SA_PASSWORD "$(random_secret)"
    set_setting "$WI_HOME/.env" WI_BACKUP_PASSPHRASE "$(random_secret)"
  else
    say "$WI_HOME/.env exists: kept as it is."
  fi
  if [ ! -f "$WI_HOME/api.env" ]; then
    install -o root -g root -m 600 "$source/deploy/docker/server/api.env.example" "$WI_HOME/api.env"
  else
    say "$WI_HOME/api.env exists: kept as it is."
  fi
  chmod 600 "$WI_HOME/.env" "$WI_HOME/api.env"

  step "Scheduling the nightly backup (02:30 server time)"
  cat > /etc/cron.d/wabi-insight-backup <<CRON
# Wabi Insight: nightly encrypted backup (docs/DEPLOYMENT_LINUX_DOCKER.md, step 32). Log: /var/log/wabi-insight-backup.log
SHELL=/bin/bash
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
30 2 * * * root WI_HOME=$WI_HOME $WI_BIN backup >> /var/log/wabi-insight-backup.log 2>&1
CRON
  chmod 644 /etc/cron.d/wabi-insight-backup

  say ""
  say "Installed: wabi-docker version $SCRIPT_VERSION, stack files in $WI_HOME."
  say "Next: set WI_DOMAIN and WI_ACME_EMAIL in $WI_HOME/.env (sudo nano $WI_HOME/.env), then: sudo wabi-docker init"
}

# ---------------------------------------------------------------------------------------------------------------- init
cmd_init() {
  need_root init
  load_env
  [ "$WI_DOMAIN" != "insight.example.com" ] || fail "set your own WI_DOMAIN in $WI_HOME/.env first (sudo nano $WI_HOME/.env)."
  [ -n "${WI_SA_PASSWORD:-}" ] || fail "WI_SA_PASSWORD is empty in $WI_HOME/.env."

  step "Starting SQL Server (the first start takes about a minute)"
  wait_for_sql
  say "SQL Server is running: $(sql -h -1 -W -Q "SET NOCOUNT ON; SELECT CONCAT(CAST(SERVERPROPERTY('Edition') AS nvarchar(128)), N' ', CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)))")"

  local current
  current=$(setting "$WI_HOME/api.env" ConnectionStrings__ConfigurationDatabase)
  if [ -n "$current" ]; then
    say "api.env already has the database connection: nothing to create."
  else
    step "Creating the database $API_DB and its login $APP_LOGIN"
    if [ "$(sql -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.server_principals WHERE name = N'$APP_LOGIN'" | tr -d '[:space:]')" = "1" ]; then
      fail "the login $APP_LOGIN exists but api.env has no connection string. Restore api.env from a backup, or drop the login and run init again."
    fi
    local password
    password=$(random_secret)
    sql -Q "IF DB_ID(N'$API_DB') IS NULL CREATE DATABASE [$API_DB];
            CREATE LOGIN [$APP_LOGIN] WITH PASSWORD = N'$password', CHECK_POLICY = ON, DEFAULT_DATABASE = [$API_DB];" >/dev/null
    sql -d "$API_DB" -Q "CREATE USER [$APP_LOGIN] FOR LOGIN [$APP_LOGIN]; ALTER ROLE db_owner ADD MEMBER [$APP_LOGIN];" >/dev/null
    set_setting "$WI_HOME/api.env" ConnectionStrings__ConfigurationDatabase \
      "Server=sqlserver,1433;Database=$API_DB;User Id=$APP_LOGIN;Password=$password;Encrypt=True;TrustServerCertificate=True;"
    say "Created. The connection string is in $WI_HOME/api.env (the password is never shown)."
  fi
  say ""
  say "Next: the first deployment, from GitHub (step 26)"
}

# ---------------------------------------------------------------------------------------------------------------- deploy
cmd_deploy() {
  need_root deploy
  local tag="" pull=yes login_user=""
  while [ $# -gt 0 ]; do
    case "$1" in
      --no-pull) pull=no ;;
      --registry-user) shift; login_user=${1:-} ;;
      -*) fail "unknown option $1" ;;
      *) tag=$1 ;;
    esac
    shift
  done
  [[ "$tag" =~ ^[A-Za-z0-9._-]{1,64}$ ]] || fail "give the version to install, for example: sudo wabi-docker deploy 1a2b3c4d5e6f"
  load_env
  [ -n "$(setting "$WI_HOME/api.env" ConnectionStrings__ConfigurationDatabase)" ] || fail "run 'sudo wabi-docker init' first (step 20)."
  local previous=$WI_IMAGE_TAG
  local started
  started=$(date -u +%FT%TZ)
  printf '%s deploy %s started (previous %s)\n' "$started" "$tag" "$previous" >> "$WI_HOME/state/deploy.log"

  export WI_IMAGE_TAG=$tag
  if [ "$pull" = yes ]; then
    if [ -n "$login_user" ]; then
      step "Signing in to ${WI_REGISTRY%%/*} as $login_user (token from the deployment)"
      docker login "${WI_REGISTRY%%/*}" -u "$login_user" --password-stdin >/dev/null
    fi
    step "Downloading version $tag"
    if ! dc pull api web; then
      [ -n "$login_user" ] && docker logout "${WI_REGISTRY%%/*}" >/dev/null 2>&1 || true
      fail "the images of version $tag could not be downloaded from $WI_REGISTRY (signed in? step 44; does the version exist?)."
    fi
    [ -n "$login_user" ] && docker logout "${WI_REGISTRY%%/*}" >/dev/null 2>&1 || true
  else
    docker image inspect "$WI_REGISTRY/wabi-insight-api:$tag" "$WI_REGISTRY/wabi-insight-web:$tag" >/dev/null \
      || fail "--no-pull: the images $WI_REGISTRY/wabi-insight-{api,web}:$tag are not on this server."
  fi

  wait_for_sql
  if db_exists "$API_DB" && [ "$previous" != "none" ]; then
    step "Backing up before the update"
    cmd_backup --no-upload --quiet || fail "the backup before the update failed: nothing was changed."
  fi

  step "Applying database changes of version $tag (migrate)"
  if ! dc run --rm --no-deps -T api migrate; then
    printf '%s deploy %s FAILED at migrate\n' "$(date -u +%FT%TZ)" "$tag" >> "$WI_HOME/state/deploy.log"
    fail "the migration failed; the running version ($previous) was not changed. Restore the backup above if the database was changed."
  fi

  step "Starting version $tag"
  dc up -d --remove-orphans
  if ! wait_for_api; then
    dc logs --tail 40 api || true
    if [ "$previous" != "none" ]; then
      warn "version $tag did not become healthy: going back to $previous."
      export WI_IMAGE_TAG=$previous
      dc up -d --remove-orphans
      wait_for_api && say "Version $previous is running again." || warn "version $previous did not become healthy either."
    fi
    printf '%s deploy %s FAILED (not healthy)\n' "$(date -u +%FT%TZ)" "$tag" >> "$WI_HOME/state/deploy.log"
    fail "version $tag did not report Healthy (the API's last log lines are above)."
  fi

  [ "$previous" != "none" ] && printf '%s\n' "$previous" > "$WI_HOME/state/previous-tag"
  printf '%s\n' "$tag" > "$WI_HOME/state/image-tag"
  printf '%s deploy %s succeeded\n' "$(date -u +%FT%TZ)" "$tag" >> "$WI_HOME/state/deploy.log"
  docker image prune -f >/dev/null 2>&1 || true

  step "Checking the site through HTTPS"
  local insecure=()
  [ "$WI_DOMAIN" = localhost ] && insecure=(-k)   # a local test site uses Caddy's own certificate
  if curl -fsS "${insecure[@]}" -o /dev/null --max-time 20 --resolve "$WI_DOMAIN:443:127.0.0.1" "https://$WI_DOMAIN/health/ready" 2>/dev/null; then
    say "https://$WI_DOMAIN answers."
  else
    warn "https://$WI_DOMAIN does not answer yet. On the first deployment the certificate can take a minute; check DNS (step 3) and ports 80/443 (steps 2 and 11), then: sudo wabi-docker status"
  fi
  say ""
  say "Version $tag is running."
}

# ---------------------------------------------------------------------------------------------------------------- bootstrap
cmd_bootstrap() {
  need_root bootstrap
  load_env
  [ "$WI_IMAGE_TAG" != "none" ] || fail "deploy a version first (step 26)."
  local email name password confirm
  email=${WI_BOOTSTRAP_EMAIL:-}
  name=${WI_BOOTSTRAP_NAME:-}
  password=${WI_BOOTSTRAP_PASSWORD:-}
  if [ -z "$email" ] && [ -t 0 ]; then read -r -p "E-mail of the first Platform Administrator: " email || true; fi
  [ -n "$email" ] || fail "no e-mail given: nothing was created."
  if [ -z "$name" ] && [ -t 0 ]; then read -r -p "Full name [Platform Administrator]: " name || true; fi
  name=${name:-Platform Administrator}
  if [ -z "$password" ]; then
    [ -t 0 ] || fail "set WI_BOOTSTRAP_PASSWORD, or run the command in a terminal to be asked for it."
    read -r -s -p "First password (12+ characters; nothing appears while you type): " password || true; echo
    read -r -s -p "The same password again: " confirm || true; echo
    [ "$password" = "$confirm" ] || fail "the two passwords differ: nothing was created."
  fi
  [ "${#password}" -ge 12 ] || fail "the password must have at least 12 characters."

  step "Creating the first Platform Administrator ($email)"
  export Bootstrap__Enabled=true Bootstrap__AdministratorEmail="$email" Bootstrap__AdministratorFullName="$name" \
         Bootstrap__AdministratorPassword="$password" Bootstrap__InitializationSecret
  Bootstrap__InitializationSecret=$(random_secret)
  if dc run --rm --no-deps -T -e Bootstrap__Enabled -e Bootstrap__AdministratorEmail -e Bootstrap__AdministratorFullName \
       -e Bootstrap__AdministratorPassword -e Bootstrap__InitializationSecret api bootstrap; then
    say ""
    say "Done. Sign in at https://$WI_DOMAIN with $email; you will be asked to choose a new password."
  else
    fail "bootstrap failed (the reason is above: 'bootstrap_already_completed' means it was done before)."
  fi
}

# ---------------------------------------------------------------------------------------------------------------- samples
run_sample() { say "  $1"; sql -i "/samples/$1" -o /dev/null || fail "$1 failed. Run it again to see the message: sudo wabi-docker samples"; }

cmd_samples() {
  need_root samples
  load_env
  local which=${1:-all}
  case "$which" in gas|bank|all) ;; *) fail "choose gas, bank or all";; esac
  wait_for_sql
  db_exists "$API_DB" || fail "deploy a version first: the configuration database has no tables yet (step 26)."
  if [ "$which" != bank ]; then
    step "Gulf Aviation Services (GAS): 3 databases, 4 users (about 2 minutes)"
    run_sample 01-create-flightoperationsdb.sql
    run_sample 02-create-humanresourcesdb.sql
    run_sample 03-create-procurementstoresdb.sql
    say "  04-seed-wabiinsightdb.sql"
    sql -i /samples/04-seed-wabiinsightdb.sql | tee "$WI_HOME/state/samples-gas.txt" >/dev/null
  fi
  if [ "$which" != gas ]; then
    step "Wabi Bank (WBK): 5 databases, 8 users (several minutes)"
    run_sample wabi-bank/01-create-wabibankpersonneldb.sql
    run_sample wabi-bank/02-create-wabibankcorebankingdb.sql
    run_sample wabi-bank/03-create-wabibankfinancedb.sql
    run_sample wabi-bank/04-create-wabibankdigitaldb.sql
    run_sample wabi-bank/05-create-wabibankitdb.sql
    say "  06-seed-wabiinsightdb.sql"
    sql -i /samples/wabi-bank/06-seed-wabiinsightdb.sql | tee "$WI_HOME/state/samples-bank.txt" >/dev/null
  fi
  chmod 600 "$WI_HOME"/state/samples-*.txt 2>/dev/null || true
  say ""
  cmd_sample_connections
}

# The read-only connection string of each sample application, as the seeds printed it (they hold sample passwords).
cmd_sample_connections() {
  need_root sample-connections
  local f found=no
  for f in "$WI_HOME/state/samples-gas.txt" "$WI_HOME/state/samples-bank.txt"; do
    [ -f "$f" ] || continue
    found=yes
    say "$(grep -m1 -o 'sign in as [^ ]*' "$f" | sed 's/^/Organization administrator: /; s/sign in as //')"
    grep -E '^[A-Z][A-Z-]+ +Server=' "$f" | sed -E 's/ +/  /; s/[[:space:]]+$//; s/^/  /'
    say ""
  done
  [ "$found" = yes ] || fail "no sample organization loaded yet: sudo wabi-docker samples"
  say "Each organization administrator enters these under Organization Administration > Applications >"
  say "Replace connection, then Test connection and Approve (step 30)."
}

# ---------------------------------------------------------------------------------------------------------------- insights-login
cmd_insights_login() {
  need_root insights-login
  load_env
  wait_for_sql
  db_exists "$API_DB" || fail "deploy a version first (step 26)."
  if [ -n "$(setting "$WI_HOME/api.env" Insights__ConnectionString)" ]; then
    say "api.env already has Insights__ConnectionString: nothing to do."; return 0
  fi
  local password
  password=$(random_secret)
  step "Creating the read-only login $INSIGHTS_LOGIN (Platform Insights)"
  sed "s/<replace-with-strong-secret>/$password/" "$WI_HOME/sql/create-insights-login.sql" \
    | SQLCMDPASSWORD="$WI_SA_PASSWORD" dc exec -T -e SQLCMDPASSWORD sqlserver "$SQLCMD" -S localhost -U sa -C -b -I >/dev/null
  set_setting "$WI_HOME/api.env" Insights__ConnectionString \
    "Server=sqlserver,1433;Database=$API_DB;User Id=$INSIGHTS_LOGIN;Password=$password;Encrypt=True;TrustServerCertificate=True;"
  dc up -d api >/dev/null
  say "Created, saved in api.env, and the API restarted with it."
}

# ---------------------------------------------------------------------------------------------------------------- backup
blob_url() { # <name> → the SAS URL of one blob in the backup container
  local base=${WI_BACKUP_SAS_URL%%\?*} query=${WI_BACKUP_SAS_URL#*\?}
  printf '%s/%s?%s' "$base" "$1" "$query"
}

cmd_backup() {
  need_root backup
  load_env
  local upload=yes quiet=no
  for arg in "$@"; do
    case "$arg" in --no-upload) upload=no ;; --quiet) quiet=yes ;; *) fail "unknown option $arg" ;; esac
  done
  [ -n "${WI_BACKUP_PASSPHRASE:-}" ] || fail "WI_BACKUP_PASSPHRASE is empty in $WI_HOME/.env."
  local stamp work name databases db
  stamp=$(date -u +%Y%m%d-%H%M%S)
  name="wabi-backup-$stamp.tar.gz.enc"
  work=$(mktemp -d "$WI_HOME/backups/.work-XXXXXX")
  # shellcheck disable=SC2064
  trap "rm -rf '$work'" RETURN

  wait_for_sql
  [ "$quiet" = yes ] || step "Backing up the databases"
  databases=$(sql -h -1 -W -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE database_id > 4 AND state_desc = N'ONLINE' ORDER BY name" | tr -d '\r')
  mkdir -p "$work/databases"
  sql -Q "EXEC master.sys.xp_create_subdir N'/var/opt/mssql/backup/wabi'" >/dev/null
  for db in $databases; do
    [ "$quiet" = yes ] || say "  $db"
    sql -Q "BACKUP DATABASE [$db] TO DISK = N'/var/opt/mssql/backup/wabi/$db.bak' WITH INIT, CHECKSUM" >/dev/null
    dc cp "sqlserver:/var/opt/mssql/backup/wabi/$db.bak" "$work/databases/$db.bak" >/dev/null 2>&1
    sql -Q "EXEC master.sys.xp_delete_files N'/var/opt/mssql/backup/wabi/$db.bak'" >/dev/null 2>&1 || true
  done

  # SQL logins live outside the databases; saved with their password hashes and SIDs so the users inside the
  # restored databases find their logins again, on this server or a new one.
  sql -h -1 -y 1024 -Q "SET NOCOUNT ON;
    SELECT CONCAT(N'IF SUSER_ID(N''', name, N''') IS NULL CREATE LOGIN [', name, N'] WITH PASSWORD = ',
                  CONVERT(nvarchar(max), CAST(LOGINPROPERTY(name, 'PasswordHash') AS varbinary(256)), 1),
                  N' HASHED, SID = ', CONVERT(nvarchar(max), sid, 1), N', CHECK_POLICY = OFF, DEFAULT_DATABASE = [master];')
    FROM sys.sql_logins WHERE name <> N'sa' AND name NOT LIKE N'##%' ORDER BY name;" | sed 's/[[:space:]]*$//' > "$work/logins.sql"

  [ "$quiet" = yes ] || step "Backing up the key ring and the settings"
  dc run --rm --no-deps -T --user root --entrypoint tar api czf - -C /var/lib/wabi-insight keys > "$work/keys.tar.gz"
  dc run --rm --no-deps -T --user root --entrypoint tar api czf - -C /var/lib/wabi-insight artifacts > "$work/files.tar.gz"
  cp "$WI_HOME/.env" "$work/env"; cp "$WI_HOME/api.env" "$work/api.env"
  cp "$WI_HOME/state/image-tag" "$work/image-tag" 2>/dev/null || echo none > "$work/image-tag"
  mkdir -p "$work/samples"; cp "$WI_HOME"/state/samples-*.txt "$work/samples/" 2>/dev/null || true
  printf 'created=%s\nhost=%s\nversion=%s\nscript=%s\n' "$stamp" "$(hostname)" "$(cat "$work/image-tag")" "$SCRIPT_VERSION" > "$work/MANIFEST"

  [ "$quiet" = yes ] || step "Encrypting into $WI_HOME/backups/$name"
  WI_PASS="$WI_BACKUP_PASSPHRASE" tar czf - -C "$work" . \
    | WI_PASS="$WI_BACKUP_PASSPHRASE" openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -salt -pass env:WI_PASS -out "$WI_HOME/backups/$name"
  chmod 600 "$WI_HOME/backups/$name"

  # Keep the newest WI_BACKUP_KEEP files.
  ls -1t "$WI_HOME"/backups/wabi-backup-*.tar.gz.enc 2>/dev/null | tail -n +"$(( ${WI_BACKUP_KEEP:-7} + 1 ))" | xargs -r rm -f

  if [ "$upload" = yes ] && [ -n "${WI_BACKUP_SAS_URL:-}" ]; then
    [ "$quiet" = yes ] || step "Copying it to Azure Blob Storage"
    curl -fsS -X PUT -H "x-ms-blob-type: BlockBlob" --upload-file "$WI_HOME/backups/$name" "$(blob_url "$name")" \
      || fail "the copy to Azure Blob Storage failed (is WI_BACKUP_SAS_URL valid, with write permission, not expired?). The backup is on this server."
  elif [ "$upload" = yes ]; then
    warn "WI_BACKUP_SAS_URL is empty: this backup stays on this server only (step 33)."
  fi
  printf '%s backup %s\n' "$(date -u +%FT%TZ)" "$name" >> "$WI_HOME/state/deploy.log"
  say "Backup: $WI_HOME/backups/$name ($(du -h "$WI_HOME/backups/$name" | cut -f1))"
}

cmd_backups() {
  need_root backups
  load_env
  step "On this server ($WI_HOME/backups)"
  ls -1t "$WI_HOME"/backups/wabi-backup-*.tar.gz.enc 2>/dev/null | while read -r f; do printf '  %s  %s\n' "$(basename "$f")" "$(du -h "$f" | cut -f1)"; done
  if [ -n "${WI_BACKUP_SAS_URL:-}" ]; then
    step "In Azure Blob Storage"
    local base=${WI_BACKUP_SAS_URL%%\?*} query=${WI_BACKUP_SAS_URL#*\?}
    curl -fsS "$base?restype=container&comp=list&$query" | grep -o '<Name>[^<]*</Name>' | sed 's/<[^>]*>//g; s/^/  /' \
      || warn "could not list the container (the SAS needs the List permission)."
  fi
}

# ---------------------------------------------------------------------------------------------------------------- restore
cmd_restore() {
  need_root restore
  local source=${1:-} file work passphrase new_server=no
  local sas_given=${WI_BACKUP_SAS_URL:-} passphrase_given=${WI_BACKUP_PASSPHRASE:-}
  [ -n "$source" ] || fail "give a backup file or name: sudo wabi-docker backups shows them."
  [ -f "$WI_HOME/compose.yaml" ] || fail "install first (step 16)."
  # A server whose SQL Server has never started (no database volume) is a new server: everything, settings included,
  # comes from the backup. WI_RESTORE_SETTINGS=yes forces the same on a server that already ran.
  docker volume inspect wabi-insight_mssql >/dev/null 2>&1 || new_server=yes
  [ "${WI_RESTORE_SETTINGS:-no}" = yes ] && new_server=yes
  if [ "$new_server" = no ]; then load_env; fi
  WI_BACKUP_SAS_URL=${sas_given:-${WI_BACKUP_SAS_URL:-}}
  work=$(mktemp -d)
  # shellcheck disable=SC2064
  trap "rm -rf '$work'" RETURN

  install -d -m 700 "$WI_HOME/backups" "$WI_HOME/state"
  if [ -f "$source" ]; then file=$source
  elif [ -f "$WI_HOME/backups/$source" ]; then file=$WI_HOME/backups/$source
  else
    [ -n "$WI_BACKUP_SAS_URL" ] || fail "$source is not on this server. To download it from Azure, run: sudo WI_BACKUP_SAS_URL='<SAS URL>' wabi-docker restore $source"
    step "Downloading $source from Azure Blob Storage"
    file="$WI_HOME/backups/$source"
    curl -fsS -o "$file" "$(blob_url "$source")" || { rm -f "$file"; fail "could not download $source (name, SAS URL with Read permission, expiry?)."; }
  fi

  passphrase=$passphrase_given
  if [ -z "$passphrase" ] && [ "$new_server" = no ]; then passphrase=$(setting "$WI_HOME/.env" WI_BACKUP_PASSPHRASE); fi
  if [ -z "$passphrase" ]; then
    [ -t 0 ] || fail "set WI_BACKUP_PASSPHRASE, or run the command in a terminal to be asked for it."
    read -r -s -p "Backup passphrase (WI_BACKUP_PASSPHRASE, from your password manager): " passphrase || true; echo
  fi
  step "Decrypting $(basename "$file")"
  WI_PASS="$passphrase" openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -pass env:WI_PASS -in "$file" 2>/dev/null | tar xzf - -C "$work" 2>/dev/null \
    || fail "the backup could not be decrypted: wrong passphrase, or the file is damaged."
  say "Backup of $(grep '^created=' "$work/MANIFEST" | cut -d= -f2) from $(grep '^host=' "$work/MANIFEST" | cut -d= -f2), version $(cat "$work/image-tag")."

  if [ "$new_server" = yes ]; then
    step "Restoring the settings (.env, api.env) from the backup"
    install -o root -g root -m 600 "$work/env" "$WI_HOME/.env"
    install -o root -g root -m 600 "$work/api.env" "$WI_HOME/api.env"
  fi
  if [ -d "$work/samples" ]; then cp "$work"/samples/samples-*.txt "$WI_HOME/state/" 2>/dev/null || true; fi
  cp "$work/image-tag" "$WI_HOME/state/image-tag"
  load_env
  [ "$WI_IMAGE_TAG" != "none" ] || fail "the backup does not name a version."

  step "Getting version $WI_IMAGE_TAG"
  if ! docker image inspect "$WI_REGISTRY/wabi-insight-api:$WI_IMAGE_TAG" "$WI_REGISTRY/wabi-insight-web:$WI_IMAGE_TAG" >/dev/null 2>&1; then
    dc pull api web || fail "could not download version $WI_IMAGE_TAG: sign in first with 'sudo wabi-docker registry-login' (step 44)."
  fi

  step "Stopping the application"
  dc stop api web caddy >/dev/null 2>&1 || true
  wait_for_sql

  step "Restoring the SQL logins"
  SQLCMDPASSWORD="$WI_SA_PASSWORD" dc exec -T -e SQLCMDPASSWORD sqlserver "$SQLCMD" -S localhost -U sa -C -b -I < "$work/logins.sql" >/dev/null

  step "Restoring the databases"
  sql -Q "EXEC master.sys.xp_create_subdir N'/var/opt/mssql/backup/wabi'" >/dev/null
  for bak in "$work"/databases/*.bak; do
    db=$(basename "$bak" .bak)
    say "  $db"
    dc cp "$bak" "sqlserver:/var/opt/mssql/backup/wabi/$db.bak" >/dev/null 2>&1
    dc exec -T --user root sqlserver chown mssql "/var/opt/mssql/backup/wabi/$db.bak" >/dev/null 2>&1 || true
    sql -Q "IF DB_ID(N'$db') IS NOT NULL ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            RESTORE DATABASE [$db] FROM DISK = N'/var/opt/mssql/backup/wabi/$db.bak' WITH REPLACE, CHECKSUM;
            ALTER DATABASE [$db] SET MULTI_USER;" >/dev/null
    sql -Q "EXEC master.sys.xp_delete_files N'/var/opt/mssql/backup/wabi/$db.bak'" >/dev/null 2>&1 || true
  done

  step "Restoring the key ring and the files"
  dc run --rm --no-deps -T --user root --entrypoint sh api -c 'rm -rf /var/lib/wabi-insight/keys/* && tar xzf - -C /var/lib/wabi-insight' < "$work/keys.tar.gz"
  dc run --rm --no-deps -T --user root --entrypoint sh api -c 'rm -rf /var/lib/wabi-insight/artifacts/* && tar xzf - -C /var/lib/wabi-insight' < "$work/files.tar.gz"

  step "Starting the application"
  dc up -d --remove-orphans
  if wait_for_api; then
    say ""
    say "Restored and running (version $WI_IMAGE_TAG). Check https://$WI_DOMAIN and sign in."
  else
    dc logs --tail 40 api || true
    fail "the restored application did not report Healthy (the API's last log lines are above)."
  fi
}

# ---------------------------------------------------------------------------------------------------------------- small commands
cmd_registry_login() {
  need_root registry-login
  load_env
  local user token
  read -r -p "GitHub user name: " user
  read -r -s -p "Token (classic, read:packages only; nothing appears while you paste it): " token; echo
  printf '%s' "$token" | docker login "${WI_REGISTRY%%/*}" -u "$user" --password-stdin
}

cmd_status() {
  need_root status
  load_env
  say "Version installed: $WI_IMAGE_TAG   (previous: $(cat "$WI_HOME/state/previous-tag" 2>/dev/null || echo none))"
  say "Site: https://$WI_DOMAIN"
  step "Containers"
  dc ps --format 'table {{.Service}}\t{{.State}}\t{{.Status}}'
  step "Application health"
  local report
  report=$(api_ready || true)
  if [ -n "$report" ]; then printf '%s\n' "$report" | sed 's/},{/},\n  {/g'; else say "The API does not answer."; fi
  step "Latest backups"
  ls -1t "$WI_HOME"/backups/wabi-backup-*.tar.gz.enc 2>/dev/null | head -n 3 | sed 's/^/  /' || true
  step "Disk"
  df -h / | tail -n 1
}

usage() {
  sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'
  cat <<'EOF'

Examples:
  sudo wabi-docker status
  sudo wabi-docker logs api          (Ctrl + C stops following)
  sudo wabi-docker backup
  sudo wabi-docker restore wabi-backup-20261112-023000.tar.gz.enc
EOF
}

main() {
  local command=${1:-help}
  shift || true
  case "$command" in
    install) cmd_install "$@" ;;
    init) cmd_init ;;
    deploy) cmd_deploy "$@" ;;
    bootstrap) cmd_bootstrap ;;
    samples) cmd_samples "$@" ;;
    insights-login) cmd_insights_login ;;
    sample-connections) cmd_sample_connections ;;
    storage-check) need_root storage-check; load_env; dc run --rm --no-deps -T api storage-check ;;
    backup) cmd_backup "$@" ;;
    backups) cmd_backups ;;
    restore) cmd_restore "$@" ;;
    registry-login) cmd_registry_login ;;
    start) need_root start; load_env; dc up -d ;;
    stop) need_root stop; load_env; dc stop ;;
    restart) need_root restart; load_env; dc up -d --force-recreate api web caddy ;;
    status) cmd_status ;;
    logs) need_root logs; load_env; dc logs --tail 200 -f "$@" ;;
    version|--version) printf '%s\n' "$SCRIPT_VERSION" ;;
    help|-h|--help) usage ;;
    *) usage; fail "unknown command: $command" ;;
  esac
}

main "$@"
