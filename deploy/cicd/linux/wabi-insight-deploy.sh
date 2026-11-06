#!/bin/bash
# =====================================================================================================================
# wabi-insight-deploy — installs a Wabi Insight release on a Linux server (docs/DEVOPS_LINUX.md).
#
# Installed once by an administrator as /usr/local/sbin/wabi-insight-deploy (root:root, 0755) and run by the GitHub
# Actions self-hosted runner through a single sudo rule. The runner never gets general root access: it can only hand
# this script a built release. The installed copy, not the one in the runner's workspace, is what runs.
#
#   wabi-insight-deploy --component backend|frontend|frontend+backend --migrate yes|no
#                       [--api-dir DIR] [--web-dir DIR] [--version SHA] [--actor NAME]
#   wabi-insight-deploy --script-version
#
# Backend:  stage → keep the running release as "previous" → stop the service → copy → [backup + migrate] → start →
#           wait for /health/ready. If migration, start or the health check fails, the previous binaries are restored
#           and the service restarted (database changes made by a migration are not rolled back; restore the backup).
# Frontend: stage → keep the live files as "previous" → copy (index.html last) → check the site answers over HTTPS.
# Paths can be overridden in /etc/wabi-insight/deploy.conf (same variable names as below).
# =====================================================================================================================
set -euo pipefail
umask 022

SCRIPT_VERSION=1
API_DIR=/opt/wabi-insight/api
WEB_DIR=/var/www/wabi-insight
RELEASES_DIR=/opt/wabi-insight/releases
ENV_FILE=/etc/wabi-insight/api.env
SERVICE=wabi-insight-api
SERVICE_USER=wabi-insight
API_HEALTH_URL=http://127.0.0.1:5080/health/ready
BACKUP_SCRIPT=/usr/local/sbin/wabi-insight-backup.sh
DOTNET=/usr/bin/dotnet
LOG_FILE=/var/log/wabi-insight-deploy.log
KEEP_RELEASES=5
HEALTH_ATTEMPTS=45
[ -f /etc/wabi-insight/deploy.conf ] && . /etc/wabi-insight/deploy.conf

component=""; migrate="no"; api_src=""; web_src=""; version="manual"; actor="${SUDO_USER:-$(id -un)}"
usage() {
    cat <<'EOF'
Usage: wabi-insight-deploy --component backend|frontend|frontend+backend --migrate yes|no
                           [--api-dir DIR] [--web-dir DIR] [--version SHA] [--actor NAME]
       wabi-insight-deploy --script-version
EOF
}

while [ $# -gt 0 ]; do
    case "$1" in
        --component) component="${2:-}"; shift 2 ;;
        --migrate) migrate="${2:-}"; shift 2 ;;
        --api-dir) api_src="${2:-}"; shift 2 ;;
        --web-dir) web_src="${2:-}"; shift 2 ;;
        --version) version="${2:-}"; shift 2 ;;
        --actor) actor="${2:-}"; shift 2 ;;
        --script-version) echo "$SCRIPT_VERSION"; exit 0 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
done

fail() { echo "::error::$*" >&2; log_line "FAILED: $*"; exit 1; }
log_line() { printf '%s actor=%s version=%s component=%s migrate=%s %s\n' "$(date -u +%FT%TZ)" "$actor" "$version" "$component" "$migrate" "$*" >> "$LOG_FILE"; }
step() { echo; echo "==> $*"; }

[ "$(id -u)" -eq 0 ] || { echo "Run as root (sudo)." >&2; exit 1; }
for tool in rsync curl flock systemctl systemd-run; do
    command -v "$tool" >/dev/null 2>&1 || { echo "::error::'$tool' is not installed (docs/DEPLOYMENT_LINUX.md step 2: apt-get install -y rsync curl)." >&2; exit 1; }
done
case "$component" in backend|frontend|frontend+backend) ;; *) echo "--component must be backend, frontend or frontend+backend" >&2; exit 2 ;; esac
case "$migrate" in yes|no) ;; *) echo "--migrate must be yes or no" >&2; exit 2 ;; esac
[[ "$version" =~ ^[A-Za-z0-9._-]{1,64}$ ]] || { echo "--version must be 1-64 characters [A-Za-z0-9._-]" >&2; exit 2; }
actor="$(printf '%s' "$actor" | tr -cd 'A-Za-z0-9._@-' | cut -c1-64)"

