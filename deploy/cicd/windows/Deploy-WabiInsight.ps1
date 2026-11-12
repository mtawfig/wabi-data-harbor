<#
.SYNOPSIS
    Installs a Wabi Insight release on Windows Server + IIS (docs/DEVOPS_WINDOWS.md).

.DESCRIPTION
    Installed once by an administrator as C:\wabitex-apps\wabi-insight\deploy\Deploy-WabiInsight.ps1 (writable by Administrators and
    SYSTEM only) and run by the GitHub Actions self-hosted runner service. The installed copy, not the one in the
    runner's workspace, is what runs.

    Backend:  stage -> keep the running release as "previous-api" -> app_offline.htm -> wait until the API has released
              its files -> copy -> [backup + migrate] -> remove app_offline.htm -> wait for /health/ready ->
              [storage-check when files are in Azure Blob Storage].
              If the copy, the migration or the health check fails, the previous binaries are restored (database changes
              made by a migration are not rolled back; restore the backup). /health/ready is not Healthy while the
              database is behind the release, so a release that needs migrations cannot be installed without them.
    Frontend: stage -> keep the live files as "previous-web" -> copy (index.html last) -> check the site over HTTPS.
    appsettings.Local.json (server settings) is never overwritten or deleted.

    Azure SQL Database (the connection string names *.database.windows.net, docs/DEPLOYMENT_WINDOWS_AZURE.md Appendix B): Azure keeps
    the backups, so before migrations the script records the point-in-time restore point (deploy log and run summary);
    the "Wabi Insight backup" task, when present, still runs first (on that setup it copies the key ring to Azure).

    Layout (docs/DEPLOYMENT_WINDOWS_SERVER_2019.md step 10): <Root>\web\api (API site), <Root>\web\app (frontend site),
    <Root>\releases, <Root>\logs. Generated and uploaded files live outside Root (D:\wabi-insight\files, Storage:RootPath)
    and are never touched by a deployment.

.EXAMPLE
    .\Deploy-WabiInsight.ps1 -Component 'frontend+backend' -Migrate yes -ApiDir D:\a\_temp\release\api -WebDir D:\a\_temp\release\web -Version 1a2b3c4
