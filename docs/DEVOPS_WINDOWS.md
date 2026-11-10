# Wabi Insight — CI/CD on Windows Server, step by step (GitHub Actions, deployments started by you)

This guide connects your Windows Server 2019 (IIS) machine to GitHub so that you deploy Wabi Insight from the **Actions** tab with one click. Nothing deploys automatically. Every step has a **Check** (already done?), a **Do** and a **Verify** command, like the deployment guide.

The Linux equivalent is [DEVOPS_LINUX.md](DEVOPS_LINUX.md). Setting up the server itself is [DEPLOYMENT_WINDOWS_SERVER_2019.md](DEPLOYMENT_WINDOWS_SERVER_2019.md). With the database in **Azure SQL** and the files in **Azure Blob Storage**, follow [DEPLOYMENT_WINDOWS_AZURE.md](DEPLOYMENT_WINDOWS_AZURE.md) instead: it covers the server and this CI/CD setup in one beginner's guide, with the same workflow and deploy script (which then records the Azure SQL restore point before migrations and checks the blob container after each backend deployment).

## How it works

| Part | When it runs | What it does |
|---|---|---|
| **CI**: `.github/workflows/ci.yml` (already in place) | Automatically on every pull request and push to `main`; manually from Actions | API build, NuGet vulnerability audit, unit + SQL Server integration tests; web lint, npm audit, tests, production build; Docker images build and smoke-start |
| **CD**: `.github/workflows/deploy-windows.yml` | **Only when you click "Run workflow"** | Builds the parts you choose on GitHub's runners, then installs them on your server through the self-hosted runner |

```
 You: Actions → "Deploy (Windows)" → Run workflow
   │   component: frontend+backend | backend | frontend      migrate database: on/off      run tests: on/off      ref: (optional)
   ▼
 GitHub-hosted runners (build-release.yml)
   ├─ backend : restore, build, unit tests, dotnet publish + IIS web.config  ──► artifact wabi-insight-api
   └─ frontend: npm ci, lint, tests, production build + IIS web.config        ──► artifact wabi-insight-web
   ▼
 Self-hosted runner service on YOUR server (outbound HTTPS only, no inbound port, no WinRM/RDP from GitHub)
   └─ C:\wabitex-apps\wabi-insight\deploy\Deploy-WabiInsight.ps1 (installed by you; the runner runs this copy)
        backend : stage → keep previous → app_offline.htm → install → [backup task + migrate] → online → wait for /health/ready
                  (failure → previous release restored automatically, run marked failed)
        frontend: stage → keep previous → install (index.html last)
        both    : final check through IIS + ARR: https://<domain>/ = 200 and /health/ready = Healthy
```

No server secrets live in GitHub. The server keeps its own `appsettings.Local.json`, which deployments never overwrite or delete.

Server layout (set up by the deployment guide, step 10):

| Folder | Role in a deployment |
|---|---|
| `C:\wabitex-apps\wabi-insight\web\api` | Running API: replaced by each backend deployment (`appsettings.Local.json` kept) |
| `C:\wabitex-apps\wabi-insight\web\app` | Running frontend: replaced by each frontend deployment |
| `C:\wabitex-apps\wabi-insight\releases` | The last 5 staged releases, plus `previous-api` and `previous-web` for roll-back |
| `C:\wabitex-apps\wabi-insight\deploy` | The installed deploy script |
| `C:\wabitex-apps\wabi-insight\logs\deploy.log` | One line per deployment |
| `C:\wabitex-apps\wabi-insight\src` | Source checkout. Not needed with CI/CD (GitHub builds), and not touched by deployments. |
| `D:\wabi-insight\files\<kind>` | Generated and uploaded files. **Never touched by a deployment.** |
| `D:\wabi-insight\backup` | Nightly backups; the *Wabi Insight backup* task runs before every migration. |

### Run options