deploy_backend=no; deploy_frontend=no
[[ "$component" == *backend* ]] && deploy_backend=yes
[[ "$component" == *frontend* ]] && deploy_frontend=yes
if [ "$migrate" = yes ] && [ "$deploy_backend" = no ]; then
    echo "::warning::Database migration runs only together with the backend; ignored for a frontend-only deployment."
    migrate=no
fi
if [ "$deploy_backend" = yes ]; then
    [ -n "$api_src" ] && [ -f "$api_src/WabiInsight.Api.dll" ] || fail "--api-dir must point at the published API (WabiInsight.Api.dll not found in '$api_src')."
    [ -f "$ENV_FILE" ] || fail "$ENV_FILE is missing: complete docs/DEPLOYMENT_LINUX.md step 13 first."
    systemctl cat "$SERVICE" >/dev/null 2>&1 || fail "systemd unit $SERVICE is not installed: complete docs/DEPLOYMENT_LINUX.md step 16 first."
fi
if [ "$deploy_frontend" = yes ]; then
    [ -n "$web_src" ] && [ -f "$web_src/index.html" ] || fail "--web-dir must point at the Angular build (index.html not found in '$web_src')."
fi

# One deployment at a time.
exec 9>/run/wabi-insight-deploy.lock
flock -n 9 || fail "Another deployment is running."

release_id="$(date -u +%Y%m%d-%H%M%S)-${version:0:12}"
stage="$RELEASES_DIR/$release_id"
mkdir -p "$stage"
chmod 750 "$RELEASES_DIR"
log_line "started release=$release_id"
echo "Wabi Insight deployment $release_id (component: $component, migrate: $migrate, by: $actor)"

public_host=""
if [ -f "$ENV_FILE" ]; then
    public_host="$(sed -n 's#^Platform__BaseUrl=https\?://\([^/:]*\).*#\1#p' "$ENV_FILE" | head -1)"
fi

api_healthy() {
    local i
    for ((i = 1; i <= HEALTH_ATTEMPTS; i++)); do
        if curl -fsS --max-time 5 "$API_HEALTH_URL" 2>/dev/null | grep -q '"status":"Healthy"'; then return 0; fi
        sleep 2
    done
    return 1
}

fix_api_permissions() {
    chown -R root:"$SERVICE_USER" "$API_DIR"
    find "$API_DIR" -type d -exec chmod 750 {} +
    find "$API_DIR" -type f -exec chmod 640 {} +
}

restore_backend() {
    echo "::warning::Restoring the previous backend release."
    if [ -f "$RELEASES_DIR/previous-api/WabiInsight.Api.dll" ]; then
        systemctl stop "$SERVICE" || true
        rsync -a --delete --exclude appsettings.Local.json "$RELEASES_DIR/previous-api/" "$API_DIR/"
        fix_api_permissions
        systemctl start "$SERVICE" || true
        if api_healthy; then echo "Previous backend release is running again."; else echo "::error::The previous release did not become healthy either: check 'journalctl -u $SERVICE'."; fi
    else
        echo "::error::No previous backend release to restore (first deployment)."
    fi
}