#>
[CmdletBinding()]
param(
    [ValidateSet('backend', 'frontend', 'frontend+backend')][string]$Component,
    [ValidateSet('yes', 'no')][string]$Migrate = 'no',
    [string]$ApiDir,
    [string]$WebDir,
    [ValidatePattern('^[A-Za-z0-9._-]{1,64}$')][string]$Version = 'manual',
    [string]$Actor = $env:USERNAME,
    [string]$Root = 'C:\wabitex-apps\wabi-insight',
    [string]$ApiHealthUrl = 'http://127.0.0.1:5080/health/ready',
    [string]$BackupTaskName = 'Wabi Insight backup',
    [int]$KeepReleases = 5,
    [int]$HealthAttempts = 45,
    [int]$OfflineWaitSeconds = 120,
    [switch]$ScriptVersion
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$DeployScriptVersion = 4
if ($ScriptVersion) { $DeployScriptVersion; return }
if (-not $Component) { throw "-Component is required: backend, frontend or frontend+backend." }

$apiTarget = Join-Path $Root 'web\api'
$webTarget = Join-Path $Root 'web\app'
$releases = Join-Path $Root 'releases'
$logFile = Join-Path $Root 'logs\deploy.log'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$Actor = ($Actor -replace '[^A-Za-z0-9._@-]', '')
if ($Actor.Length -gt 64) { $Actor = $Actor.Substring(0, 64) }

function Write-DeployLog([string]$Message) {
    New-Item -ItemType Directory -Force -Path (Split-Path $logFile) | Out-Null
    Add-Content -Path $logFile -Value ('{0} actor={1} version={2} component={3} migrate={4} {5}' -f (Get-Date).ToUniversalTime().ToString('s'), $Actor, $Version, $Component, $Migrate, $Message)
}
function Stop-Deploy([string]$Message) {
    Write-Output "::error::$Message"
    Write-DeployLog "FAILED: $Message"
    throw $Message
}
function Write-Step([string]$Message) { Write-Output ''; Write-Output "==> $Message" }
function Invoke-Native([scriptblock]$Command) {
    # Windows PowerShell 5.1 turns a native program's stderr into terminating errors when output is redirected (as on
    # the runner); exit codes are checked explicitly instead.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command 2>&1 | ForEach-Object { "$_" } } finally { $ErrorActionPreference = $previous }
}
function Invoke-Robocopy([string]$From, [string]$To, [string[]]$Options) {
    New-Item -ItemType Directory -Force -Path $To | Out-Null
    $output = Invoke-Native { robocopy.exe $From $To @Options /R:3 /W:2 /NP /NFL /NDL /NJH /NJS }
    if ($LASTEXITCODE -ge 8) { $output | Write-Output; Stop-Deploy "robocopy $From -> $To failed with exit code $LASTEXITCODE." }
    $global:LASTEXITCODE = 0
}
function Read-SettingsFile([string]$Path) {
    # appsettings.json may contain full-line // comments (owner decision G); Windows PowerShell's JSON reader rejects them.
    if (-not (Test-Path $Path)) { return $null }
    $text = (Get-Content $Path | Where-Object { $_ -notmatch '^\s*//' }) -join "`n"
    try { return ($text | ConvertFrom-Json) } catch { return $null }
}
function Get-ServerSetting([scriptblock]$Pick) {
    # The installed API's settings: appsettings.Local.json (this server) first, then appsettings.json.
    foreach ($name in 'appsettings.Local.json', 'appsettings.json') {
        $settings = Read-SettingsFile (Join-Path $apiTarget $name)
        if ($null -ne $settings) {
            $value = $null
            try { $value = & $Pick $settings } catch { $value = $null }
            if ($value) { return [string]$value }
        }
    }
    return $null
}
function Test-ApiHealthy {
    for ($i = 1; $i -le $HealthAttempts; $i++) {
        try {
            $response = Invoke-WebRequest -Uri $ApiHealthUrl -UseBasicParsing -TimeoutSec 10
            if ($response.Content -match '"status":"Healthy"') { return $true }
        } catch { Start-Sleep -Milliseconds 100 }
        Start-Sleep -Seconds 2
    }
    return $false
}
function Test-ApiFilesReleased {
    # Windows keeps the DLLs of a running application locked: they can be replaced only once the worker process has
    # let go of them. Opening the main DLL for exclusive access succeeds exactly then.
    $dll = Join-Path $apiTarget 'WabiInsight.Api.dll'
    if (-not (Test-Path $dll)) { return $true }
    try {
        $handle = [System.IO.File]::Open($dll, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $handle.Dispose()
        return $true
    } catch { return $false }
}
function Set-AppOffline([bool]$Offline) {
    $file = Join-Path $apiTarget 'app_offline.htm'
    if ($Offline) {
        Set-Content -Path $file -Value '<!doctype html><title>Updating</title><p>Wabi Insight is being updated. Please try again in a minute.</p>' -Encoding UTF8
        # The ASP.NET Core Module notices the file, lets requests in flight finish (a long analysis can take a while) and
        # shuts the application down. Wait until the files are really free instead of a fixed pause.
        Start-Sleep -Seconds 3
        for ($i = 1; $i -le $OfflineWaitSeconds; $i++) {
            if (Test-ApiFilesReleased) { return $true }
            Start-Sleep -Seconds 1
        }
        return (Test-ApiFilesReleased)
    } elseif (Test-Path $file) {
        Remove-Item $file -Force
    }
    return $true
}
function Write-StartupErrors([datetime]$Since) {
    # The ASP.NET Core Module logs startup failures (with the app's captured stdout/stderr) to the Application event log.
    try {
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'IIS AspNetCore Module V2'; StartTime = $Since } -MaxEvents 5 -ErrorAction Stop |
            ForEach-Object { Write-Output "---- $($_.TimeCreated.ToString('s')) $($_.LevelDisplayName)"; Write-Output $_.Message }
    } catch { Write-Output '(no ASP.NET Core Module events in the Application log)' }
}
function Restore-Backend {
    Write-Output '::warning::Restoring the previous backend release.'
    $previous = Join-Path $releases 'previous-api'
    if (Test-Path (Join-Path $previous 'WabiInsight.Api.dll')) {
        if (-not (Set-AppOffline $true)) { Write-Output '::warning::The API files are still in use; restoring anyway.' }
        Invoke-Robocopy $previous $apiTarget @('/MIR', '/XF', 'appsettings.Local.json', 'app_offline.htm')
        Set-AppOffline $false | Out-Null
        if (Test-ApiHealthy) { Write-Output 'Previous backend release is running again.' }
        else { Write-Output "::error::The previous release did not become healthy either: see $Root\logs and the Application event log." }
    } else {
        Set-AppOffline $false | Out-Null
        Write-Output '::error::No previous backend release to restore (first deployment).'
    }
}

$deployBackend = $Component -like '*backend*'
$deployFrontend = $Component -like '*frontend*'
if ($Migrate -eq 'yes' -and -not $deployBackend) {
    Write-Output '::warning::Database migration runs only together with the backend; ignored for a frontend-only deployment.'
    $Migrate = 'no'
}
if ($deployBackend) {
    if (-not $ApiDir -or -not (Test-Path (Join-Path $ApiDir 'WabiInsight.Api.dll'))) { Stop-Deploy "-ApiDir must point at the published API (WabiInsight.Api.dll not found in '$ApiDir')." }
    if (-not (Test-Path $dotnet)) { Stop-Deploy "$dotnet not found: install the ASP.NET Core Hosting Bundle (docs/DEPLOYMENT_WINDOWS_SERVER_2019.md step 13, or docs/DEPLOYMENT_WINDOWS_AZURE.md step 19)." }
    if (-not (Test-Path (Join-Path $apiTarget 'appsettings.Local.json'))) { Stop-Deploy "$apiTarget\appsettings.Local.json is missing: create it first (docs/DEPLOYMENT_WINDOWS_SERVER_2019.md step 24, or docs/DEPLOYMENT_WINDOWS_AZURE.md step 30)." }
}
if ($deployFrontend) {
    if (-not $WebDir -or -not (Test-Path (Join-Path $WebDir 'browser\index.html'))) { Stop-Deploy "-WebDir must contain browser\index.html (the Angular build) and iis\web.config." }
}

# One deployment at a time.
$mutex = New-Object System.Threading.Mutex($false, 'Global\WabiInsightDeploy')
if (-not $mutex.WaitOne(0)) { Stop-Deploy 'Another deployment is running.' }

try {
    $releaseId = '{0}-{1}' -f (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss'), ($Version.Substring(0, [Math]::Min(12, $Version.Length)))
    $stage = Join-Path $releases $releaseId
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Write-DeployLog "started release=$releaseId"
    Write-Output "Wabi Insight deployment $releaseId (component: $Component, migrate: $Migrate, by: $Actor)"

    $publicHost = $null
    $localSettingsPath = Join-Path $apiTarget 'appsettings.Local.json'
    if (Test-Path $localSettingsPath) {
        $baseUrl = (Get-Content $localSettingsPath -Raw | ConvertFrom-Json).Platform.BaseUrl
        if ($baseUrl) { $publicHost = ([Uri]$baseUrl).Host }
    }

    if ($deployBackend) {
        Write-Step "Backend: staging $ApiDir"
        $stageApi = Join-Path $stage 'api'
        Invoke-Robocopy $ApiDir $stageApi @('/MIR')
        'appsettings.Local.json', 'appsettings.Local.example.json', 'appsettings.Development.json', 'app_offline.htm' |
            ForEach-Object { Remove-Item (Join-Path $stageApi $_) -Force -ErrorAction SilentlyContinue }
        @{ release = $releaseId; version = $Version; actor = $Actor; deployedAt = (Get-Date).ToUniversalTime().ToString('s') + 'Z' } |
            ConvertTo-Json | Set-Content (Join-Path $stageApi 'deploy-info.json') -Encoding UTF8

        if (Test-Path (Join-Path $apiTarget 'WabiInsight.Api.dll')) {
            Write-Step 'Backend: keeping the running release as previous-api'
            Invoke-Robocopy $apiTarget (Join-Path $releases 'previous-api') @('/MIR', '/XF', 'app_offline.htm')
        }

        Write-Step 'Backend: taking the API offline (app_offline.htm)'
        if (-not (Set-AppOffline $true)) {
            # Nothing was copied yet: the running release is untouched, so it simply goes back online.
            Set-AppOffline $false | Out-Null
            Stop-Deploy "The API did not release its files within $OfflineWaitSeconds seconds (a request is still running, or another program holds them). Nothing was changed and the site is back online; try again in a few minutes."
        }

        Write-Step "Backend: installing into $apiTarget"
        try {
            Invoke-Robocopy $stageApi $apiTarget @('/MIR', '/XF', 'appsettings.Local.json', 'app_offline.htm')
        } catch {
            # A copy that stops half-way leaves old and new files mixed: put the previous release back before failing.
            Restore-Backend
            throw
        }

        if ($Migrate -eq 'yes') {
            $connection = Get-ServerSetting { param($s) $s.ConnectionStrings.ConfigurationDatabase }
            $azureSql = $connection -match '\.database\.windows\.net'
            if ($azureSql) {
                # Azure SQL keeps automatic backups: record the moment to restore to if this migration must be undone.
                $restorePoint = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
                Write-Step "Database: Azure SQL Database, point-in-time restore point $restorePoint (UTC)"
                Write-Output 'To undo this migration: Azure portal -> SQL database -> Restore -> point in time = this moment (docs/DEPLOYMENT_WINDOWS_AZURE.md Appendix B).'
                Write-DeployLog "azure-sql restore-point=$restorePoint"
                if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value "Azure SQL point-in-time restore point before the migration: **$restorePoint** (UTC)`n" -Encoding UTF8 }
            }
            $task = Get-ScheduledTask -TaskName $BackupTaskName -ErrorAction SilentlyContinue
            if ($task) {
                Write-Step "Database: backup before migration (scheduled task '$BackupTaskName')"
                Start-ScheduledTask -TaskName $BackupTaskName
                Start-Sleep -Seconds 3
                $deadline = (Get-Date).AddMinutes(15)
                while ((Get-ScheduledTask -TaskName $BackupTaskName).State -eq 'Running' -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 3 }
                $result = (Get-ScheduledTaskInfo -TaskName $BackupTaskName).LastTaskResult
                if ($result -ne 0) { Restore-Backend; Stop-Deploy "Backup task finished with result $result; nothing was migrated." }
            } elseif ($azureSql) {
                Write-Output "::warning::Scheduled task '$BackupTaskName' not found: the key ring was not copied before the migration (docs/DEPLOYMENT_WINDOWS_AZURE.md step 36). The database itself is covered by the restore point above."
            } else {
                Write-Output "::warning::Scheduled task '$BackupTaskName' not found: migrating without a fresh backup (docs/DEPLOYMENT_WINDOWS_SERVER_2019.md step 30)."
            }
            Write-Step 'Database: applying migrations'
            Push-Location $apiTarget
            try {
                $env:ASPNETCORE_ENVIRONMENT = 'Production'
                Invoke-Native { & $dotnet (Join-Path $apiTarget 'WabiInsight.Api.dll') migrate }
                $migrateExit = $LASTEXITCODE
            } finally { Pop-Location }
            if ($migrateExit -ne 0) {
                Restore-Backend
                Stop-Deploy 'Migration failed; the previous backend was restored. Applied migrations (if any) stay; see the backup.'
            }
        }

        Write-Step 'Backend: bringing the API online'
        $onlineAt = Get-Date
        Set-AppOffline $false | Out-Null
        if (-not (Test-ApiHealthy)) {
            # What the API itself reports, for example a database that is behind this release (run with migrations).
            $report = (Invoke-Native { curl.exe -sS --max-time 10 $ApiHealthUrl }) -join ''
            $global:LASTEXITCODE = 0
            if ($report) { Write-Output "Health report: $report" }
            if ($report -match '"name":"database-schema","status":"Unhealthy"') {
                Write-Output '::error::The database is behind this release: deploy again with "Apply database migrations" ticked.'
            }
            Write-StartupErrors $onlineAt
            Restore-Backend
            Stop-Deploy "The new backend did not report Healthy at $ApiHealthUrl; the previous release was restored. The startup error is printed above (Application event log, source 'IIS AspNetCore Module V2')."
        }
        $healthJson = (Invoke-WebRequest -Uri $ApiHealthUrl -UseBasicParsing -TimeoutSec 10).Content
        Write-Output ('Backend is healthy: ' + $healthJson.Substring(0, [Math]::Min(200, $healthJson.Length)))

        $storageProvider = Get-ServerSetting { param($s) $s.Storage.Provider }
        if ($storageProvider -eq 'AzureBlob') {
            # Files live in Azure Blob Storage: prove the installed API can write, read and delete there.
            Write-Step 'Storage: checking the Azure Blob Storage container (storage-check)'
            Push-Location $apiTarget
            try {
                $env:ASPNETCORE_ENVIRONMENT = 'Production'
                Invoke-Native { & $dotnet (Join-Path $apiTarget 'WabiInsight.Api.dll') storage-check }
                $storageExit = $LASTEXITCODE
            } finally { Pop-Location }
            $global:LASTEXITCODE = 0
            if ($storageExit -ne 0) { Stop-Deploy 'Storage check failed: the new backend is running, but it cannot use the Azure Blob Storage container. Check Storage:AzureBlob (connection string, container name) in appsettings.Local.json (docs/DEPLOYMENT_WINDOWS_AZURE.md steps 9, 10, 11 and 30).' }
        }
    }

    if ($deployFrontend) {
        Write-Step "Frontend: staging $WebDir"
        $stageWeb = Join-Path $stage 'web'
        Invoke-Robocopy (Join-Path $WebDir 'browser') $stageWeb @('/MIR')
        $iisConfig = Join-Path $WebDir 'iis\web.config'
        if (Test-Path $iisConfig) { Copy-Item $iisConfig (Join-Path $stageWeb 'web.config') -Force }
        elseif (Test-Path (Join-Path $webTarget 'web.config')) { Copy-Item (Join-Path $webTarget 'web.config') (Join-Path $stageWeb 'web.config') -Force }
        else { Stop-Deploy 'No IIS web.config in the release and none on the server.' }

        if (Test-Path (Join-Path $webTarget 'index.html')) {
            Write-Step 'Frontend: keeping the live files as previous-web'
            Invoke-Robocopy $webTarget (Join-Path $releases 'previous-web') @('/MIR')
        }

        Write-Step "Frontend: installing into $webTarget"
        # New hashed files first, index.html last, then remove files of the old release.
        Invoke-Robocopy $stageWeb $webTarget @('/E', '/XF', 'index.html')
        Copy-Item (Join-Path $stageWeb 'index.html') (Join-Path $webTarget 'index.html') -Force
        Invoke-Robocopy $stageWeb $webTarget @('/MIR')
    }

    if ($publicHost) {
        Write-Step "Site check through IIS: https://$publicHost"
        $code = (Invoke-Native { curl.exe -sS -o NUL -w '%{http_code}' --max-time 15 --resolve "${publicHost}:443:127.0.0.1" "https://$publicHost/" }) -join ''
        if ($code -notmatch '200$') { Stop-Deploy "The release is installed, but https://$publicHost/ answered '$code' through IIS (expected 200): check the WabiInsight site, its https binding and certificate." }
        $health = (Invoke-Native { curl.exe -sS --max-time 15 --resolve "${publicHost}:443:127.0.0.1" "https://$publicHost/health/ready" }) -join ''
        if ($health -notmatch '"status":"Healthy"') { Stop-Deploy "The release is installed, but https://$publicHost/health/ready is not Healthy through IIS (ARR proxy?): $health" }
        $global:LASTEXITCODE = 0
        Write-Output "https://$publicHost/ -> 200, /health/ready -> Healthy"
    } else {
        Write-Output '::warning::Platform.BaseUrl not found in appsettings.Local.json; skipped the check through IIS.'
    }

    # Keep the newest releases only.
    Get-ChildItem $releases -Directory | Where-Object Name -like '20*' | Sort-Object Name -Descending | Select-Object -Skip $KeepReleases |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force }

    Write-DeployLog "succeeded release=$releaseId"
    Write-Output ''
    Write-Output "Deployment $releaseId succeeded."
} finally {
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
