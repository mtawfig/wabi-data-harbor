# Wabi Insight — CI/CD on Linux, step by step (GitHub Actions, deployments started by you)

This guide connects your Ubuntu server to GitHub so that you deploy Wabi Insight from the **Actions** tab with one click. Nothing deploys automatically. Every step has a **Check** (already done?), a **Do** and a **Verify** command, like the deployment guide.

The Windows Server equivalent is [DEVOPS_WINDOWS.md](DEVOPS_WINDOWS.md). Setting up the server itself is [DEPLOYMENT_LINUX.md](DEPLOYMENT_LINUX.md).

## How it works

| Part | When it runs | What it does |
|---|---|---|
| **CI**: `.github/workflows/ci.yml` (already in place) | Automatically on every pull request and push to `main`; manually from Actions | API build, NuGet vulnerability audit, unit + SQL Server integration tests; web lint, npm audit, tests, production build; Docker images build and smoke-start |
| **CD**: `.github/workflows/deploy-linux.yml` | **Only when you click "Run workflow"** | Builds the parts you choose on GitHub's runners, then installs them on your server through the self-hosted runner |

```
 You: Actions → "Deploy (Linux)" → Run workflow
   │   component: frontend+backend | backend | frontend      migrate database: on/off      run tests: on/off      ref: (optional)
   ▼
 GitHub-hosted runners (build-release.yml)
   ├─ backend : restore, build, unit tests, dotnet publish  ──► artifact wabi-insight-api
   └─ frontend: npm ci, lint, tests, production build       ──► artifact wabi-insight-web
   ▼
 Self-hosted runner on YOUR server (outbound HTTPS only, no inbound port)
   └─ sudo /usr/local/sbin/wabi-insight-deploy (installed by you, root-owned; the runner may run only this as root)
        backend : stage → keep previous → stop service → install → [backup + migrate] → start → wait for /health/ready
                  (failure → previous release restored automatically, run marked failed)
        frontend: stage → keep previous → install (index.html last)
        both    : final check through nginx: https://<domain>/ = 200 and /health/ready = Healthy
```

No server secrets live in GitHub. The server keeps its own configuration (`/etc/wabi-insight/api.env`, `appsettings.json`), and the runner connects out to GitHub, so you don't open SSH to the internet.

### Run options

| Input | Values | Meaning |
|---|---|---|
| **Use workflow from** | a branch (normally `main`) | The code that is built. |
| **component** | `frontend+backend` (default), `backend`, `frontend` | What to deploy. |
| **migrate_database** | off (default) / on | Apply EF Core migrations. Runs only together with the backend, after a fresh database backup. Ignored, with a warning, for `frontend`. |
| **run_tests** | on (default) / off | Unit tests (API) and lint + tests (web) before packaging. CI has already run the full suite on the pull request. |
| **ref** | empty, or a commit SHA / tag | Deploy that exact version instead of the branch tip, e.g. to roll back (step 9). |

## 0. Prerequisites