if [ "$deploy_backend" = yes ]; then
    step "Backend: staging $api_src"
    mkdir -p "$stage/api"
    rsync -a --delete "$api_src"/ "$stage/api/"
    rm -f "$stage/api/appsettings.Local.json" "$stage/api/appsettings.Local.example.json" "$stage/api/appsettings.Development.json"
    cat > "$stage/api/deploy-info.json" <<EOF
{"release":"$release_id","version":"$version","actor":"$actor","deployedAt":"$(date -u +%FT%TZ)"}
EOF

    if [ -f "$API_DIR/WabiInsight.Api.dll" ]; then
        step "Backend: keeping the running release as previous-api"
        mkdir -p "$RELEASES_DIR/previous-api"
        rsync -a --delete "$API_DIR"/ "$RELEASES_DIR/previous-api/"
    fi

    step "Backend: stopping $SERVICE"
    systemctl stop "$SERVICE" || true

    step "Backend: installing into $API_DIR"
    mkdir -p "$API_DIR"
    rsync -a --delete --exclude appsettings.Local.json "$stage/api/" "$API_DIR/"
    fix_api_permissions

    if [ "$migrate" = yes ]; then
        if [ -x "$BACKUP_SCRIPT" ]; then
            step "Database: backup before migration ($BACKUP_SCRIPT)"
            "$BACKUP_SCRIPT" || { restore_backend; fail "Backup failed; nothing was migrated."; }
        else
            echo "::warning::$BACKUP_SCRIPT not found: migrating without a fresh backup (docs/DEPLOYMENT_LINUX.md step 21)."
        fi
        step "Database: applying migrations"
        if ! systemd-run --wait --pipe --quiet --collect --uid="$SERVICE_USER" --gid="$SERVICE_USER" \
                --property=EnvironmentFile="$ENV_FILE" --setenv=ASPNETCORE_ENVIRONMENT=Production \
                --working-directory="$API_DIR" "$DOTNET" WabiInsight.Api.dll migrate; then
            restore_backend
            fail "Migration failed; the previous backend was restored. Applied migrations (if any) stay; see the backup."
        fi
    fi

    step "Backend: starting $SERVICE"
    systemctl start "$SERVICE" || true
    if ! api_healthy; then
        journalctl -u "$SERVICE" -n 40 --no-pager 2>/dev/null || true
        restore_backend
        fail "The new backend did not report Healthy at $API_HEALTH_URL; the previous release was restored."
    fi
    echo "Backend is healthy: $(curl -fsS --max-time 5 "$API_HEALTH_URL" | head -c 200)"
fi

if [ "$deploy_frontend" = yes ]; then
    step "Frontend: staging $web_src"
    mkdir -p "$stage/web"
    rsync -a --delete "$web_src"/ "$stage/web/"
    rm -f "$stage/web/web.config"

    if [ -f "$WEB_DIR/index.html" ]; then
        step "Frontend: keeping the live files as previous-web"
        mkdir -p "$RELEASES_DIR/previous-web"
        rsync -a --delete "$WEB_DIR"/ "$RELEASES_DIR/previous-web/"
    fi

    step "Frontend: installing into $WEB_DIR"
    mkdir -p "$WEB_DIR"
    # New hashed files first, index.html last, then remove files of the old release.
    rsync -a --exclude index.html "$stage/web/" "$WEB_DIR/"
    rsync -a "$stage/web/index.html" "$WEB_DIR/index.html"
    rsync -a --delete "$stage/web/" "$WEB_DIR/"
    chown -R root:root "$WEB_DIR"
    chmod -R a+rX "$WEB_DIR"
fi

if [ -n "$public_host" ]; then
    step "Site check through nginx: https://$public_host"
    code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 15 --resolve "$public_host:443:127.0.0.1" "https://$public_host/" || true)"
    [ "$code" = 200 ] || fail "The release is installed, but https://$public_host/ answered '$code' through nginx (expected 200): check 'nginx -t', the certificate and 'systemctl status nginx'."
    health="$(curl -sS --max-time 15 --resolve "$public_host:443:127.0.0.1" "https://$public_host/health/ready" || true)"
    echo "$health" | grep -q '"status":"Healthy"' || fail "The release is installed, but https://$public_host/health/ready is not Healthy through nginx: $health"
    echo "https://$public_host/ → 200, /health/ready → Healthy"
else
    echo "::warning::Platform__BaseUrl not found in $ENV_FILE; skipped the check through nginx."
fi

# Keep the newest releases only.
find "$RELEASES_DIR" -mindepth 1 -maxdepth 1 -type d -name '20*' -printf '%f\n' | sort -r | tail -n +$((KEEP_RELEASES + 1)) |
    while read -r old; do rm -rf "${RELEASES_DIR:?}/$old"; done

log_line "succeeded release=$release_id"
echo
echo "Deployment $release_id succeeded."