| Input | Values | Meaning |
|---|---|---|
| **Use workflow from** | a branch (normally `main`) | The code that is built. |
| **component** | `frontend+backend` (default), `backend`, `frontend` | What to deploy. |
| **migrate_database** | off (default) / on | Apply EF Core migrations. Runs only together with the backend, after the *Wabi Insight backup* task. Ignored, with a warning, for `frontend`. |
| **run_tests** | on (default) / off | Unit tests (API) and lint + tests (web) before packaging. CI has already run the full suite on the pull request. |
| **ref** | empty, or a commit SHA / tag | Deploy that exact version instead of the branch tip, e.g. to roll back (step 9). |

## 0. Prerequisites

1. **The server is prepared** with [DEPLOYMENT_WINDOWS_SERVER_2019.md](DEPLOYMENT_WINDOWS_SERVER_2019.md) steps **1–26 and 30**: IIS and its modules, Hosting Bundle, SQL Server Express, the database and its login, folders and pools, `appsettings.Local.json`, both IIS sites, the certificate, the firewall and the backup task. That guide also covers this CI/CD setup for beginners (steps 27–33); this page is the reference behind it. With CI/CD the server needs no Git, .NET SDK or Node.js (those are only in the guide's Appendix A, *Installing without GitHub Actions*). The first run creates the schema, and the first administrator comes right after it (step 6 below). Until the first run, the health checks fail because `C:\wabitex-apps\wabi-insight\web\api` is still empty. That's expected.
2. **The workflows are on `main`.** GitHub lists a manually started workflow only once its file is on the default branch.
3. **Your GitHub account** has admin rights on the repository, to add a runner and an environment.
4. **Outbound HTTPS** from the server to `github.com`, `api.github.com`, `*.actions.githubusercontent.com`, `codeload.github.com`, `objects.githubusercontent.com` and `*.blob.core.windows.net` (artifact storage).

**Check** in an elevated Windows PowerShell (run the session settings and helpers from step 5 of the deployment guide first, and `$WI = Import-Clixml C:\wabitex-apps\wabi-insight\wabi-insight.vars.xml`):

```powershell
Import-Module WebAdministration
[pscustomobject]@{
    HostingBundle   = Test-Path "$env:ProgramFiles\dotnet\dotnet.exe"
    ApiSite         = [bool](Get-Website -Name 'WabiInsightApi' -ErrorAction SilentlyContinue)
    WebSite         = [bool](Get-Website -Name 'WabiInsight' -ErrorAction SilentlyContinue)
    LocalSettings   = Test-Path 'C:\wabitex-apps\wabi-insight\web\api\appsettings.Local.json'
    FilesFolder     = Test-Path $WI.Files
    BackupTask      = [bool](Get-ScheduledTask -TaskName 'Wabi Insight backup' -ErrorAction SilentlyContinue)
    CurlExe         = [bool](Get-Command curl.exe -ErrorAction SilentlyContinue)
    GitHubReachable = (Invoke-WebRequest 'https://api.github.com' -UseBasicParsing).StatusCode -eq 200
}
```

**Verify:** every value is `True`.

## 1. A GitHub environment for the server

Each deployment is recorded under the repository's **Environments** (history, URL, optional approval).

**Check:** GitHub → repository → **Settings → Environments**. Is `production-windows` listed?

**Do:**

1. **Settings → Environments → New environment**, name `production-windows` → **Configure environment**.
2. **Deployment branches and tags:** *Selected branches and tags* → **Add deployment branch or tag rule** → `main`. (When you roll back with `ref`, the run is still started from `main`.)
3. Optional: **Required reviewers**: add yourself to get an approval prompt before the server is touched.
4. **Environment variables → Add variable**: `WI_PUBLIC_URL` = `https://insight.example.com` (your domain).

Required reviewers and branch rules for **private** repositories need GitHub Pro, Team or Enterprise. On GitHub Free the workflow still runs, but without those rules.

**Verify:** `production-windows` is listed, with the branch rule `main` (and the variable).

## 2. Download the self-hosted runner

**Check:**

```powershell
Test-Path 'C:\actions-runner\config.cmd'
Get-Service -Name 'actions.runner.*' -ErrorAction SilentlyContinue | Select-Object Name, Status, StartType
```

**Do:** create the folder with Administrators/SYSTEM-only access (it will hold the runner's credentials), download the current runner and check its SHA-256:

```powershell
. {
$runnerDir = 'C:\actions-runner'
New-Item -ItemType Directory -Force -Path $runnerDir | Out-Null
icacls $runnerDir /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' 'BUILTIN\Administrators:(OI)(CI)F' | Out-Null
$release = Invoke-RestMethod 'https://api.github.com/repos/actions/runner/releases/latest' -Headers @{ 'User-Agent' = 'wabi-insight-setup' }
$asset = $release.assets | Where-Object name -match '^actions-runner-win-x64-[\d\.]+\.zip$' | Select-Object -First 1
$zip = Join-Path $runnerDir $asset.name
Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing
$expected = if ($release.body -match 'BEGIN SHA win-x64 -->([0-9a-f]{64})') { $Matches[1] } else { $null }
$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
if ($expected) { if ($actual -ne $expected) { throw 'Runner checksum mismatch' } else { 'checksum OK' } }
else { "SHA-256 ${actual}: compare it with the hash on GitHub's 'New self-hosted runner' page." }
Expand-Archive $zip -DestinationPath $runnerDir -Force
Remove-Item $zip
}
```

The GitHub page **Settings → Actions → Runners → New self-hosted runner → Windows / x64** shows the same download with its version and hash, if you prefer to paste those commands.

**Verify:** `Test-Path C:\actions-runner\config.cmd` returns `True`, and `icacls C:\actions-runner` lists only SYSTEM and Administrators.

## 3. Register the runner and run it as a service (LocalSystem)

The service runs as **LocalSystem**: deployments write to `C:\wabitex-apps\wabi-insight` (Administrators and SYSTEM only), take the API offline through `app_offline.htm`, start the backup task and run migrations with the server's `appsettings.Local.json`.

**Check:** `Get-CimInstance Win32_Service -Filter "Name LIKE 'actions.runner.%'" | Select-Object Name, State, StartName` (`StartName` must be `LocalSystem`).

**Do:**

1. On GitHub: **Settings → Actions → Runners → New self-hosted runner → Windows**. Copy the token from the `./config.cmd … --token XXXX` line. It's valid for one hour.
2. On the server:

```powershell
. {
Set-Location 'C:\actions-runner'
$token = Read-Host 'Runner registration token'
.\config.cmd --unattended --url https://github.com/mtawfig/wabi-data-harbor --token $token `
    --name "$env:COMPUTERNAME-wabi-insight" --labels wabi-insight --work _work --runasservice --replace
Remove-Variable token
$service = Get-Service -Name 'actions.runner.*' | Select-Object -First 1
sc.exe config $service.Name obj= LocalSystem | Out-Null
sc.exe config $service.Name start= delayed-auto | Out-Null
Restart-Service $service.Name
}
```

**Verify:**

```powershell
Get-CimInstance Win32_Service -Filter "Name LIKE 'actions.runner.%'" | Select-Object Name, State, StartName, StartMode   # Running, LocalSystem, Auto
Get-Content (Get-ChildItem 'C:\actions-runner\_diag' -Filter 'Runner_*.log' | Sort-Object LastWriteTime | Select-Object -Last 1).FullName -Tail 20 | Select-String 'Listening for Jobs'
```

On GitHub, **Settings → Actions → Runners** shows the runner **Idle** with the labels `self-hosted`, `Windows`, `X64`, `wabi-insight`.

## 4. Install the deploy script

The runner runs `C:\wabitex-apps\wabi-insight\deploy\Deploy-WabiInsight.ps1`, never a copy from its workspace. The folder inherits the Administrators/SYSTEM-only permissions of `C:\wabitex-apps\wabi-insight` (deployment guide step 10). The script is in the repository at `deploy/cicd/windows/Deploy-WabiInsight.ps1`.

**Check:**

```powershell
if (Test-Path 'C:\wabitex-apps\wabi-insight\deploy\Deploy-WabiInsight.ps1') { & 'C:\wabitex-apps\wabi-insight\deploy\Deploy-WabiInsight.ps1' -ScriptVersion } else { 'not installed' }   # 3 = current version installed
```

**Do:** download the script of `main` with a GitHub token (*Contents: read*). If you have a checkout of the repository, copy the file instead.

```powershell
. {
$deployDir = 'C:\wabitex-apps\wabi-insight\deploy'
New-Item -ItemType Directory -Force -Path $deployDir | Out-Null
$ghToken = Read-Host 'GitHub token (Contents: read)' -AsSecureString
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($ghToken))
Invoke-WebRequest -UseBasicParsing -OutFile (Join-Path $deployDir 'Deploy-WabiInsight.ps1') `
    -Uri 'https://api.github.com/repos/mtawfig/wabi-data-harbor/contents/deploy/cicd/windows/Deploy-WabiInsight.ps1?ref=main' `
    -Headers @{ Authorization = "Bearer $plain"; Accept = 'application/vnd.github.raw'; 'User-Agent' = 'wabi-insight-setup' }
Remove-Variable plain, ghToken
Unblock-File (Join-Path $deployDir 'Deploy-WabiInsight.ps1')        # downloaded files are blocked by default
Get-Content (Join-Path $deployDir 'Deploy-WabiInsight.ps1') -TotalCount 5   # review before use
}
```

**Verify:**

```powershell
& 'C:\wabitex-apps\wabi-insight\deploy\Deploy-WabiInsight.ps1' -ScriptVersion          # 3
icacls 'C:\wabitex-apps\wabi-insight\deploy'                                            # SYSTEM and Administrators only
Get-ExecutionPolicy -Scope LocalMachine                                   # RemoteSigned (default) or stricter-but-allowing local scripts
```

## 5. The first run

**Check:** **Actions** shows **Deploy (Windows)** in the workflow list (it appears once this file is on `main`), and the runner is **Idle** (step 3).

**Do:**

1. GitHub → **Actions** → **Deploy (Windows)** → **Run workflow**.
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
| Deploy to Windows server | your server | 1–2 min (+ waiting for approval, if configured) |

The deploy log shows each stage (`==> Backend: …`, `==> Database: …`, `==> Frontend: …`) and ends with `Deployment <release> succeeded.` The run summary lists the component, migration flag, version and who started it.

**Verify** on the server:

```powershell
Get-Content 'C:\wabitex-apps\wabi-insight\logs\deploy.log' -Tail 3                                  # … started … / … succeeded release=…
Get-Content 'C:\wabitex-apps\wabi-insight\web\api\deploy-info.json'                                     # version = the commit you deployed
Test-Path 'C:\wabitex-apps\wabi-insight\web\api\app_offline.htm'                                        # False
(Invoke-WebRequest 'http://127.0.0.1:5080/health/ready' -UseBasicParsing).Content     # Healthy
(Invoke-WebRequest "https://$($WI.Domain)/health/ready" -UseBasicParsing).Content     # Healthy, through IIS + ARR
Get-ChildItem 'C:\wabitex-apps\wabi-insight\releases' | Select-Object Name                          # one folder per release (the newest 5 are kept)
```

Then open `https://<your domain>` in a browser.

