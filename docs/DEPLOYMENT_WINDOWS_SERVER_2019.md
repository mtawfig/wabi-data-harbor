# Wabi Insight — Windows Server 2019 deployment, step by step (IIS + SQL Server Express)

This guide takes a fresh Windows Server 2019 (Desktop Experience) machine to a live Wabi Insight at `https://<your domain>`. Every step has three parts:

- **Check:** a command that tells you whether the step is already done. If it is, skip **Do**.
- **Do:** the commands that make the change.
- **Verify:** a command whose output proves the step worked. Don't continue until it does.

The result:

```
 browser ──HTTPS 443──► IIS site "WabiInsight" ──┬─ /             Angular files (C:\wabitex-apps\wabi-insight\web\app)
                        (URL Rewrite + ARR)      └─ /api, /health ─► IIS site "WabiInsightApi" on 127.0.0.1:5080
                                                                       (ASP.NET Core Module, in-process)
                                                                          │
                                                                          └─► SQL Server Express (localhost,1433 · WabiInsightDB)
```

The API has its own IIS site bound to the loopback address. The public site forwards `/api` and `/health` to it, so the browser sees a single origin and the session cookie stays first-party. Don't mount the API as an IIS sub-application named `api`: IIS would strip `/api` from every path and all API routes would return 404.

The Linux equivalent is [DEPLOYMENT_LINUX.md](DEPLOYMENT_LINUX.md). A summary of all configuration keys is in [DEPLOYMENT.md](DEPLOYMENT.md).

## SQL Server Express works

Wabi Insight runs on **SQL Server Express 2019, 2022 and 2025**. The full integration suite (120 tests: migrations, multi-tenant isolation, row scopes, the support desk, the sample-database scripts) passes on each of these Express versions. The app uses no Standard- or Enterprise-only feature and needs no SQL Server Agent.

| Limit | Express 2019 / 2022 | Express 2025 | What it means here |
|---|---|---|---|
| Database size | 10 GB per database | 50 GB per database | `WabiInsightDB` is about 5 MB after setup and grows with history, audit and usage events, so several years fit. The sample databases are under 50 MB each. |
| Memory | 1,410 MB buffer pool | 1,410 MB buffer pool | Enough for the configuration database. |
| CPU | 1 socket or 4 cores (whichever is lower) | same | Fine for the API's short queries. |
| SQL Server Agent | not included | not included | Backups are scheduled with Task Scheduler instead (step 25). |
| Backup compression | not supported | not supported | Backups run without `COMPRESSION`. |

Express needs three changes from its defaults, all covered in step 11: it installs as the **named instance `SQLEXPRESS`** with **TCP/IP disabled** and **Windows authentication only**. The API needs TCP on a fixed port (1433) and SQL logins (mixed mode). If your SQL Server Express is on another machine, apply step 11.4 on that machine, skip the rest of step 11, and use its address in steps 12 and 17.

## 0. Before you start

| You need | Example | Notes |
|---|---|---|
| Windows Server 2019 Standard/Datacenter, **Desktop Experience**, x64 | 4 vCPU, 8 GB RAM, 80 GB disk | Fully patched (step 3). |
| A DNS name pointing at the server | `insight.example.com` → server public IP | For the certificate and email links. |
| Remote Desktop with a local administrator | | |
| Access to the GitHub repository | personal access token with *Contents: read* | The repository is private. |
| Outbound internet | Microsoft download sites, nodejs.org, GitHub, npm, NuGet, PowerShell Gallery, `api.anthropic.com:443`, your SMTP host on 587/465 | |
| Ports 80 and 443 open to the server | cloud firewall / security group | 80 is needed for Let's Encrypt validation and the redirect. |

## 1. Open an administrator PowerShell and set the variables

Start **Windows PowerShell** with **Run as administrator**. Every command in this guide runs in that window.

**Check:**

```powershell
$PSVersionTable.PSVersion.ToString()                                                   # 5.1.x
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)   # True
```