1. **The server is prepared** with [DEPLOYMENT_LINUX.md](DEPLOYMENT_LINUX.md) steps **1–29**: packages, firewall, SQL Server Express, the database and its login, the ASP.NET Core runtime, the service account and folders, the server files from GitHub, `api.env`, the systemd unit, the backup script, nginx + certificate. That guide also covers this CI/CD setup for beginners (steps 30–37); this page is the reference behind it. With CI/CD the server needs no Node.js, git clone or build (those are only in the guide's appendix, *Installing without GitHub Actions*). The first run creates the schema, and the first administrator comes right after it (step 6 below).
2. **The workflows are on `main`.** GitHub lists a manually started workflow only once its file is on the default branch.
3. **Your GitHub account** has admin rights on the repository, to add a runner and an environment.
4. **Outbound HTTPS** from the server to `github.com`, `api.github.com`, `*.actions.githubusercontent.com`, `codeload.github.com`, `objects.githubusercontent.com` and `*.blob.core.windows.net` (artifact storage).

**Check** in a root shell on the server (`sudo -i`):

```bash
systemctl cat wabi-insight-api >/dev/null && echo "unit: ok"
[ -f /etc/wabi-insight/api.env ] && grep -q '^Platform__BaseUrl=https://' /etc/wabi-insight/api.env && echo "api.env: ok"
dotnet --list-runtimes | grep -q 'Microsoft.AspNetCore.App 10\.' && echo "aspnetcore 10: ok"
nginx -t 2>&1 | grep -q successful && echo "nginx: ok"
[ -x /usr/local/sbin/wabi-insight-backup.sh ] && echo "backup script: ok"
for t in rsync curl flock; do command -v $t >/dev/null && echo "$t: ok"; done
curl -s -o /dev/null -w 'github.com reachable: %{http_code}\n' https://api.github.com
```

**Verify:** every line prints `ok`, and GitHub answers `200`.

## 1. A GitHub environment for the server

Each deployment is recorded under the repository's **Environments** (history, URL, optional approval).

**Check:** GitHub → repository → **Settings → Environments**. Is `production-linux` listed?

**Do:**

1. **Settings → Environments → New environment**, name `production-linux` → **Configure environment**.
2. **Deployment branches and tags:** *Selected branches and tags* → **Add deployment branch or tag rule** → `main`. Only code from `main` can then be deployed. (When you roll back with `ref`, the run is still started from `main`.)
3. Optional: **Required reviewers**: add yourself to get an approval prompt before the server is touched.
4. **Environment variables → Add variable**: `WI_PUBLIC_URL` = `https://insight.example.com` (your domain). It's shown as the link of each deployment.

Required reviewers and branch rules for **private** repositories need GitHub Pro, Team or Enterprise. On GitHub Free the workflow still runs, but without those rules.

**Verify:** `production-linux` is listed, with the branch rule `main` (and the variable).

## 2. The runner user

The runner runs as an unprivileged user. Its only root right is the deploy script (step 4).

**Check:** `id github-runner`

**Do:**

```bash
useradd --create-home --shell /bin/bash github-runner
```

**Verify:** `id github-runner` prints `uid=…(github-runner)`, and `sudo -l -U github-runner` lists no commands yet.

## 3. Install the self-hosted runner

**Check:**

```bash
ls /opt/actions-runner/config.sh 2>/dev/null
systemctl list-units --all 'actions.runner.*' --no-legend
```

**Do:**

1. On GitHub: **Settings → Actions → Runners → New self-hosted runner → Linux / x64**. Copy the token from the `./config.sh … --token XXXX` line. It's valid for one hour.
2. On the server, download the current runner and check its SHA-256 against the release notes. The GitHub page from 1. shows the same download, with its version and hash, if you prefer to paste those commands:

```bash
mkdir -p /opt/actions-runner && cd /opt/actions-runner
curl -fsSL https://api.github.com/repos/actions/runner/releases/latest -o /tmp/runner-release.json
RUNNER_VERSION=$(python3 -c "import json;print(json.load(open('/tmp/runner-release.json'))['tag_name'].lstrip('v'))")
RUNNER_SHA=$(python3 -c "import json,re;b=json.load(open('/tmp/runner-release.json'))['body'];m=re.search(r'BEGIN SHA linux-x64 -->([0-9a-f]{64})',b);print(m.group(1) if m else '')")
curl -fsSL -o actions-runner.tar.gz "https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz"
if [ -n "$RUNNER_SHA" ]; then echo "${RUNNER_SHA}  actions-runner.tar.gz" | sha256sum -c -     # actions-runner.tar.gz: OK
else sha256sum actions-runner.tar.gz; echo "Compare this hash with the one on GitHub's 'New self-hosted runner' page."; fi
tar xzf actions-runner.tar.gz && rm actions-runner.tar.gz
./bin/installdependencies.sh
chown -R github-runner:github-runner /opt/actions-runner
```

3. Register it with the repository and the label `wabi-insight`, then install it as a service running as `github-runner`:

```bash
read -rsp 'Runner registration token: ' RUNNER_TOKEN; echo
runuser -u github-runner -- ./config.sh --unattended --url https://github.com/mtawfig/wabi-data-harbor \
  --token "$RUNNER_TOKEN" --name "$(hostname)-wabi-insight" --labels wabi-insight --work _work --replace
./svc.sh install github-runner
./svc.sh start
unset RUNNER_TOKEN
```

**Verify:**

```bash
./svc.sh status | grep -E 'Active: active \(running\)'
journalctl -u 'actions.runner.*' -n 5 --no-pager | grep -i 'Listening for Jobs'
```

On GitHub, **Settings → Actions → Runners** shows the runner **Idle** with the labels `self-hosted`, `Linux`, `X64`, `wabi-insight`.

## 4. The deploy script and its sudo rule

The runner installs releases through `/usr/local/sbin/wabi-insight-deploy`, which is root-owned. The runner can't modify it, and a single sudo rule lets it run only this script. The script is in the repository at `deploy/cicd/linux/wabi-insight-deploy.sh`.

**Check:**

```bash
/usr/local/sbin/wabi-insight-deploy --script-version 2>/dev/null       # 1 = installed
sudo -l -U github-runner | grep wabi-insight-deploy
```

**Do:** download the script of `main` with a GitHub token (*Contents: read*) and install it. If you have a checkout of the repository, copy the file instead.

```bash
read -rsp 'GitHub token (Contents: read): ' GH_TOKEN; echo
curl -fsSL -H "Authorization: Bearer $GH_TOKEN" -H "Accept: application/vnd.github.raw" \
  "https://api.github.com/repos/mtawfig/wabi-data-harbor/contents/deploy/cicd/linux/wabi-insight-deploy.sh?ref=main" \
  -o /tmp/wabi-insight-deploy.sh; unset GH_TOKEN
bash -n /tmp/wabi-insight-deploy.sh && head -3 /tmp/wabi-insight-deploy.sh            # review before installing
install -o root -g root -m 755 /tmp/wabi-insight-deploy.sh /usr/local/sbin/wabi-insight-deploy && rm /tmp/wabi-insight-deploy.sh
cat > /etc/sudoers.d/wabi-insight-deploy <<'EOF'
# GitHub Actions runner may run the Wabi Insight deploy script as root, and nothing else (docs/DEVOPS_LINUX.md).
github-runner ALL=(root) NOPASSWD: /usr/local/sbin/wabi-insight-deploy
EOF
chmod 440 /etc/sudoers.d/wabi-insight-deploy
visudo -cf /etc/sudoers.d/wabi-insight-deploy
```

The script reads optional overrides from `/etc/wabi-insight/deploy.conf` (for example `HEALTH_ATTEMPTS=60` or other paths). Without that file, it uses the paths of the deployment guide.

**Verify:**

```bash
stat -c '%U:%G %a' /usr/local/sbin/wabi-insight-deploy                          # root:root 755
runuser -u github-runner -- sudo -n /usr/local/sbin/wabi-insight-deploy --script-version   # 1
runuser -u github-runner -- sudo -n /usr/bin/id 2>&1 | head -1                   # "a password is required" (no other root rights)
```

## 5. The first run

**Check:** **Actions** shows **Deploy (Linux)** in the workflow list (it appears once this file is on `main`), and the runner is **Idle** (step 3).

**Do:**

1. GitHub → **Actions** → **Deploy (Linux)** → **Run workflow**.
2. **Use workflow from:** `main`.
3. **component:** `frontend+backend`.
4. **migrate_database:** ✅ (creates the schema on a new database).
5. **run_tests:** ✅.
6. **ref:** leave empty.
7. **Run workflow**, then open the run.

| Job | Runs on | Duration (first run) |
|---|---|---|
| Build / Resolve version | GitHub | ~20 s |
| Build / Backend (.NET 10) | GitHub | 2–4 min |
| Build / Frontend (Angular 22) | GitHub | 2–4 min (in parallel) |
| Deploy to Linux server | your server | 15–60 s (+ waiting for approval, if configured) |

The deploy log shows each stage (`==> Backend: …`, `==> Database: …`, `==> Frontend: …`) and ends with `Deployment <release> succeeded.` The run summary lists the component, migration flag, version and who started it.

**Verify** on the server:

```bash
tail -3 /var/log/wabi-insight-deploy.log                      # … started … / … succeeded release=…
cat /opt/wabi-insight/api/deploy-info.json; echo              # version = the commit you deployed
systemctl is-active wabi-insight-api                          # active
curl -s https://$(sed -n 's#^Platform__BaseUrl=https://##p' /etc/wabi-insight/api.env)/health/ready; echo   # Healthy
ls /opt/wabi-insight/releases                                 # one folder per release (the newest 5 are kept)
```

Then open `https://<your domain>` in a browser.

## 6. New server only: create the first Platform Administrator

The first run created the schema. Create the administrator once, with [DEPLOYMENT_LINUX.md step 36](DEPLOYMENT_LINUX.md#36-create-the-first-platform-administrator-once). It uses the API the pipeline just installed in `/opt/wabi-insight/api`. Then sign in and change the password ([step 37](DEPLOYMENT_LINUX.md#37-go-live-checks)).

**Check:** `0` means not done yet: `SQLCMDPASSWORD=… sqlcmd -S 127.0.0.1,1433 -U wabiinsightdbowner -d WabiInsightDB -C -h -1 -Q "SELECT COUNT(*) FROM dbo.Administrators"`.

## 7. Everyday deployments

| What changed | component | migrate_database |
|---|---|---|
| Only the Angular app (screens, texts, translations) | `frontend` | off |
| API code without a new migration | `backend` | off |
| A pull request that adds a migration (files under `src/WabiInsight.Infrastructure/Persistence/Migrations/`) | `backend` or `frontend+backend` | **on** |
| Both, no migration | `frontend+backend` | off |
| Not sure | `frontend+backend` | on (an empty migration step is harmless: "Configuration database is up to date") |

To see whether migrations are pending, compare the deployed version with `main`:

```bash
cat /opt/wabi-insight/api/deploy-info.json                         # "version":"<deployed sha>"
git log --oneline <deployed sha>..origin/main -- src/WabiInsight.Infrastructure/Persistence/Migrations   # any output = migrate
```

## 8. If a deployment fails

| Where | What happened | What to do |
|---|---|---|
| A **Build** job | Tests or the build failed. Nothing reached the server. | Fix it in a pull request and run again. |
| Deploy: *Check the server's deploy script* | Script missing, or the sudo rule is wrong. | Step 4. A **warning** that the script "differs" means the repository has a newer deploy script. Reinstall it (step 4). |
| Deploy: backend `did not report Healthy` / `Migration failed` | The previous backend was **restored automatically** and is running. | Read the job log (the last 40 journal lines are printed) and `journalctl -u wabi-insight-api`. Migrations already applied stay applied. Restore the backup taken just before (`/var/opt/mssql/backup`) if you must go back. |
| Deploy: `The release is installed, but …` (site check) | The release is live, but nginx or the certificate didn't answer correctly. | `nginx -t`, `systemctl status nginx`, `certbot certificates`. |
| Deploy waits forever on `Waiting for a runner` | The runner is offline. | `cd /opt/actions-runner && ./svc.sh status`, then `./svc.sh start`. |
| `Another deployment is running` | Two runs at once. | The workflow queues runs (`concurrency`). The server lock protects manual runs too. Wait and run again. |

## 9. Rolling back

- **Code only (no migration in between):** **Run workflow** with **ref** = the commit SHA you want back, taken from `/var/log/wabi-insight-deploy.log` (`version=`) or the earlier run's summary. Choose the same component and leave **migrate_database** off.
- **Instantly, without GitHub:** the previous release is kept on the server:

```bash
rsync -a --delete --exclude appsettings.Local.json /opt/wabi-insight/releases/previous-api/ /opt/wabi-insight/api/ && systemctl restart wabi-insight-api
rsync -a --delete /opt/wabi-insight/releases/previous-web/ /var/www/wabi-insight/
```

- **Across a migration:** the old code may not work with the new schema. Restore the database backup taken by that deployment (`RESTORE DATABASE … WITH REPLACE`, guide step 43), then deploy the old version.

## 10. Keeping it up to date

| What | How |
|---|---|
| The deploy script changed in a pull request | The deploy job warns *"differs from … in this version"*. Repeat step 4 (download and `install`). |
| The runner | It updates itself automatically. Check with `./svc.sh status` and the version on the Runners page. |
| A new runner token (moving or re-registering) | `./svc.sh stop && ./svc.sh uninstall && runuser -u github-runner -- ./config.sh remove --token <removal token>`, then step 3 again. |

## 11. Security notes

- The runner belongs to this private repository only, and the deploy workflow can only be started manually by people with write access. Keep self-hosted runners out of pull-request workflows. CI uses GitHub-hosted runners.
- The runner user can't read the server's own settings (`/etc/wabi-insight/api.env` is root-only, the key ring belongs to `wabi-insight`) and has no root rights except `wabi-insight-deploy`. That script validates its inputs and runs nothing from the workspace except the published API itself (migrations), as the `wabi-insight` service user.
- Artifacts contain `appsettings.json` with the committed configuration (owner decision G). They're private to the repository and expire after 7 days.
- With **required reviewers** on `production-linux`, every deployment waits for your approval in GitHub before the server job starts.