## 6. New server only: create the first Platform Administrator

The first run created the schema. Create the administrator once, with [DEPLOYMENT_WINDOWS_SERVER_2019.md step 32](DEPLOYMENT_WINDOWS_SERVER_2019.md#32-create-the-first-platform-administrator-once). It uses the API the pipeline just installed in `C:\wabitex-apps\wabi-insight\web\api`. Then sign in and change the password ([step 33](DEPLOYMENT_WINDOWS_SERVER_2019.md#33-go-live-checks)).

## 7. Everyday deployments

| What changed | component | migrate_database |
|---|---|---|
| Only the Angular app (screens, texts, translations) | `frontend` | off |
| API code without a new migration | `backend` | off |
| A pull request that adds a migration (files under `src/WabiInsight.Infrastructure/Persistence/Migrations/`) | `backend` or `frontend+backend` | **on** |
| Both, no migration | `frontend+backend` | off |
| Not sure | `frontend+backend` | on (an empty migration step is harmless: "Configuration database is up to date") |

`C:\wabitex-apps\wabi-insight\web\api\deploy-info.json` tells you which commit is live. On GitHub, compare it with `main` (`https://github.com/mtawfig/wabi-data-harbor/compare/<deployed sha>...main`) and look for new files under `Persistence/Migrations`.

## 8. If a deployment fails

| Where | What happened | What to do |
|---|---|---|
| A **Build** job | Tests or the build failed. Nothing reached the server. | Fix it in a pull request and run again. |
| Deploy: *Check the server's deploy script* | Script missing. | Step 4. A **warning** that the script "differs" means the repository has a newer deploy script. Reinstall it (step 4). |
| Deploy: backend `did not report Healthy` / `Migration failed` | The previous backend was **restored automatically** and is running. | The job log shows the startup error from the Application event log (source *IIS AspNetCore Module V2*, also in Event Viewer). Migrations already applied stay applied. Restore the backup taken just before (`D:\wabi-insight\backup`) if you must go back. |
| Deploy: `The release is installed, but …` (site check) | The release is live, but IIS, ARR or the certificate didn't answer correctly. | Deployment guide steps 15, 25 and 26 **Verify**. |
| Deploy: `robocopy … failed` | A file was locked (rare: the app wasn't fully stopped). | Run again. If it persists, `Restart-WebAppPool WabiInsightApi` and run again. |
| Deploy waits forever on `Waiting for a runner` | The runner service is stopped or offline. | `Get-Service 'actions.runner.*' \| Start-Service`. |
| `Another deployment is running` | Two runs at once. | The workflow queues runs (`concurrency`). Wait and run again. |

## 9. Rolling back

- **Code only (no migration in between):** **Run workflow** with **ref** = the commit SHA you want back, taken from `C:\wabitex-apps\wabi-insight\logs\deploy.log` (`version=`) or the earlier run's summary. Choose the same component and leave **migrate_database** off.
- **Instantly, without GitHub:** the previous release is kept on the server:

```powershell
Set-Content 'C:\wabitex-apps\wabi-insight\web\api\app_offline.htm' 'Updating'; Start-Sleep -Seconds 5
robocopy 'C:\wabitex-apps\wabi-insight\releases\previous-api' 'C:\wabitex-apps\wabi-insight\web\api' /MIR /XF appsettings.Local.json app_offline.htm /NFL /NDL /NJH /NJS
Remove-Item 'C:\wabitex-apps\wabi-insight\web\api\app_offline.htm'
robocopy 'C:\wabitex-apps\wabi-insight\releases\previous-web' 'C:\wabitex-apps\wabi-insight\web\app' /MIR /NFL /NDL /NJH /NJS
```

- **Across a migration:** the old code may not work with the new schema. Restore the database backup taken by that deployment (deployment guide step 39), then deploy the old version.

## 10. Keeping it up to date

| What | How |
|---|---|
| The deploy script changed in a pull request | The deploy job warns *"differs from … in this version"*. Repeat step 4. |
| The runner | It updates itself automatically. Check its version on the Runners page. |
| Re-registering (new server name, moved repository) | `Set-Location C:\actions-runner; .\config.cmd remove --token <removal token>`, then steps 3–4 again. |

## 11. Security notes

- The runner belongs to this private repository only, and the deploy workflow can only be started manually by people with write access. Keep self-hosted runners out of pull-request workflows. CI uses GitHub-hosted runners.
- The runner runs as LocalSystem because deployments change protected folders. Its folder (`C:\actions-runner`) and the deploy script (`C:\wabitex-apps\wabi-insight\deploy`) are writable by Administrators and SYSTEM only, so the workflow can't replace the installed deploy logic.
- `appsettings.Local.json` (database password, server paths) is never in an artifact, and deployments never overwrite or delete it.
- Artifacts contain `appsettings.json` with the committed configuration (owner decision G). They're private to the repository and expire after 7 days.
- With **required reviewers** on `production-windows`, every deployment waits for your approval in GitHub before the server job starts.