**Do:** session settings (repeat them whenever you open a new window):

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'      # makes downloads much faster in Windows PowerShell
$ErrorActionPreference = 'Stop'
```

Set the values for your installation once, and save them:

```powershell
$WI = [ordered]@{
    Domain     = 'insight.example.com'        # public DNS name of the site
    CertEmail  = 'it@example.com'             # Let's Encrypt expiry notices
    AdminEmail = 'support@wabitex.com'        # first Platform Administrator (a real mailbox)
    Repo       = 'https://github.com/mtawfig/wabi-data-harbor.git'
    Branch     = 'main'
    Root       = 'C:\wabitex-apps\wabi-insight'   # application: src, web\app, web\api, keys, logs, releases, deploy
    Files      = 'D:\wabi-insight\files'          # generated and uploaded files (one folder per kind)
    Backup     = 'D:\wabi-insight\backup'         # nightly backups (step 25)
}
New-Item -ItemType Directory -Force -Path $WI.Root | Out-Null
$WI | Export-Clixml (Join-Path $WI.Root 'wabi-insight.vars.xml')
```

In a new window, load them again with `$WI = Import-Clixml C:\wabitex-apps\wabi-insight\wabi-insight.vars.xml`.

Helper functions used throughout the guide (paste them into every new window):

```powershell
function Update-WiPath {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
}
function Get-WiDownload([string]$Url, [string]$FileName) {
    $folder = Join-Path $WI.Root 'install'
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $path = Join-Path $folder $FileName
    if (-not (Test-Path $path)) { Invoke-WebRequest -Uri $Url -OutFile $path -UseBasicParsing }
    $path
}
function New-WiSecret([int]$Length = 24) {
    # Letters and digits from a cryptographic generator, plus '-' and a digit so SQL Server's password policy is always met.
    $chars = [char[]]'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $bytes = New-Object byte[] $Length
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    'Wi-' + (-join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })) + '-7'
}
```

**Verify:** `$WI` prints your values, and `Get-Command New-WiSecret` finds the function.

## 2. Check the server

```powershell
(Get-CimInstance Win32_OperatingSystem) | Select-Object Caption, Version, OSArchitecture   # Windows Server 2019 …, 10.0.17763, 64-bit
Get-CimInstance Win32_ComputerSystem | Select-Object NumberOfLogicalProcessors, @{n='RAM_GB';e={[math]::Round($_.TotalPhysicalMemory/1GB,1)}}
Get-PSDrive C | Select-Object @{n='Free_GB';e={[math]::Round($_.Free/1GB,1)}}            # >= 30 GB free
```

**Verify** that DNS points at this server. Both lines must show the same address:

```powershell
(Resolve-DnsName $WI.Domain -Type A | Where-Object Type -eq 'A' | Select-Object -First 1).IPAddress
Invoke-RestMethod -Uri 'https://api.ipify.org' -UseBasicParsing
```

## 3. Windows updates and pending reboots

**Check:**

```powershell
Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 3 HotFixID, InstalledOn   # recent cumulative update
$pending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
           (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
"Reboot pending: $pending"
```

**Do:** install all updates with **Settings → Update & Security → Windows Update** (or `sconfig`, option 6), and restart if asked. Then reopen PowerShell as administrator and run the session settings and helpers from step 1 again.

**Verify:** the **Check** block shows an update from the last month and `Reboot pending: False`.

## 4. Time zone and clock

**Check:**

```powershell
Get-TimeZone | Select-Object Id
w32tm /query /status | Select-String 'Source|Last Successful'
```

**Do** (only if needed; the application stores UTC):

```powershell
Set-TimeZone -Id 'Arabian Standard Time'      # optional, e.g. UAE
w32tm /resync
```

**Verify:** `w32tm /query /status` shows a recent `Last Successful Sync Time`.

## 5. Folders

| Folder | Contents |
|---|---|
| `C:\wabitex-apps\wabi-insight\src` | Source code (Git checkout used for building) |
| `C:\wabitex-apps\wabi-insight\web\app` | Running frontend: the Angular build (IIS site `WabiInsight`) |
| `C:\wabitex-apps\wabi-insight\web\api` | Running API (IIS site `WabiInsightApi`) |
| `C:\wabitex-apps\wabi-insight\keys` | Data Protection key ring. **Losing it makes every stored connection string and provider key unreadable.** |
| `C:\wabitex-apps\wabi-insight\logs`, `releases`, `deploy`, `install`, `tools` | API stdout logs (when enabled) and the deploy log, CI/CD release history, the deploy script, downloaded installers, win-acme |
| `D:\wabi-insight\files\<kind>` | Generated and uploaded files, one folder per kind (below). Inside each: `yyyy\MM\<random>.bin`. |
| `D:\wabi-insight\backup` | Nightly backups (step 25) |

The API stores every file under `Storage:RootPath` (`D:\wabi-insight\files`), in the folder of its kind:

| Folder under `D:\wabi-insight\files` | What is stored there |
|---|---|
| `analysis-reports` | Study PDFs generated by Ask Wabi Insight |
| `analysis-results` | Stored result sets of every answer (reopening history never re-queries the source) |
| `analysis-exports` | PDF, DOCX and XLSX exports from My History and My Reports |
| `organization-logos` | Uploaded organization logos |
| `support-attachments` | Files attached to support tickets |

The API creates a missing folder by itself. Creating them here makes the layout visible from the start.

**Check:**

```powershell
Get-PSDrive -Name D -ErrorAction SilentlyContinue | Select-Object Name, @{n='Free_GB';e={[math]::Round($_.Free/1GB,1)}}   # the D: drive exists
$appFolders  = 'src', 'web\app', 'web\api', 'keys', 'logs', 'releases', 'deploy', 'install', 'tools'
$fileFolders = 'analysis-reports', 'analysis-results', 'analysis-exports', 'organization-logos', 'support-attachments'
$appFolders  | ForEach-Object { '{0,-48} {1}' -f (Join-Path $WI.Root $_), (Test-Path (Join-Path $WI.Root $_)) }
$fileFolders | ForEach-Object { '{0,-48} {1}' -f (Join-Path $WI.Files $_), (Test-Path (Join-Path $WI.Files $_)) }
'{0,-48} {1}' -f $WI.Backup, (Test-Path $WI.Backup)
```

**Do:** create the folders and remove the inherited `Users` access on both roots. The application folder will hold `appsettings.json` with secrets, and the D: drive root grants `Users` access by default.

```powershell
$appFolders  | ForEach-Object { New-Item -ItemType Directory -Force -Path (Join-Path $WI.Root $_) | Out-Null }
$fileFolders | ForEach-Object { New-Item -ItemType Directory -Force -Path (Join-Path $WI.Files $_) | Out-Null }
New-Item -ItemType Directory -Force -Path $WI.Backup | Out-Null
$dataRoot = Split-Path $WI.Files -Parent                                  # D:\wabi-insight
foreach ($folder in $WI.Root, $dataRoot) {
    icacls $folder /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' 'BUILTIN\Administrators:(OI)(CI)F' | Out-Null
}
```

**Verify:**

```powershell
icacls $WI.Root                          # only NT AUTHORITY\SYSTEM and BUILTIN\Administrators
icacls (Split-Path $WI.Files -Parent)    # only NT AUTHORITY\SYSTEM and BUILTIN\Administrators
Get-ChildItem $WI.Files | Select-Object Name   # the five folders above
```

## 6. IIS with the required features

**Check:**

```powershell
$iisFeatures = 'Web-Server','Web-Default-Doc','Web-Static-Content','Web-Http-Errors','Web-Http-Redirect','Web-Http-Logging',
               'Web-Request-Monitor','Web-Filtering','Web-Stat-Compression','Web-Dyn-Compression','Web-AppInit','Web-Mgmt-Console'
Get-WindowsFeature $iisFeatures | Format-Table Name, InstallState
```

**Do:**

```powershell
Install-WindowsFeature -Name $iisFeatures
```

**Verify:**

```powershell
(Get-WindowsFeature $iisFeatures | Where-Object InstallState -ne 'Installed').Count     # 0
Get-Service W3SVC | Select-Object Status, StartType                                      # Running, Automatic
```

## 7. Microsoft Visual C++ runtime (x64)

The native libraries used for PDF and chart rendering load the Visual C++ runtime. Installing it prevents `DllNotFoundException` at the first report.

**Check:** `(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64' -ErrorAction SilentlyContinue).Version`

**Do:**

```powershell
$vc = Get-WiDownload 'https://aka.ms/vs/17/release/vc_redist.x64.exe' 'vc_redist.x64.exe'
$p = Start-Process $vc -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru
"exit code $($p.ExitCode)"      # 0, 3010 (reboot later) or 1638 (a newer version is already installed)
```

**Verify:** the **Check** command prints a version such as `v14.4x.xxxxx`.

## 8. ASP.NET Core 10 Hosting Bundle

It installs the .NET 10 runtime and the ASP.NET Core Module for IIS. It must come **after** IIS (step 6).

**Check:**

```powershell
Test-Path "$env:ProgramFiles\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll"
if (Test-Path "$env:ProgramFiles\dotnet\dotnet.exe") { & "$env:ProgramFiles\dotnet\dotnet.exe" --list-runtimes | Select-String 'Microsoft.AspNetCore.App 10\.' }
```

**Do:**

```powershell
$hb = Get-WiDownload 'https://aka.ms/dotnet/10.0/dotnet-hosting-win.exe' 'dotnet-hosting-10.0-win.exe'
$p = Start-Process $hb -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru; "exit code $($p.ExitCode)"
net stop was /y
net start w3svc
Update-WiPath
```

**Verify:**

```powershell
Get-WebGlobalModule -Name AspNetCoreModuleV2 | Select-Object Name, Image
dotnet --list-runtimes | Select-String 'Microsoft.AspNetCore.App 10\.'          # Microsoft.AspNetCore.App 10.0.x
```

## 9. IIS URL Rewrite 2.1

**Check:** `Get-WebGlobalModule -Name RewriteModule -ErrorAction SilentlyContinue`

**Do:**

```powershell
$rw = Get-WiDownload 'https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi' 'rewrite_amd64_en-US.msi'
$p = Start-Process msiexec.exe -ArgumentList '/i', "`"$rw`"", '/qn', '/norestart' -Wait -PassThru; "exit code $($p.ExitCode)"
```

**Verify:** `Get-WebGlobalModule -Name RewriteModule` returns `RewriteModule …\rewrite.dll`.

## 10. Application Request Routing 3.0 (reverse proxy)

### 10.1 Install

**Check:** `Get-WebGlobalModule -Name ApplicationRequestRouting -ErrorAction SilentlyContinue`

**Do:**

```powershell
$arr = Get-WiDownload 'https://download.microsoft.com/download/E/9/8/E9849D6A-020E-47E4-9FD0-A023E99B54EB/requestRouter_amd64.msi' 'requestRouter_amd64.msi'
$p = Start-Process msiexec.exe -ArgumentList '/i', "`"$arr`"", '/qn', '/norestart' -Wait -PassThru; "exit code $($p.ExitCode)"
```

**Verify:** `Get-WebGlobalModule -Name ApplicationRequestRouting` returns the module.

### 10.2 Enable the proxy with a 10-minute timeout

Study reports with PDF generation take 1–3 minutes. ARR's default timeout is 2 minutes.

**Check:**

```powershell
Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' | Select-Object enabled, timeout, preserveHostHeader, includePortInXForwardedFor
```

**Do:**

```powershell
$proxy = @{ PSPath = 'MACHINE/WEBROOT/APPHOST'; Filter = 'system.webServer/proxy' }
Set-WebConfigurationProperty @proxy -Name enabled -Value $true
Set-WebConfigurationProperty @proxy -Name timeout -Value '00:10:00'
Set-WebConfigurationProperty @proxy -Name preserveHostHeader -Value $true
Set-WebConfigurationProperty @proxy -Name reverseRewriteHostInResponseHeaders -Value $false
Set-WebConfigurationProperty @proxy -Name includePortInXForwardedFor -Value $false
```

**Verify:** the **Check** command shows `True  00:10:00  True  False`.

### 10.3 Allow the rewrite rule to set `X-Forwarded-Proto`

**Check:** `Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/rewrite/allowedServerVariables/add' | Select-Object name`

**Do:**

```powershell
if (-not (Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "system.webServer/rewrite/allowedServerVariables/add[@name='HTTP_X_FORWARDED_PROTO']")) {
    Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/rewrite/allowedServerVariables' -Name '.' -Value @{ name = 'HTTP_X_FORWARDED_PROTO' }
}
```

**Verify:** the **Check** command lists `HTTP_X_FORWARDED_PROTO`.

## 11. SQL Server 2025 Express

Skip 11.1–11.3 if SQL Server Express is already installed (the **Check** in 11.1 finds it). Still apply **11.4** to it.

### 11.1 Check for an existing instance

```powershell
Get-Service -Name 'MSSQL$*' -ErrorAction SilentlyContinue | Select-Object Name, Status, StartType
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL' -ErrorAction SilentlyContinue)
```

`MSSQL$SQLEXPRESS` running means SQL Server Express is installed: go to 11.4.

### 11.2 Download the installation media

**Check:** `Get-ChildItem (Join-Path $WI.Root 'install\sql') -Filter 'SQLEXPR*.exe' -ErrorAction SilentlyContinue`

**Do:**

```powershell
$ssei = Get-WiDownload 'https://go.microsoft.com/fwlink/?linkid=2216019' 'SQL2025-SSEI-Expr.exe'
$media = Join-Path $WI.Root 'install\sql'
New-Item -ItemType Directory -Force -Path $media | Out-Null
Start-Process $ssei -ArgumentList '/ACTION=Download', "/MEDIAPATH=$media", '/MEDIATYPE=Core', '/QUIET' -Wait
```

If no `SQLEXPR*.exe` appears, run `SQL2025-SSEI-Expr.exe` from `C:\wabitex-apps\wabi-insight\install` by double-click, choose **Download Media → Express Core**, and save to `C:\wabitex-apps\wabi-insight\install\sql`. The link above always serves the current Express release (SQL Server 2025 as of this guide). SQL Server 2022 Express works the same way.

**Verify:** the **Check** command lists `SQLEXPR_x64_ENU.exe` (or similar).

### 11.3 Unattended installation with mixed-mode authentication and TCP enabled

**Check:** `Get-Service 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue`

**Do:** choose the `sa` password: at least 12 characters, mixed case, a digit and a symbol, no `"`. Keep it in your password manager. The application never uses it.

```powershell
$saSecure = Read-Host 'New sa password' -AsSecureString
$sa = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($saSecure))
$media = Join-Path $WI.Root 'install\sql'
$package = (Get-ChildItem $media -Filter 'SQLEXPR*.exe' | Select-Object -First 1).FullName
$setupDir = Join-Path $media 'setup'
Start-Process $package -ArgumentList '/q', "/x:`"$setupDir`"" -Wait
$arguments = @(
    '/Q', '/ACTION=Install', '/IACCEPTSQLSERVERLICENSETERMS', '/FEATURES=SQLEngine',
    '/INSTANCENAME=SQLEXPRESS', '/SQLSVCACCOUNT="NT Service\MSSQL$SQLEXPRESS"', '/SQLSVCSTARTUPTYPE=Automatic',
    '/SQLSYSADMINACCOUNTS="BUILTIN\Administrators"', '/SECURITYMODE=SQL', "/SAPWD=`"$sa`"",
    '/TCPENABLED=1', '/NPENABLED=0', '/BROWSERSVCSTARTUPTYPE=Disabled', '/UPDATEENABLED=False'
)
$p = Start-Process (Join-Path $setupDir 'setup.exe') -ArgumentList $arguments -Wait -PassThru
"setup exit code $($p.ExitCode)"      # 0 = success, 3010 = success, reboot required
Remove-Variable sa
```

If the exit code isn't 0 or 3010, read `C:\Program Files\Microsoft SQL Server\*\Setup Bootstrap\Log\Summary.txt`.

**Verify:**

```powershell
Get-Service 'MSSQL$SQLEXPRESS' | Select-Object Status, StartType        # Running, Automatic
Get-Content "$env:ProgramFiles\Microsoft SQL Server\*\Setup Bootstrap\Log\Summary.txt" | Select-String 'Final result|Exit code' | Select-Object -First 2
```

### 11.4 Fixed TCP port 1433 and mixed-mode authentication

The API connects to `localhost,1433`. Express defaults to a dynamic port and may be Windows-authentication only.

**Check:**

```powershell
$instanceId = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').SQLEXPRESS     # e.g. MSSQL17.SQLEXPRESS
$sqlKey = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\MSSQLServer"
[pscustomobject]@{
    LoginMode       = (Get-ItemProperty $sqlKey).LoginMode                                         # 2 = mixed mode
    TcpEnabled      = (Get-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp").Enabled                  # 1
    TcpPort         = (Get-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp\IPAll").TcpPort            # 1433
    TcpDynamicPorts = (Get-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp\IPAll").TcpDynamicPorts    # (empty)
}
Test-NetConnection -ComputerName localhost -Port 1433 -InformationLevel Quiet                     # True
```

**Do:**

```powershell
Set-ItemProperty $sqlKey -Name LoginMode -Value 2
Set-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp" -Name Enabled -Value 1
Set-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp\IPAll" -Name TcpDynamicPorts -Value ''
Set-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp\IPAll" -Name TcpPort -Value '1433'
Restart-Service 'MSSQL$SQLEXPRESS' -Force
```

**Verify:** run the **Check** block again: `2, 1, 1433, (empty)` and `True`.

### 11.5 PowerShell `SqlServer` module (`Invoke-Sqlcmd`)

This guide runs SQL with `Invoke-Sqlcmd`, which handles `GO` batches and works whether or not `sqlcmd.exe` is installed.

**Check:** `Get-Module -ListAvailable SqlServer | Select-Object Name, Version`

**Do:**

```powershell
Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force | Out-Null
Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
Install-Module -Name SqlServer -Scope AllUsers -AllowClobber -Force
```

Define the helper (paste it into every new window). It signs in with your Windows administrator account, which setup made `sysadmin`:

```powershell
Import-Module SqlServer
function Invoke-WiSql {
    param([string]$Query, [string]$InputFile, [string]$Database = 'master', [pscredential]$Credential)
    $p = @{ ServerInstance = 'localhost,1433'; Database = $Database; TrustServerCertificate = $true; QueryTimeout = 600; DisableVariables = $true; ErrorAction = 'Stop' }
    if ($InputFile) { $p.InputFile = $InputFile } else { $p.Query = $Query }
    if ($Credential) { $p.Credential = $Credential }
    Invoke-Sqlcmd @p
}
```

**Verify** the edition, the version and mixed mode:

```powershell
Invoke-WiSql "SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(64)) AS Edition, CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(32)) AS Version, CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int) AS WindowsAuthOnly"
# Express Edition (64-bit)   17.0.xxxx.x   0
```

## 12. Configuration database and its login

The API uses one SQL login that owns `WabiInsightDB` (`db_owner` on that database only, no server-wide rights). Migrations run under it.

**Check:**

```powershell
Invoke-WiSql "SELECT DB_ID(N'WabiInsightDB') AS DatabaseId, SUSER_ID(N'wabiinsightdbowner') AS LoginId"
```

If both have values (an earlier installation), keep the existing password and go to **Verify**.

**Do:**

```powershell
$dbPassword = New-WiSecret
"Store this password for wabiinsightdbowner in your password manager: $dbPassword"
Invoke-WiSql "IF DB_ID(N'WabiInsightDB') IS NULL CREATE DATABASE [WabiInsightDB];"
Invoke-WiSql "IF SUSER_ID(N'wabiinsightdbowner') IS NULL CREATE LOGIN [wabiinsightdbowner] WITH PASSWORD = N'$dbPassword', DEFAULT_DATABASE = [WabiInsightDB], CHECK_POLICY = ON;"
Invoke-WiSql -Database WabiInsightDB "IF DATABASE_PRINCIPAL_ID(N'wabiinsightdbowner') IS NULL CREATE USER [wabiinsightdbowner] FOR LOGIN [wabiinsightdbowner]; ALTER ROLE db_owner ADD MEMBER [wabiinsightdbowner];"
```

**Verify** by signing in as the new login:

```powershell
$dbCredential = New-Object pscredential 'wabiinsightdbowner', (ConvertTo-SecureString $dbPassword -AsPlainText -Force)
Invoke-WiSql -Database WabiInsightDB -Credential $dbCredential "SELECT DB_NAME() AS Db, IS_ROLEMEMBER('db_owner') AS DbOwner, IS_SRVROLEMEMBER('sysadmin') AS Sysadmin"
# WabiInsightDB 1 0
```

## 13. Build tools: Git, .NET 10 SDK, Node.js 22

These are only needed to build on this server. If you build on another machine, copy `C:\wabitex-apps\wabi-insight\web\api` and `C:\wabitex-apps\wabi-insight\web\app` instead and skip 13, 14 and the build parts of 15.

### 13.1 Git

**Check:** `Update-WiPath; if (Get-Command git -ErrorAction SilentlyContinue) { git --version } else { 'git not installed' }`

**Do:**

```powershell
$release = Invoke-RestMethod 'https://api.github.com/repos/git-for-windows/git/releases/latest' -Headers @{ 'User-Agent' = 'wabi-insight-setup' }
$asset = $release.assets | Where-Object name -match '^Git-[\d\.]+-64-bit\.exe$' | Select-Object -First 1
$git = Get-WiDownload $asset.browser_download_url $asset.name
$p = Start-Process $git -ArgumentList '/VERYSILENT', '/NORESTART', '/NOCANCEL', '/SP-' -Wait -PassThru; "exit code $($p.ExitCode)"
Update-WiPath
```

**Verify:** `git --version` prints `git version 2.x.windows.x`.

### 13.2 .NET 10 SDK

**Check:** `if (Get-Command dotnet -ErrorAction SilentlyContinue) { dotnet --list-sdks | Select-String '^10\.' }` (no output means not installed)

**Do:**

```powershell
$sdk = Get-WiDownload 'https://aka.ms/dotnet/10.0/dotnet-sdk-win-x64.exe' 'dotnet-sdk-10.0-win-x64.exe'
$p = Start-Process $sdk -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru; "exit code $($p.ExitCode)"
Update-WiPath
```

**Verify:** `dotnet --list-sdks` shows `10.0.x [C:\Program Files\dotnet\sdk]`.

### 13.3 Node.js 22

Angular 22 needs Node `^22.22.3`, `^24.15` or newer.

**Check:** `if (Get-Command node -ErrorAction SilentlyContinue) { node --version } else { 'node not installed' }` (v22.22.3 or later is fine).

**Do:** download the current Node 22 MSI and check its SHA-256 against nodejs.org:

```powershell
$sums = (Invoke-WebRequest 'https://nodejs.org/dist/latest-v22.x/SHASUMS256.txt' -UseBasicParsing).Content -split "`n"
$line = $sums | Where-Object { $_ -match '\snode-v22\.[\d\.]+-x64\.msi$' } | Select-Object -First 1
$hash, $name = $line.Trim() -split '\s+'
$msi = Get-WiDownload "https://nodejs.org/dist/latest-v22.x/$name" $name
if ((Get-FileHash $msi -Algorithm SHA256).Hash -ne $hash.ToUpper()) { throw "Checksum mismatch for $name" }
$p = Start-Process msiexec.exe -ArgumentList '/i', "`"$msi`"", '/qn', '/norestart' -Wait -PassThru; "exit code $($p.ExitCode)"
Update-WiPath
```

**Verify:** `node --version` prints v22.x, and `npm --version` prints a version.

## 14. Get the source code

**Check:** `if (Test-Path (Join-Path $WI.Root 'src\.git')) { git -C (Join-Path $WI.Root 'src') log -1 --oneline } else { 'no checkout yet' }`

**Do:** Git Credential Manager opens a sign-in window. Sign in to GitHub, or use your user name and the personal access token:

```powershell
git clone --branch $WI.Branch $WI.Repo (Join-Path $WI.Root 'src')
```

To update an existing checkout instead:

```powershell
$src = Join-Path $WI.Root 'src'
git -C $src fetch origin $WI.Branch; git -C $src checkout $WI.Branch; git -C $src pull --ff-only
```

**Verify:**

```powershell
git -C (Join-Path $WI.Root 'src') log -1 --oneline        # the commit you expect
Test-Path (Join-Path $WI.Root 'src\WabiInsight.slnx')      # True
```

## 15. Build the API and the web app

### 15.1 API

**Check:** `Test-Path (Join-Path $WI.Root 'web\api\WabiInsight.Api.dll')` (True means built before. Rebuilding is safe).

**Do:**

```powershell
$src = Join-Path $WI.Root 'src'; $api = Join-Path $WI.Root 'web\api'
dotnet publish "$src\src\WabiInsight.Api\WabiInsight.Api.csproj" -c Release -o $api
Copy-Item "$src\deploy\iis\web.config" (Join-Path $api 'web.config') -Force       # publish writes a generic web.config; use the repository's
```

`appsettings.json` in the publish folder holds the committed configuration (database connection string, SMTP, AI key: owner decision G). The publish never includes `appsettings.Local.json` or `appsettings.Development.json`.

**Verify:**

```powershell
Get-ChildItem $api -Filter 'appsettings*.json' | Select-Object Name                  # appsettings.json (+ appsettings.Local.json once step 17 is done)
Select-String -Path (Join-Path $api 'web.config') -Pattern 'AspNetCoreModuleV2|maxAllowedContentLength' | Select-Object -ExpandProperty Line
```

### 15.2 Web

**Check:** `Test-Path (Join-Path $WI.Root 'web\app\index.html')`

**Do:**

```powershell
$src = Join-Path $WI.Root 'src'; $web = Join-Path $WI.Root 'web\app'
Push-Location "$src\src\web"
npm ci
npm run build -- --configuration production
Pop-Location
robocopy "$src\src\web\dist\wabi-insight-web\browser" $web /MIR /XF web.config /NFL /NDL /NJH /NJS | Out-Null
Copy-Item "$src\deploy\iis\web.spa.config" (Join-Path $web 'web.config') -Force
```

**Verify:**

```powershell
Test-Path (Join-Path $web 'index.html'), (Join-Path $web 'web.config')                 # True True
Select-String -Path (Join-Path $web 'web.config') -Pattern 'API reverse proxy' -Quiet   # True
```

## 16. Application pools and folder permissions

| Pool | Site | Rights |
|---|---|---|
| `WabiInsightApi` | API | read `web\api`, modify `D:\wabi-insight\files`, `keys` and `logs` |
| `WabiInsightWeb` | public site | read `web\app` |

**Check:**

```powershell
Import-Module WebAdministration
Get-ChildItem IIS:\AppPools | Where-Object Name -like 'WabiInsight*' | Select-Object Name, State, managedRuntimeVersion, startMode
```

**Do:**

```powershell
Import-Module WebAdministration
foreach ($pool in 'WabiInsightApi', 'WabiInsightWeb') {
    if (-not (Test-Path "IIS:\AppPools\$pool")) { New-WebAppPool -Name $pool | Out-Null }
    Set-ItemProperty "IIS:\AppPools\$pool" -Name managedRuntimeVersion -Value ''           # No Managed Code
}
# API pool: always running (background e-mail outbox and support-access expiry), no idle shutdown, one recycle at 03:00
$apiPool = 'IIS:\AppPools\WabiInsightApi'
Set-ItemProperty $apiPool -Name startMode -Value 'AlwaysRunning'
Set-ItemProperty $apiPool -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty $apiPool -Name processModel.loadUserProfile -Value $true
Set-ItemProperty $apiPool -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
Clear-ItemProperty $apiPool -Name recycling.periodicRestart.schedule
New-ItemProperty $apiPool -Name recycling.periodicRestart.schedule -Value @{ value = '03:00:00' } | Out-Null

$root = $WI.Root
$dataRoot = Split-Path $WI.Files -Parent
# Parent folders: list/read on the folder itself only (no inheritance), so each pool can reach its own subfolder.
icacls "$root\web" /grant 'IIS AppPool\WabiInsightApi:(RX)' 'IIS AppPool\WabiInsightWeb:(RX)' 'IUSR:(RX)' | Out-Null
icacls $dataRoot /grant 'IIS AppPool\WabiInsightApi:(RX)' | Out-Null
icacls "$root\web\api" /grant 'IIS AppPool\WabiInsightApi:(OI)(CI)RX' | Out-Null
icacls "$root\web\app" /grant 'IIS AppPool\WabiInsightWeb:(OI)(CI)RX' 'IUSR:(OI)(CI)RX' | Out-Null
icacls $WI.Files /grant 'IIS AppPool\WabiInsightApi:(OI)(CI)M' | Out-Null
icacls "$root\keys" /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' 'BUILTIN\Administrators:(OI)(CI)F' 'IIS AppPool\WabiInsightApi:(OI)(CI)M' | Out-Null
icacls "$root\logs" /grant 'IIS AppPool\WabiInsightApi:(OI)(CI)M' | Out-Null
```

**Verify:**

```powershell
Get-ItemProperty IIS:\AppPools\WabiInsightApi | Select-Object managedRuntimeVersion, startMode, @{n='idleTimeout';e={$_.processModel.idleTimeout}}   # '', AlwaysRunning, 00:00:00
icacls "$($WI.Root)\keys"               # SYSTEM, Administrators, IIS AppPool\WabiInsightApi only
icacls "$($WI.Root)\web\api" | Select-String 'WabiInsightApi'
icacls $WI.Files | Select-String 'WabiInsightApi'    # (OI)(CI)(M)
```

## 17. Server-specific configuration (`appsettings.Local.json`)

`appsettings.json` already holds the SMTP account and the AI key. `appsettings.Local.json` next to it sets what differs on this server. It loads last and overrides everything else, and `dotnet publish` never overwrites it.

**Check:** `Test-Path (Join-Path $WI.Root 'web\api\appsettings.Local.json')`

**Do:** if `$dbPassword` is no longer set in this window, enter it again:

```powershell
if (-not $dbPassword) { $dbPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR((Read-Host 'Password of wabiinsightdbowner' -AsSecureString))) }
$localSettings = [ordered]@{
    ConnectionStrings = @{ ConfigurationDatabase = "Server=localhost,1433;Database=WabiInsightDB;User Id=wabiinsightdbowner;Password=$dbPassword;Encrypt=True;TrustServerCertificate=True;" }
    Platform          = @{ BaseUrl = "https://$($WI.Domain)" }
    Storage           = @{ RootPath = $WI.Files }
    DataProtection    = @{ KeyRingPath = (Join-Path $WI.Root 'keys') }
}
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
if (-not (Test-Path $localPath)) { $localSettings | ConvertTo-Json -Depth 4 | Set-Content -Path $localPath -Encoding UTF8 }
```

If the database is on another server, change `ConfigurationDatabase` to that server. To keep the committed connection string from `appsettings.json`, remove the `ConnectionStrings` entry. You can add other overrides the same way (for example `Email = @{ Password = '…' }`). Don't put a `Bootstrap` section here: this file overrides environment variables, and step 19 enables bootstrap through them.

**Verify** (password masked):

```powershell
(Get-Content $localPath -Raw) -replace 'Password=[^;]*', 'Password=***'
(Get-Content $localPath -Raw | ConvertFrom-Json).Platform.BaseUrl         # https://<your domain>
```

### 17.1 Optional: keep the files in Azure Blob Storage

Files go to `D:\wabi-insight\files` unless you switch to a private Azure Blob Storage container ([DEPLOYMENT.md, File storage](DEPLOYMENT.md#file-storage-server-disk-or-azure-blob-storage)). You can do it now or at any time later.

**Do:** in the Azure portal copy the storage account's **Connection string** (*Security + networking → Access keys*), then add it to `appsettings.Local.json` (the key is read without echo and never shown):

```powershell
$cs = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR((Read-Host 'Azure Storage connection string' -AsSecureString)))
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
$settings = Get-Content $localPath -Raw | ConvertFrom-Json
if (-not $settings.Storage) { $settings | Add-Member -NotePropertyName Storage -NotePropertyValue ([pscustomobject]@{}) }
$settings.Storage | Add-Member -Force -NotePropertyName AzureBlob -NotePropertyValue ([pscustomobject]@{ ConnectionString = $cs })
$settings | ConvertTo-Json -Depth 6 | Set-Content $localPath -Encoding UTF8
Remove-Variable cs
```

Then switch the provider: in `src\WabiInsight.Api\appsettings.json` of the repository, put `//` in front of `"Provider": "FileSystem",`, remove the `//` in front of `"Provider": "AzureBlob",`, commit and deploy. (To switch this server only, add `"Provider": "AzureBlob"` next to `AzureBlob` in `appsettings.Local.json` instead.)

**Verify** (once the switch is deployed):

```powershell
Push-Location (Join-Path $WI.Root 'web\api'); $env:ASPNETCORE_ENVIRONMENT = 'Production'
dotnet .\WabiInsight.Api.dll storage-check     # "Storage check passed"
dotnet .\WabiInsight.Api.dll storage-copy      # optional: copies files already in D:\wabi-insight\files
Pop-Location
```

Restart the API site afterwards (`Restart-WebAppPool WabiInsightApi`). Editing only the server's `web\api\appsettings.json` also works, but the next deployment publishes the repository's copy again.

## 18. Create the database schema (migrations)

**Check:** this lists applied migrations:

```powershell
$dbCredential = New-Object pscredential 'wabiinsightdbowner', (ConvertTo-SecureString $dbPassword -AsPlainText -Force)
Invoke-WiSql -Database WabiInsightDB -Credential $dbCredential "IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NULL SELECT 0 AS Applied, N'none' AS Latest ELSE SELECT COUNT(*) AS Applied, MAX(MigrationId) AS Latest FROM dbo.__EFMigrationsHistory"
```

**Do:**

```powershell
Push-Location (Join-Path $WI.Root 'web\api')
$env:ASPNETCORE_ENVIRONMENT = 'Production'
dotnet .\WabiInsight.Api.dll migrate
Pop-Location
```

**Verify:** run the **Do** block again. It must print `Configuration database is up to date.` The **Check** query now shows the migration count and the newest migration (e.g. `…_AddAnalysisSharing`).

## 19. Create the first Platform Administrator (once)

**Check:** `0` means not done yet:

```powershell
Invoke-WiSql -Database WabiInsightDB -Credential $dbCredential "SELECT COUNT(*) AS Administrators FROM dbo.Administrators"
```

Environment variables override `appsettings.json`, so the variables below turn bootstrap on for this one command only. `appsettings.Local.json` would override them, which is why it must not contain a `Bootstrap` section.

**Do:** choose the administrator's initial password: at least 12 characters, mixed case, a digit and a symbol. You change it at first sign-in. The settings live only in this PowerShell process:

```powershell
$adminSecure = Read-Host 'Initial password for the Platform Administrator' -AsSecureString
Push-Location (Join-Path $WI.Root 'web\api')
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:Bootstrap__Enabled = 'true'
$env:Bootstrap__AdministratorEmail = $WI.AdminEmail
$env:Bootstrap__AdministratorFullName = 'Platform Administrator'
$env:Bootstrap__AdministratorPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($adminSecure))
$env:Bootstrap__InitializationSecret = New-WiSecret 40
dotnet .\WabiInsight.Api.dll bootstrap
'Bootstrap__Enabled','Bootstrap__AdministratorEmail','Bootstrap__AdministratorFullName','Bootstrap__AdministratorPassword','Bootstrap__InitializationSecret' |
    ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue }
Pop-Location
```

**Verify:** the command printed `Bootstrap completed. AdministratorId=…`, the **Check** query returns `1`, and the variables are gone:

```powershell
Get-ChildItem Env: | Where-Object Name -like 'Bootstrap__*'          # no output
(Get-Content (Join-Path $WI.Root 'web\api\appsettings.json') -Raw | ConvertFrom-Json).Bootstrap.Enabled         # False: bootstrap stays off for the running site
```

## 20. IIS site for the API (loopback only)

**Check:** `Get-Website -Name 'WabiInsightApi' -ErrorAction SilentlyContinue | Select-Object Name, State, PhysicalPath, @{n='Bindings';e={$_.bindings.Collection.bindingInformation}}`

**Do:**

```powershell
Import-Module WebAdministration
if (-not (Get-Website -Name 'WabiInsightApi')) {
    New-Website -Name 'WabiInsightApi' -PhysicalPath (Join-Path $WI.Root 'web\api') -ApplicationPool 'WabiInsightApi' -IPAddress '127.0.0.1' -Port 5080 | Out-Null
}
Set-ItemProperty 'IIS:\Sites\WabiInsightApi' -Name applicationDefaults.preloadEnabled -Value $true
Start-Website 'WabiInsightApi'
```

**Verify:**

```powershell
Get-Website 'WabiInsightApi' | Select-Object State, @{n='Binding';e={$_.bindings.Collection.bindingInformation}}    # Started, 127.0.0.1:5080:
(Invoke-WebRequest 'http://127.0.0.1:5080/health/ready' -UseBasicParsing).Content         # {"status":"Healthy",…"configuration-database"…"Healthy"…}
Invoke-WebRequest 'http://127.0.0.1:5080/api/auth/csrf' -UseBasicParsing | Out-Null; Get-ChildItem (Join-Path $WI.Root 'keys')   # key-….xml
```

On `HTTP Error 500.30` or `500.31`, see Troubleshooting (stdout log).

## 21. IIS site for the web app (HTTP first)

**Check:**

```powershell
Get-Website | Select-Object Name, State, @{n='Bindings';e={$_.bindings.Collection.bindingInformation -join ', '}}
```

**Do:** stop the IIS default site and create the public site with a host-name binding on port 80:

```powershell
Import-Module WebAdministration
if (Get-Website -Name 'Default Web Site') {
    Stop-Website 'Default Web Site'
    Set-ItemProperty 'IIS:\Sites\Default Web Site' -Name serverAutoStart -Value $false
}
if (-not (Get-Website -Name 'WabiInsight')) {
    New-Website -Name 'WabiInsight' -PhysicalPath (Join-Path $WI.Root 'web\app') -ApplicationPool 'WabiInsightWeb' -Port 80 -HostHeader $WI.Domain | Out-Null
}
Start-Website 'WabiInsight'
```

**Verify:** port 80 answers for your domain. The redirect to HTTPS is expected, since the certificate comes next:

```powershell
$r = Invoke-WebRequest "http://$($WI.Domain)/login" -MaximumRedirection 0 -UseBasicParsing -ErrorAction SilentlyContinue
"$($r.StatusCode) $($r.Headers.Location)"                     # 301 https://<your domain>/login
```

## 22. HTTPS certificate and binding

### Option A: free Let's Encrypt certificate with win-acme (auto-renewing)

**Check:** `Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -like "*$($WI.Domain)*" -or $_.DnsNameList.Unicode -contains $WI.Domain } | Select-Object Subject, NotAfter, Thumbprint`

**Do:**

```powershell
$release = Invoke-RestMethod 'https://api.github.com/repos/win-acme/win-acme/releases/latest' -Headers @{ 'User-Agent' = 'wabi-insight-setup' }
$asset = $release.assets | Where-Object name -match 'x64\.trimmed\.zip$' | Select-Object -First 1
$zip = Get-WiDownload $asset.browser_download_url $asset.name
$wacsDir = Join-Path $WI.Root 'tools\win-acme'
Expand-Archive $zip -DestinationPath $wacsDir -Force
$siteId = (Get-Website -Name 'WabiInsight').Id
& (Join-Path $wacsDir 'wacs.exe') --source iis --siteid $siteId --host $WI.Domain --emailaddress $WI.CertEmail --accepttos
```

win-acme validates over port 80, stores the certificate in the machine store, adds the `https` binding (port 443, SNI) to the site, and creates a daily scheduled task that renews it. If the unattended command reports an argument error in your win-acme version, run `wacs.exe` without arguments and choose **N: Create certificate (default settings)** → site **WabiInsight**.

### Option B: a certificate you already have (`.pfx`)

**Do:**

```powershell
$pfxPassword = Read-Host 'PFX password' -AsSecureString
$cert = Import-PfxCertificate -FilePath 'C:\path\to\certificate.pfx' -CertStoreLocation Cert:\LocalMachine\My -Password $pfxPassword
if (-not (Get-WebBinding -Name 'WabiInsight' -Protocol https)) {
    New-WebBinding -Name 'WabiInsight' -Protocol https -Port 443 -HostHeader $WI.Domain -SslFlags 1
}
(Get-WebBinding -Name 'WabiInsight' -Protocol https).AddSslCertificate($cert.Thumbprint, 'My')
```

### Verify (both options)

```powershell
Get-WebBinding -Name 'WabiInsight' | Select-Object protocol, bindingInformation, sslFlags        # http *:80:<domain> and https *:443:<domain> (sslFlags 1 = SNI)
netsh http show sslcert hostnameport="$($WI.Domain):443" | Select-String 'Certificate Hash|Store Name'
(Invoke-WebRequest "https://$($WI.Domain)/health/ready" -UseBasicParsing).Content                # Healthy, through IIS + ARR
$r = Invoke-WebRequest "https://$($WI.Domain)/" -UseBasicParsing
$r.Headers['Strict-Transport-Security']; $r.Headers['Content-Security-Policy'] | Select-Object -First 1
Get-ScheduledTask | Where-Object TaskName -like 'win-acme*' | Select-Object TaskName, State    # Option A: renewal task Ready
```

## 23. Windows Firewall

IIS opens 80 and 443 during installation. SQL Server Express adds no inbound rule, so 1433 stays private.

**Check:**

```powershell
Get-NetFirewallRule -DisplayGroup 'World Wide Web Services (HTTP)', 'Secure World Wide Web Services (HTTPS)' -ErrorAction SilentlyContinue |
    Select-Object DisplayName, Enabled, Direction, Action
Get-NetFirewallPortFilter | Where-Object LocalPort -eq 1433 | Get-NetFirewallRule | Where-Object { $_.Enabled -eq 'True' -and $_.Direction -eq 'Inbound' } | Select-Object DisplayName
```

**Do** (only if the HTTP/HTTPS rules are missing or disabled):

```powershell
New-NetFirewallRule -DisplayName 'Wabi Insight HTTP'  -Direction Inbound -Protocol TCP -LocalPort 80  -Action Allow | Out-Null
New-NetFirewallRule -DisplayName 'Wabi Insight HTTPS' -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow | Out-Null
```

**Verify:** the HTTP and HTTPS rules show `Enabled True / Inbound / Allow`, no enabled inbound rule lists port 1433, and `https://<your domain>` opens from your own computer.

## 24. Go-live checks

**Verify** from the server:

```powershell
$email = (Get-Content (Join-Path $WI.Root 'web\api\appsettings.json') -Raw | ConvertFrom-Json).Email
Test-NetConnection $email.Host -Port $email.Port -InformationLevel Quiet          # True: SMTP reachable
Test-NetConnection api.anthropic.com -Port 443 -InformationLevel Quiet           # True: AI provider reachable
Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' -Name enabled   # True
```

**Verify** in a browser, from your own computer:

1. Open `https://<your domain>`. The landing page loads, the padlock shows a valid certificate, and the **العربية** switch flips the layout to RTL.
2. Sign in with the administrator email and the initial password. You're asked to change it, so do so.
3. **Platform → System Health**: database and API are healthy.
4. **Platform → AI providers**: the default Anthropic provider is listed. **Test** succeeds.
5. Create an organization with a real organization-administrator mailbox. The temporary password **email** arrives, which proves SMTP. Sign in as that administrator in a private window and change the password.
6. As that administrator, register an application or load the samples (step 26), then ask a question on **Ask Wabi Insight**.

## 25. Backups (SQL Server Express has no Agent, so use Task Scheduler)

A restore needs **three things together**: the database, the key ring (`C:\wabitex-apps\wabi-insight\keys`) and the files (`D:\wabi-insight\files`). The nightly task writes to `D:\wabi-insight\backup`:

| What | How | Kept |
|---|---|---|
| `WabiInsightDB-<stamp>.bak` | `BACKUP DATABASE … WITH CHECKSUM` | 14 days |
| `keys-<stamp>.zip` | the key ring | 14 days |
| `files\` | mirror of `D:\wabi-insight\files` (robocopy, only changes are copied) | current |

The task runs as `SYSTEM`, which gets backup rights on `WabiInsightDB` only, so no password is stored on disk. The backups sit on the same disk as the files, so copy `D:\wabi-insight\backup` to another machine or to cloud storage regularly.

**Check:**

```powershell
Get-ScheduledTask -TaskName 'Wabi Insight backup' -ErrorAction SilentlyContinue | Select-Object TaskName, State
Test-Path (Join-Path $WI.Backup 'backup.ps1')
```

**Do:**

```powershell
$backup = $WI.Backup
icacls $backup /grant 'NT Service\MSSQL$SQLEXPRESS:(OI)(CI)M' | Out-Null             # SQL Server writes the .bak file
Invoke-WiSql "IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS;"
Invoke-WiSql -Database WabiInsightDB "IF DATABASE_PRINCIPAL_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM]; ALTER ROLE db_backupoperator ADD MEMBER [NT AUTHORITY\SYSTEM];"
$script = @'
# Nightly backup of WabiInsightDB + key ring + files. Runs as SYSTEM from the "Wabi Insight backup" task.
param([string]$Keys = '__KEYS__', [string]$Files = '__FILES__')
$ErrorActionPreference = 'Stop'
Import-Module SqlServer
$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$bak = Join-Path $PSScriptRoot "WabiInsightDB-$stamp.bak"
Invoke-Sqlcmd -ServerInstance 'localhost,1433' -TrustServerCertificate -QueryTimeout 3600 -DisableVariables `
    -Query "BACKUP DATABASE [WabiInsightDB] TO DISK = N'$bak' WITH INIT, CHECKSUM;"
Compress-Archive -Path $Keys -DestinationPath (Join-Path $PSScriptRoot "keys-$stamp.zip") -Force
$ErrorActionPreference = 'Continue'
robocopy.exe $Files (Join-Path $PSScriptRoot 'files') /MIR /R:2 /W:5 /NP /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy of $Files failed with exit code $LASTEXITCODE" }
$ErrorActionPreference = 'Stop'
Get-ChildItem $PSScriptRoot -File | Where-Object { $_.Name -like 'WabiInsightDB-*.bak' -or $_.Name -like 'keys-*.zip' } |
    Where-Object LastWriteTime -lt (Get-Date).AddDays(-14) | Remove-Item -Force
exit 0
'@
$script = $script.Replace('__KEYS__', (Join-Path $WI.Root 'keys')).Replace('__FILES__', $WI.Files)
Set-Content -Path (Join-Path $backup 'backup.ps1') -Value $script -Encoding UTF8
$action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$backup\backup.ps1`""
$trigger   = New-ScheduledTaskTrigger -Daily -At '02:30'
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName 'Wabi Insight backup' -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
```

**Verify:** run it once and check the backup (verifying needs `sysadmin`, which your administrator account has):

```powershell
Start-ScheduledTask -TaskName 'Wabi Insight backup'; Start-Sleep -Seconds 30
(Get-ScheduledTaskInfo -TaskName 'Wabi Insight backup').LastTaskResult             # 0
$latest = Get-ChildItem $backup -Filter 'WabiInsightDB-*.bak' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Invoke-WiSql "RESTORE VERIFYONLY FROM DISK = N'$($latest.FullName)' WITH CHECKSUM;" -Verbose 4>&1 | Select-Object -Last 1   # The backup set on file 1 is valid.
Get-ChildItem $backup -Filter 'keys-*.zip' | Select-Object -Last 1 Name, Length
Get-ChildItem (Join-Path $backup 'files') | Select-Object Name                     # the same folders as D:\wabi-insight\files
```

To restore: stop both sites, `RESTORE DATABASE [WabiInsightDB] FROM DISK = N'…' WITH REPLACE`, unpack `keys-<stamp>.zip` into `C:\wabitex-apps\wabi-insight` (it contains the `keys` folder), run `robocopy D:\wabi-insight\backup\files D:\wabi-insight\files /MIR`, then start the sites.

## 26. Optional: sample databases and sample users

To fill the platform with realistic data (flight operations, HR/payroll/attendance, procurement/stores) and four users of different roles, follow [`scripts/sql/samples/README.md`](../scripts/sql/samples/README.md).

**Check:**

```powershell
Invoke-WiSql "SELECT name FROM sys.databases WHERE name IN (N'FlightOperationsDB', N'HumanResourcesDB', N'ProcurementStoresDB')"
```

**Do:** about 2 minutes:

```powershell
$samples = Join-Path $WI.Root 'src\scripts\sql\samples'
'01-create-flightoperationsdb.sql', '02-create-humanresourcesdb.sql', '03-create-procurementstoresdb.sql', '04-seed-wabiinsightdb.sql' | ForEach-Object {
    "== $_"
    Invoke-WiSql -InputFile (Join-Path $samples $_) | Format-Table -AutoSize | Out-String -Width 220
}
```

**Verify:** the last script lists the four users and the three applications. Then, signed in as `faisal.alshamsi@gulfaviation.example`, open **Applications** and for each application **Replace connection** with `Server=localhost,1433;Database=…;User Id=…_reader;Password=…;Encrypt=True;TrustServerCertificate=True;`, **Test connection** (*read-only verified*), then **Approve**. Change the sample passwords (README) before anyone else uses the server.

## 27. Optional hardening: dedicated insights login

Platform Insights can run under a login confined to the `insights` schema. Without it, the API logs a warning and uses the configuration login.

**Check:** `(Get-Content (Join-Path $WI.Root 'web\api\appsettings.Local.json') -Raw | ConvertFrom-Json).Insights.ConnectionString` (empty means not configured).

**Do:**

```powershell
$insightsPassword = New-WiSecret
$sql = (Get-Content (Join-Path $WI.Root 'src\scripts\sql\create-insights-login.sql') -Raw).Replace('USE [WabiInsight];', 'USE [WabiInsightDB];').Replace('<replace-with-strong-secret>', $insightsPassword)
$tmp = Join-Path $env:TEMP 'insights-login.sql'; Set-Content $tmp $sql -Encoding UTF8
Invoke-WiSql -InputFile $tmp; Remove-Item $tmp
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
$settings = Get-Content $localPath -Raw | ConvertFrom-Json
$settings | Add-Member -Force -NotePropertyName Insights -NotePropertyValue ([pscustomobject]@{
    ConnectionString = "Server=localhost,1433;Database=WabiInsightDB;User Id=wabi_insight_insights;Password=$insightsPassword;Encrypt=True;TrustServerCertificate=True;" })
$settings | ConvertTo-Json -Depth 4 | Set-Content $localPath -Encoding UTF8
Restart-WebAppPool 'WabiInsightApi'
```

**Verify:** **Platform → Platform Insights Configuration** shows *confined to the insights schema*.

## 28. Updating to a new version

With CI/CD set up ([DEVOPS_WINDOWS.md](DEVOPS_WINDOWS.md)), use **Actions → Deploy (Windows)** instead of these commands. The manual procedure:

```powershell
$WI = Import-Clixml C:\wabitex-apps\wabi-insight\wabi-insight.vars.xml     # plus the session settings and helpers from step 1
$src = Join-Path $WI.Root 'src'; $api = Join-Path $WI.Root 'web\api'; $web = Join-Path $WI.Root 'web\app'
git -C $src fetch origin $WI.Branch; git -C $src checkout $WI.Branch; git -C $src pull --ff-only; git -C $src log -1 --oneline
Start-ScheduledTask -TaskName 'Wabi Insight backup'; Start-Sleep -Seconds 30                      # backup first
Set-Content (Join-Path $api 'app_offline.htm') '<h1>Wabi Insight is being updated. Please try again in a minute.</h1>'
Start-Sleep -Seconds 5
dotnet publish "$src\src\WabiInsight.Api\WabiInsight.Api.csproj" -c Release -o $api
Copy-Item "$src\deploy\iis\web.config" (Join-Path $api 'web.config') -Force
Push-Location $api; $env:ASPNETCORE_ENVIRONMENT = 'Production'; dotnet .\WabiInsight.Api.dll migrate; Pop-Location
Remove-Item (Join-Path $api 'app_offline.htm')
Push-Location "$src\src\web"; npm ci; npm run build -- --configuration production; Pop-Location
robocopy "$src\src\web\dist\wabi-insight-web\browser" $web /MIR /XF web.config /NFL /NDL /NJH /NJS | Out-Null
Copy-Item "$src\deploy\iis\web.spa.config" (Join-Path $web 'web.config') -Force
```

**Verify:** `(Invoke-WebRequest "https://$($WI.Domain)/health/ready" -UseBasicParsing).Content` reports Healthy. Sign in, and **System Health** is green.

## 29. Moving an existing installation from `C:\WabiInsight`

Earlier versions of this guide used `C:\WabiInsight\{src,api,web,data\storage,data\keys,backup}`. To move such a server to this layout, open an administrator PowerShell with the step 1 settings, variables (the new values) and helpers loaded.

**Check:** `Test-Path C:\WabiInsight\api\WabiInsight.Api.dll` (False means nothing to move).

**Do:**

```powershell
Import-Module WebAdministration
$old = 'C:\WabiInsight'
Stop-Website 'WabiInsight'; Stop-Website 'WabiInsightApi'; Stop-WebAppPool 'WabiInsightApi'
# Folders first (step 5 Check/Do), then copy everything across
$moves = @(
    @($(Join-Path $old 'src'), $(Join-Path $WI.Root 'src')),
    @($(Join-Path $old 'api'), $(Join-Path $WI.Root 'web\api')),
    @($(Join-Path $old 'web'), $(Join-Path $WI.Root 'web\app')),
    @($(Join-Path $old 'data\keys'), $(Join-Path $WI.Root 'keys')),
    @($(Join-Path $old 'data\storage'), $WI.Files),
    @($(Join-Path $old 'backup'), $WI.Backup)
)
foreach ($m in $moves) { if (Test-Path $m[0]) { robocopy $m[0] $m[1] /E /COPY:DAT /R:2 /W:2 /NFL /NDL /NJH /NJS | Out-Null; if ($LASTEXITCODE -ge 8) { throw "copy $($m[0]) failed" } } }
# New paths in the server settings
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
$settings = Get-Content $localPath -Raw | ConvertFrom-Json
$settings.Storage.RootPath = $WI.Files
$settings.DataProtection.KeyRingPath = Join-Path $WI.Root 'keys'
$settings | ConvertTo-Json -Depth 4 | Set-Content $localPath -Encoding UTF8
# IIS sites point at the new folders
Set-ItemProperty 'IIS:\Sites\WabiInsightApi' -Name physicalPath -Value (Join-Path $WI.Root 'web\api')
Set-ItemProperty 'IIS:\Sites\WabiInsight' -Name physicalPath -Value (Join-Path $WI.Root 'web\app')
```

Then apply the permissions of **step 16** (the `icacls` lines) and **step 25** (backup task, re-registered with the new paths), and, with CI/CD, reinstall the deploy script ([DEVOPS_WINDOWS.md](DEVOPS_WINDOWS.md) step 4). Finally:

```powershell
Start-WebAppPool 'WabiInsightApi'; Start-Website 'WabiInsightApi'; Start-Website 'WabiInsight'
```

**Verify:**

```powershell
(Get-Website 'WabiInsightApi').physicalPath; (Get-Website 'WabiInsight').physicalPath      # …\web\api and …\web\app
(Invoke-WebRequest "https://$($WI.Domain)/health/ready" -UseBasicParsing).Content         # Healthy
Get-ChildItem $WI.Files | Select-Object Name                                               # analysis-reports, … (your existing files)
```

Open an earlier analysis in **My History** and download its report, which proves the files and the key ring were found. When everything works, delete `C:\WabiInsight`.

## 30. Troubleshooting

| Symptom | Check | Fix |
|---|---|---|
| `HTTP Error 500.19` | Is the module installed? `Get-WebGlobalModule -Name AspNetCoreModuleV2, RewriteModule, ApplicationRequestRouting` | Install the missing module (steps 8–10). Run `net stop was /y; net start w3svc` after the Hosting Bundle. |
| `HTTP Error 500.30 / 500.31` (API failed to start) | In `C:\wabitex-apps\wabi-insight\web\api\web.config` set `stdoutLogEnabled="true"` and `stdoutLogFile="C:\wabitex-apps\wabi-insight\logs\stdout"`, reload, read the newest log | Usually the connection string or a missing .NET 10 runtime. Set it back to `false` afterwards. |
| `HTTP Error 500.50` on `/api/...` | Step 10.3 **Check** | Allow `HTTP_X_FORWARDED_PROTO` as a server variable. |
| `404` on `/api/...` but the site loads | Step 10.2 **Check** (`enabled`) and `Select-String 'API reverse proxy' C:\wabitex-apps\wabi-insight\web\app\web.config` | Enable the ARR proxy and copy `deploy\iis\web.spa.config` as `web.config` again. |
| `502.3` after about 2 minutes on a study | Step 10.2 **Check** (`timeout`) | Set the proxy timeout to `00:10:00`. |
| Everyone shares one login limit (`429`) | `Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter system.webServer/proxy -Name includePortInXForwardedFor` | ARR adds `X-Forwarded-For`. The API trusts it from 127.0.0.1 only, so keep the API site bound to `127.0.0.1:5080`. |
| `Login failed for user 'wabiinsightdbowner'` | Step 12 **Verify**; step 11.4 `LoginMode` | Wrong password in `appsettings.Local.json`, or the server is still Windows-authentication only. |
| `A network-related … error` connecting to SQL | Step 11.4 **Check** (`TcpPort`, `Test-NetConnection`) | Fixed port 1433 and TCP enabled, then restart `MSSQL$SQLEXPRESS`. |
| Signed out right after signing in | Is the site opened over `https://`? | Session cookies are `Secure`, so plain HTTP drops them. |
| Emails don't arrive | `Invoke-WiSql -Database WabiInsightDB "SELECT TOP 10 Status, RecipientEmail, LastError, CreatedAt FROM dbo.EmailMessages ORDER BY CreatedAt DESC"` | Fix SMTP settings or outbound port (step 24). Failed messages are retried. |
| Background emails stop when nobody uses the site | Step 16 **Verify** (`AlwaysRunning`, idle timeout 0, preload) | Apply the pool settings and `applicationDefaults.preloadEnabled`. |
| Stored connection strings fail after a restore | `Get-ChildItem C:\wabitex-apps\wabi-insight\keys` | Restore the key ring from the same backup as the database. |
| Database close to the Express limit | `Invoke-WiSql -Database WabiInsightDB "EXEC sp_spaceused"` | Archive old usage events, or move to Express 2025 (50 GB) or Standard. |
