# Wabi Insight on a Windows Server 2019 VPS with SQL Server Express — the complete beginner's guide (deployment + CI/CD)

This guide assumes **no previous knowledge**. It takes you from a fresh Windows Server 2019 VPS to a live Wabi Insight
at `https://<your domain>`, with everything on **one server**:

- the **web app and the API** in IIS (Microsoft's web server, part of Windows);
- the **database** in **SQL Server 2025 Express** (Microsoft's free edition of SQL Server), on the same VPS;
- the **generated and uploaded files** (reports, exports, attachments, logos) on the VPS disk;
- a **nightly backup** of the database, the encryption keys and the files;
- **one-click deployments** from GitHub (**Actions → Deploy (Windows)**). GitHub builds the code, and a small agent on
  your VPS installs it. The VPS needs **no Git, .NET SDK or Node.js**.

```
 Users' browsers
      │ HTTPS (443)
      ▼
 ┌──────────── Your VPS: Windows Server 2019 ─────────────────────────────────────────┐
 │ IIS site "WabiInsight" ── /            → the web app (C:\wabitex-apps\wabi-insight\web\app)
 │   (URL Rewrite + ARR)  ── /api, /health → IIS site "WabiInsightApi" on 127.0.0.1:5080
 │                                            (C:\wabitex-apps\wabi-insight\web\api)
 │                                                 │ SQL on localhost, port 1433
 │                                                 ▼
 │ SQL Server 2025 Express (instance SQLEXPRESS) → database "WabiInsightDB"
 │ Files:    D:\wabi-insight\files\<kind>          Key ring: C:\wabitex-apps\wabi-insight\keys
 │ Nightly task "Wabi Insight backup" → D:\wabi-insight\backup (database .bak + key ring + files)
 │ GitHub Actions runner (service) ← receives the deployments you start on GitHub
 └──────────────┬──────────────────────────────┬──────────────────────────┬──────────┘
                │ outgoing HTTPS only          │ HTTPS (443)              │ SMTP (587 or 465)
                ▼                              ▼                          ▼
   GitHub (builds each version)        The AI provider            Your e-mail server
```

Without a second disk (`D:`), the files and the backups go to `C:\wabi-insight` instead. Step 5 chooses this for you.

Other guides:
- Database in **Azure SQL** and files in **Azure Blob Storage** instead of SQL Server Express and the disk:
  [DEPLOYMENT_WINDOWS_AZURE.md](DEPLOYMENT_WINDOWS_AZURE.md).
- More detail on the deployment pipeline: [DEVOPS_WINDOWS.md](DEVOPS_WINDOWS.md).
- The same on Linux: [DEPLOYMENT_LINUX.md](DEPLOYMENT_LINUX.md). Every configuration key: [DEPLOYMENT.md](DEPLOYMENT.md).

**Time:** about 3–4 hours the first time. **Extra monthly cost** on top of your VPS: none is required (see
[Costs](#42-costs-approximate-per-month) at the end).

## How to read this guide

- **Where a step happens** is shown at its start:
  - **[VPS PowerShell]**: the administrator PowerShell window on your VPS.
  - **[GitHub]**: the GitHub website, in your browser.
  - **[Domain registrar]**: the website of the company where you bought your domain.
  - **[Provider panel]**: your VPS provider's control panel, in your browser.
  - **[Browser]**: any browser, on your own computer.
- Most steps have three parts:
  - **Check:** tells you whether the step is already done. If it is, skip **Do**.
  - **Do:** the actions or commands.
  - **Verify:** proves the step worked. **Don't continue until it does.**
- **Copy and paste** commands exactly. In PowerShell, paste with a **right-click**, a whole block at once, then press
  **Enter**.
- **Blocks between `. {` and `}`** ask you for a password or must stop at the first error. Paste the whole block,
  **including those two lines**: nothing runs until the closing `}`, then PowerShell asks its questions one at a time.
  If the prompt shows `>>` after pasting, press **Enter** once more. If such a block stops with red text, nothing after
  the error has run: fix the cause and paste the whole block again.
  Lines starting with `#` are comments and are ignored. A comment at the end of a line, such as `# True`, shows the
  output you should see.
- Text in `<angle brackets>` is a placeholder. Replace it with your own value, without the brackets.
- Red text in PowerShell means an error. Stop, read it, and look it up in [Troubleshooting](#43-troubleshooting)
  (step 43).
- **Never paste a password into a chat, an e-mail or GitHub.** Keep every password and key in a password manager
  (Bitwarden, 1Password, KeePass…).

### Words you'll meet

| Word | Meaning |
|---|---|
| VPS | A virtual server you rent from a hosting company. Yours runs Windows Server 2019. |
| RDP / Remote Desktop | The program you use to see and control the VPS screen from your computer. |
| PowerShell (as administrator) | Windows' command window with full rights. Almost everything on the VPS is done there. |
| IIS | Internet Information Services, the web server built into Windows. |
| Application pool | The IIS process that runs a site. |
| URL Rewrite, ARR | Two IIS add-ons. Together they pass the requests for `/api` and `/health` from the public site to the API site. This is called a *reverse proxy*. |
| SQL Server Express | Microsoft's free edition of SQL Server, the database engine. It runs on your VPS. |
| Instance | One installed copy of SQL Server. Express installs as the instance `SQLEXPRESS`; its Windows service is `MSSQL$SQLEXPRESS`. |
| `sa` | SQL Server's built-in administrator login. You keep its password for emergencies only. |
| SQL login | A user name and password that SQL Server checks itself. The application signs in with the SQL login `wabiinsightdbowner`. |
| Mixed mode | SQL Server accepts both Windows accounts and SQL logins. The application needs it. |
| Connection string | One line of text that tells the application where the database is and how to sign in. It contains a password, so treat it as a secret. |
| Migration | A change to the database tables that comes with a new version. A deployment applies it when you tick **migrate_database**. |
| Key ring | Files that encrypt the secrets the application stores in the database. **If you lose them, those secrets can't be read.** |
| `.bak` file | A complete backup of a SQL Server database, in one file. |
| Scheduled task | A job that Windows runs at set times (Task Scheduler). Here: the nightly backup. |
| DNS A record | The setting at your domain registrar that points `insight.example.com` to your VPS IP address. |
| TLS certificate | What makes the padlock and `https://` work. Here it's free, from Let's Encrypt, and renews itself. |
| CI | Continuous Integration: GitHub builds and tests every change automatically. Already active in this repository. |
| CD | Continuous Delivery: installing a version on your server. Here you start it with one click. |
| Self-hosted runner | A small GitHub agent installed on your VPS as a Windows service. It receives the deployments you start and installs them. |
| Fine-grained token | A short-lived GitHub password for a script, limited to one repository and to reading. |

## Your information sheet

Fill this in as you go. Keep the secret rows in your password manager, not in a text file.

| Item | Example | Secret? |
|---|---|---|
| VPS public IP address | `203.0.113.10` | no |
| VPS Administrator user and password | `Administrator` / … | **yes** |
| Domain for the site | `insight.example.com` | no |
| E-mail for certificate notices | `it@example.com` | no |
| Folder for files and backups | `D:\wabi-insight` (or `C:\wabi-insight`) | no |
| SQL Server `sa` password | … | **yes** |
| Password of the SQL login `wabiinsightdbowner` | … | **yes** |
| First Platform Administrator e-mail and password | `support@wabitex.com` / … | **yes** |

---

# Part A — Before you start

## 1. What you need

| You need | Where to get it | Notes |
|---|---|---|
| The VPS: Windows Server 2019 **with Desktop Experience**, 64-bit | Your hosting provider's control panel shows its IP address and the Administrator password | 2 vCPU / 4 GB RAM works; 4 vCPU / 8 GB RAM is comfortable, because SQL Server runs on the same VPS. At least 30 GB free disk. A second disk (`D:`) for files and backups is recommended, not required. |
| A domain name and access to its DNS settings | Your domain registrar (GoDaddy, Namecheap, Cloudflare…) | For `https://` and for the links in e-mails |
| A GitHub account with **admin** rights on the repository `mtawfig/wabi-data-harbor` | `https://github.com` | Needed to add the runner and the environment |
| A computer with a browser and a Remote Desktop app | Windows: *Remote Desktop Connection* (`mstsc`) is built in. Mac: install *Windows App* (Microsoft Remote Desktop) from the App Store. | |
| A password manager | Bitwarden, 1Password, KeePass… | You'll create several passwords |

The SMTP account (for e-mails), the AI key and a connection string for the configuration database are already in the
repository's `appsettings.json` (owner decision G). **You don't need to enter the SMTP account or the AI key.** This
server gets its own database (Part D), so step 24 writes this server's connection string into `appsettings.Local.json`,
which overrides `appsettings.json`. You can override any other committed value there too.

### Is SQL Server Express enough?

Yes. Wabi Insight runs on **SQL Server Express 2019, 2022 and 2025**. The full integration test suite (migrations,
multi-tenant isolation, row scopes, the support desk, the sample-database scripts) passes on each of these Express
versions. The application uses no Standard- or Enterprise-only feature and needs no SQL Server Agent.

| Limit | Express 2019 / 2022 | Express 2025 | What it means here |
|---|---|---|---|
| Database size | 10 GB per database | 50 GB per database | `WabiInsightDB` is about 5 MB after setup and grows with history, audit and usage events, so several years fit. The sample databases are under 50 MB each. |
| Memory | 1,410 MB buffer pool | 1,410 MB buffer pool | Enough for the configuration database. |
| CPU | 1 socket or 4 cores (whichever is lower) | same | Fine for the API's short queries. |
| SQL Server Agent | not included | not included | Backups are scheduled with Task Scheduler instead (step 30). |
| Backup compression | not supported | not supported | Backups run without `COMPRESSION`. |

Express needs three changes from its defaults, all made in steps 18 and 19. By default it installs:
- as the **named instance `SQLEXPRESS`**,
- with **TCP/IP disabled**,
- and with **Windows authentication only**.

The API needs TCP on a fixed port (1433) and SQL logins (mixed mode).

> This guide puts SQL Server Express on the same VPS. If yours is on another machine, apply step 19 on that machine and
> skip steps 16–18. Then use its address instead of `localhost,1433` in `Invoke-WiSql` (step 5) and in the connection
> string (step 24). The backup task (step 30) needs adapting too, because SQL Server writes the `.bak` file on its own
> machine.

## 2. Open the VPS ports in your provider's control panel

**Where:** [Provider panel].

Many providers have a firewall in their control panel, outside Windows. Make sure it allows:

| Port | Why | From |
|---|---|---|
| 80 (HTTP) | Certificate validation and the redirect to HTTPS | Anywhere |
| 443 (HTTPS) | The website | Anywhere |
| 3389 (Remote Desktop) | Your access | **Only your own IP address**, if the panel allows it (strongly recommended) |

**Don't open** port 1433 (SQL Server) or 5080 (the API). Only programs on the VPS itself use them.

Outgoing connections must be allowed. They usually are by default:
- HTTPS (443) to GitHub: `github.com`, `api.github.com`, `codeload.github.com`, `objects.githubusercontent.com`,
  `*.actions.githubusercontent.com`, and `*.blob.core.windows.net` (where GitHub keeps the built versions);
- HTTPS (443) to Microsoft's download sites and the PowerShell Gallery (installers), and to Let's Encrypt (certificate);
- HTTPS (443) to the AI provider, `api.anthropic.com`;
- your SMTP server, on port 587 or 465.

## 3. Point your domain at the VPS (DNS)

**Where:** [Domain registrar].

**Do:**
1. Open the DNS settings of your domain.
2. Add a record of type **A**:
   - **Name/Host:** `insight` (for `insight.example.com`; use `@` for the bare domain);
   - **Value/Points to:** your VPS IP address;
   - **TTL:** the default.
3. Save.

DNS changes can take from a few minutes to a few hours to work everywhere.

**Verify** (on your own computer, in a command prompt): `nslookup insight.example.com` shows your VPS IP address.

---

# Part B — Connect to the VPS and prepare PowerShell

## 4. Connect with Remote Desktop

**Do:**
- **Windows:** press `Win + R`, type `mstsc`, press Enter. In *Computer*, enter the VPS IP address → **Connect**. User
  `Administrator` and the password from your provider → **OK**. Accept the certificate warning (**Yes**).
- **Mac:** open *Windows App* → **+** → *Add PC* → *PC name*: the IP address → **Add**, then double-click it and sign in.

**Verify:** you see the Windows Server desktop.

**Recommended:** change the Administrator password to a long, unique one. Press `Ctrl + Alt + End` → *Change a
password*, and store it in your password manager.

Copy and paste work between your computer and the VPS through Remote Desktop. You can do the **[GitHub]** steps in the
browser on your own computer and paste the results into the VPS.

## 5. Open PowerShell as administrator, and set your values

**Where:** the VPS. Click **Start**, type `PowerShell`, right-click **Windows PowerShell** → **Run as administrator** →
**Yes**. Use this window for every **[VPS PowerShell]** step.

**Check:**

```powershell
$PSVersionTable.PSVersion.ToString()                                                   # 5.1.x
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)   # True
```

**Do:** first, the session settings. **Paste these again every time you open a new PowerShell window.**

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'      # makes downloads much faster
$ErrorActionPreference = 'Stop'
```

Then your values. **Edit the lines marked `# CHANGE`** before pasting. The files and the backups go on `D:` if your VPS
has a second local disk, otherwise on `C:`.

```powershell
$dataDisk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='D:' AND DriveType=3"    # a local disk D:, not a DVD drive
$dataRoot = if ($dataDisk) { 'D:\wabi-insight' } else { 'C:\wabi-insight' }
$WI = [ordered]@{
    Domain     = 'insight.example.com'               # CHANGE: the domain from step 3
    CertEmail  = 'it@example.com'                    # CHANGE: receives certificate expiry notices
    AdminEmail = 'support@wabitex.com'               # CHANGE: first Platform Administrator (a real mailbox)
    Repo       = 'mtawfig/wabi-data-harbor'
    Root       = 'C:\wabitex-apps\wabi-insight'      # the application: web\app, web\api, keys, logs, releases, deploy
    Files      = "$dataRoot\files"                   # generated and uploaded files, one folder per kind
    Backup     = "$dataRoot\backup"                  # nightly backups (step 30)
}
New-Item -ItemType Directory -Force -Path $WI.Root | Out-Null
$WI | Export-Clixml (Join-Path $WI.Root 'wabi-insight.vars.xml')
```

To change a value later, edit it above and paste the block again: the saved copy is updated. In a **new window**, load
the values back with:

```powershell
$WI = Import-Clixml C:\wabitex-apps\wabi-insight\wabi-insight.vars.xml
```

Finally, the helper functions. **Paste them into every new window too:**

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
function Read-WiSecret([string]$Prompt) {
    # Reads a secret without showing it on screen.
    $secure = Read-Host $Prompt -AsSecureString
    [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
function Invoke-WiSql {
    # Runs SQL on the local SQL Server Express. Works once SQL Server and its PowerShell module are installed (steps 18-20).
    # Signs in with your Windows administrator account (sysadmin), or with -Credential.
    [CmdletBinding()]
    # -Tables shows every result of a script as its own table (the sample scripts return several).
    param([string]$Query, [string]$InputFile, [string]$Database = 'master', [pscredential]$Credential, [switch]$Tables)
    Import-Module SqlServer -Verbose:$false
    $p = @{ ServerInstance = 'localhost,1433'; Database = $Database; TrustServerCertificate = $true; QueryTimeout = 600; DisableVariables = $true; ErrorAction = 'Stop' }
    if ($InputFile) { $p.InputFile = $InputFile } else { $p.Query = $Query }
    if ($Credential) { $p.Credential = $Credential }
    if ($Tables) { Invoke-Sqlcmd @p -OutputAs DataTables | ForEach-Object { $_ | Format-Table -AutoSize | Out-String -Width 250 } }
    else { Invoke-Sqlcmd @p }
}
```

**Verify:** `$WI` prints your values, and `Get-Command New-WiSecret, Read-WiSecret, Invoke-WiSql` finds all three
functions.

## 6. Check the server, its disks and its public IP address

**[VPS PowerShell] Do / Verify:**

```powershell
(Get-CimInstance Win32_OperatingSystem) | Select-Object Caption, Version, OSArchitecture   # Windows Server 2019 …, 10.0.17763, 64-bit
Get-CimInstance Win32_ComputerSystem | Select-Object NumberOfLogicalProcessors, @{n='RAM_GB';e={[math]::Round($_.TotalPhysicalMemory/1GB,1)}}
Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | Select-Object DeviceID, @{n='Free_GB';e={[math]::Round($_.FreeSpace/1GB,1)}}   # C: with at least 30 GB free (and D:, if you have one)
$WI.Files                                                                                # D:\wabi-insight\files, or C:\wabi-insight\files without a D: disk
Invoke-RestMethod -Uri 'https://api.ipify.org' -UseBasicParsing                          # the VPS public IP: write it on your sheet
(Resolve-DnsName $WI.Domain -Type A | Where-Object Type -eq 'A' | Select-Object -First 1).IPAddress   # must be the same IP (step 3)
```

- If the last line shows an error or another address, DNS isn't ready yet. Wait before step 26 (the certificate). You
  can continue with the other steps meanwhile.
- If your provider added a second disk that has no drive letter yet: run `diskmgmt.msc`, bring the disk **Online**,
  **Initialize** it, create a **New Simple Volume** with the letter `D`, then paste the values block of step 5 again. If
  a DVD drive already uses `D:`, first give it another letter: right-click it → **Change Drive Letter and Paths**.

## 7. Two passwords for SQL Server

**[VPS PowerShell] Do:** generate them and **store both in your password manager** right away:

```powershell
"sa password (SQL Server's own administrator):              $(New-WiSecret)"
"Password of wabiinsightdbowner (the application's login):  $(New-WiSecret)"
```

- **`sa`** is SQL Server's built-in administrator. Setup needs a password for it (step 18). Neither you nor the
  application uses it day to day: you sign in to SQL Server with your Windows administrator account. Keep it for
  emergencies.
- **`wabiinsightdbowner`** is the SQL login the application uses. It owns the database `WabiInsightDB` and nothing else
  (step 21). Its password goes into `appsettings.Local.json` (step 24).

They contain only letters, digits and `-`. This meets SQL Server's password rules and is safe inside connection
strings. Don't use `;`, `'` or `"` in these passwords.

---

# Part C — Install the software on the VPS

Run every step in **[VPS PowerShell]**, with the session settings, `$WI` and the helpers from step 5 loaded.

## 8. Windows updates

**Check:**

```powershell
Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 3 HotFixID, InstalledOn   # a recent cumulative update
$pending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
           (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
"Reboot pending: $pending"
```

**Do:**
1. Install all updates: **Start → Settings → Update & Security → Windows Update → Check for updates**, then **Install**.
   (Or type `sconfig` in PowerShell and choose option **6**.)
2. Restart when asked: **Start → Power → Restart**.
3. Reconnect (step 4), open PowerShell as administrator, and paste the session settings, `$WI = Import-Clixml …` and the
   helpers again (step 5).

**Verify:** the **Check** block shows an update from the last month, and `Reboot pending: False`.

## 9. Time zone and clock

**Check:**

```powershell
Get-TimeZone | Select-Object Id
w32tm /query /status | Select-String 'Source|Last Successful'
```

**Do** (only if needed; the application stores times in UTC):

```powershell
Set-TimeZone -Id 'Arabian Standard Time'      # optional, e.g. UAE
w32tm /resync
```

**Verify:** `w32tm /query /status` shows a recent `Last Successful Sync Time`.

## 10. Folders

| Folder | Contents |
|---|---|
| `C:\wabitex-apps\wabi-insight\web\app` | The web app (IIS site `WabiInsight`) |
| `C:\wabitex-apps\wabi-insight\web\api` | The API (IIS site `WabiInsightApi`) and `appsettings.Local.json` (this server's settings) |
| `C:\wabitex-apps\wabi-insight\keys` | The key ring. **Losing it makes every stored connection string and provider key unreadable.** Backed up every night (step 30). |
| `C:\wabitex-apps\wabi-insight\logs` | The deploy log `deploy.log` and, when you turn it on, the API's startup log |
| `C:\wabitex-apps\wabi-insight\releases` | The last 5 releases, plus `previous-api` and `previous-web` for rolling back |
| `C:\wabitex-apps\wabi-insight\deploy` | The deploy script that the runner runs (step 31) |
| `C:\wabitex-apps\wabi-insight\install`, `tools` | Downloaded installers; the certificate tool (win-acme) |
| `C:\wabitex-apps\wabi-insight\src` | Stays empty with GitHub deployments. Only used if you build on the server (Appendix A). |
| `D:\wabi-insight\files\<kind>` | Generated and uploaded files (the setting `Storage:RootPath`), one folder per kind. Inside each: `yyyy\MM\<random>.bin`. |
| `D:\wabi-insight\backup` | Nightly backups (step 30) |

The API stores every file under `Storage:RootPath`, in the folder of its kind:

| Folder under `D:\wabi-insight\files` | What is stored there |
|---|---|
| `analysis-reports` | Study PDFs generated by Ask Wabi Insight |
| `analysis-results` | Stored result sets of every answer (reopening history never re-queries the source) |
| `analysis-exports` | PDF, Word and Excel exports from My History and My Reports |
| `report-deliveries` | Files of the periodic reports sent to users |
| `organization-logos` | Uploaded organization logos |
| `support-attachments` | Files attached to support tickets |

The API creates a missing folder by itself. Creating them here makes the layout visible from the start.

**Check:**

```powershell
$appFolders  = 'src', 'web\app', 'web\api', 'keys', 'logs', 'releases', 'deploy', 'install', 'tools'
$fileFolders = 'analysis-reports', 'analysis-results', 'analysis-exports', 'report-deliveries', 'organization-logos', 'support-attachments'
$appFolders  | ForEach-Object { '{0,-48} {1}' -f (Join-Path $WI.Root $_), (Test-Path (Join-Path $WI.Root $_)) }
$fileFolders | ForEach-Object { '{0,-48} {1}' -f (Join-Path $WI.Files $_), (Test-Path (Join-Path $WI.Files $_)) }
'{0,-48} {1}' -f $WI.Backup, (Test-Path $WI.Backup)
```

**Do:** create them, and remove the general `Users` access on both roots. The application folder will hold secrets,
and a `D:` drive grants `Users` access by default.

```powershell
$appFolders  | ForEach-Object { New-Item -ItemType Directory -Force -Path (Join-Path $WI.Root $_) | Out-Null }
$fileFolders | ForEach-Object { New-Item -ItemType Directory -Force -Path (Join-Path $WI.Files $_) | Out-Null }
New-Item -ItemType Directory -Force -Path $WI.Backup | Out-Null
$dataRoot = Split-Path $WI.Files -Parent                                  # D:\wabi-insight (or C:\wabi-insight)
foreach ($folder in $WI.Root, $dataRoot) {
    icacls $folder /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' 'BUILTIN\Administrators:(OI)(CI)F' | Out-Null
}
```

**Verify:**

```powershell
icacls $WI.Root                          # only NT AUTHORITY\SYSTEM and BUILTIN\Administrators
icacls (Split-Path $WI.Files -Parent)    # only NT AUTHORITY\SYSTEM and BUILTIN\Administrators
Get-ChildItem $WI.Files | Select-Object Name   # the six folders above
```

## 11. IIS (the web server)

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

## 12. Microsoft Visual C++ runtime (x64)

PDF reports and charts use native libraries that need it. Without it, the first report fails with
`DllNotFoundException`.

**Check:** `(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64' -ErrorAction SilentlyContinue).Version`

**Do:**

```powershell
$vc = Get-WiDownload 'https://aka.ms/vs/17/release/vc_redist.x64.exe' 'vc_redist.x64.exe'
$p = Start-Process $vc -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru
"exit code $($p.ExitCode)"      # 0, 3010 (reboot later) or 1638 (a newer version is already installed)
```

**Verify:** the **Check** command prints a version such as `v14.4x.xxxxx`.

## 13. ASP.NET Core 10 Hosting Bundle (runs the API inside IIS)

It installs the .NET 10 runtime and the IIS module for ASP.NET Core. It must come **after** IIS (step 11). The deploy
script stops if it's missing.

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

## 14. IIS URL Rewrite 2.1

**Check:** `Get-WebGlobalModule -Name RewriteModule -ErrorAction SilentlyContinue`

**Do:**

```powershell
$rw = Get-WiDownload 'https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi' 'rewrite_amd64_en-US.msi'
$p = Start-Process msiexec.exe -ArgumentList '/i', "`"$rw`"", '/qn', '/norestart' -Wait -PassThru; "exit code $($p.ExitCode)"
```

**Verify:** `Get-WebGlobalModule -Name RewriteModule` returns `RewriteModule …\rewrite.dll`.

## 15. Application Request Routing 3.0 (passes `/api` to the API)

**Check:**

```powershell
Get-WebGlobalModule -Name ApplicationRequestRouting -ErrorAction SilentlyContinue
Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' | Select-Object enabled, timeout, preserveHostHeader, includePortInXForwardedFor
```

**Do:** install it and enable the proxy with a 10-minute timeout. Study reports with PDFs take 1–3 minutes, and ARR's
default timeout is 2 minutes. The last lines allow the rewrite rule to tell the API that the visitor used HTTPS (the
`X-Forwarded-Proto` header):

```powershell
$arr = Get-WiDownload 'https://download.microsoft.com/download/E/9/8/E9849D6A-020E-47E4-9FD0-A023E99B54EB/requestRouter_amd64.msi' 'requestRouter_amd64.msi'
$p = Start-Process msiexec.exe -ArgumentList '/i', "`"$arr`"", '/qn', '/norestart' -Wait -PassThru; "exit code $($p.ExitCode)"
$proxy = @{ PSPath = 'MACHINE/WEBROOT/APPHOST'; Filter = 'system.webServer/proxy' }
Set-WebConfigurationProperty @proxy -Name enabled -Value $true
Set-WebConfigurationProperty @proxy -Name timeout -Value '00:10:00'
Set-WebConfigurationProperty @proxy -Name preserveHostHeader -Value $true
Set-WebConfigurationProperty @proxy -Name reverseRewriteHostInResponseHeaders -Value $false
Set-WebConfigurationProperty @proxy -Name includePortInXForwardedFor -Value $false
if (-not (Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "system.webServer/rewrite/allowedServerVariables/add[@name='HTTP_X_FORWARDED_PROTO']")) {
    Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/rewrite/allowedServerVariables' -Name '.' -Value @{ name = 'HTTP_X_FORWARDED_PROTO' }
}
```

**Verify:**

```powershell
Get-WebGlobalModule -Name ApplicationRequestRouting | Select-Object Name                                   # ApplicationRequestRouting
Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' | Select-Object enabled, timeout, preserveHostHeader, includePortInXForwardedFor   # True 00:10:00 True False
Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/rewrite/allowedServerVariables/add' | Select-Object name   # HTTP_X_FORWARDED_PROTO
```

---

# Part D — SQL Server 2025 Express (the database)

Run every step in **[VPS PowerShell]**, with the session settings, `$WI` and the helpers from step 5 loaded.

## 16. Look for an existing SQL Server Express

**Check:**

```powershell
Get-Service -Name 'MSSQL$*' -ErrorAction SilentlyContinue | Select-Object Name, Status, StartType
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL' -ErrorAction SilentlyContinue)
```

- **No output:** SQL Server isn't installed. Continue with step 17.
- **`MSSQL$SQLEXPRESS` is listed:** SQL Server Express is already installed. **Skip steps 17 and 18**, but still do
  step 19 on it. You need its `sa` password only if you want to keep it for emergencies; step 7's `sa` password isn't
  used.

## 17. Download the installation media

**Check:** `Get-ChildItem (Join-Path $WI.Root 'install\sql') -Filter 'SQLEXPR*.exe' -ErrorAction SilentlyContinue`

**Do:**

```powershell
$ssei = Get-WiDownload 'https://go.microsoft.com/fwlink/?linkid=2216019' 'SQL2025-SSEI-Expr.exe'
$media = Join-Path $WI.Root 'install\sql'
New-Item -ItemType Directory -Force -Path $media | Out-Null
Start-Process $ssei -ArgumentList '/ACTION=Download', "/MEDIAPATH=$media", '/MEDIATYPE=Core', '/QUIET' -Wait
```

If no `SQLEXPR*.exe` appears: open `C:\wabitex-apps\wabi-insight\install` in File Explorer, double-click
`SQL2025-SSEI-Expr.exe`, choose **Download Media → Express Core**, and save to
`C:\wabitex-apps\wabi-insight\install\sql`. The link above always serves the current Express release (SQL Server 2025
as of this guide). SQL Server 2022 Express works the same way.

**Verify:** the **Check** command lists `SQLEXPR_x64_ENU.exe` (or similar).

## 18. Install SQL Server Express (mixed mode, TCP on)

This installs SQL Server unattended, with these choices:
- the instance `SQLEXPRESS`, started automatically with Windows;
- **mixed mode** (`/SECURITYMODE=SQL`), with the `sa` password from step 7;
- the local **Administrators** group as SQL Server administrators, so your Windows account can manage it;
- **TCP on** and named pipes off; the SQL Server Browser service off (not needed with a fixed port);
- no update check during setup (Windows Update handles updates later).

**Check:** `Get-Service 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue` (a result means it's installed: go to step
19).

**Do:** paste the **`sa` password** from step 7 when asked. Setup takes 5–15 minutes and shows nothing until it ends.

```powershell
. {
$sa = Read-WiSecret 'sa password (step 7)'
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
}
```

- **Exit code 3010:** restart the VPS (**Start → Power → Restart**), reconnect, and paste the session settings, `$WI`
  and the helpers (step 5) before **Verify**.
- **Any other code than 0 or 3010:** read `C:\Program Files\Microsoft SQL Server\*\Setup Bootstrap\Log\Summary.txt`.

**Verify:**

```powershell
Get-Service 'MSSQL$SQLEXPRESS' | Select-Object Status, StartType        # Running, Automatic
Get-Content "$env:ProgramFiles\Microsoft SQL Server\*\Setup Bootstrap\Log\Summary.txt" | Select-String 'Final result|Exit code' | Select-Object -First 2
```

## 19. Fixed TCP port 1433 and mixed-mode sign-in

The API connects to `localhost,1433`. Express uses a changing (dynamic) port by default, and an existing installation
may accept Windows accounts only.

**Check** (the **Do** block uses `$sqlKey` from here, so run this first):

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

## 20. The PowerShell `SqlServer` module

This guide runs SQL with `Invoke-Sqlcmd` from Microsoft's `SqlServer` module. It handles `GO` batches and works whether
or not `sqlcmd.exe` is installed. The nightly backup (step 30) uses it too.

**Check:** `Get-Module -ListAvailable SqlServer | Select-Object Name, Version`

**Do:**

```powershell
Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force | Out-Null
Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
Install-Module -Name SqlServer -Scope AllUsers -AllowClobber -Force
```

The helper `Invoke-WiSql` from step 5 now works. It signs in with your Windows administrator account, which setup made
a SQL Server administrator (`sysadmin`, step 18).

**Verify** the edition, the version and mixed mode:

```powershell
Invoke-WiSql "SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(64)) AS Edition, CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(32)) AS Version, CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int) AS WindowsAuthOnly"
# Express Edition (64-bit)   17.0.xxxx.x   0
```

## 21. The configuration database and its login

The API uses one SQL login, `wabiinsightdbowner`, that owns `WabiInsightDB`: `db_owner` on that database only, no
server-wide rights. Migrations run under it. The database stays empty until the first deployment creates the tables
(step 31).

**Check:**

```powershell
Invoke-WiSql "SELECT DB_ID(N'WabiInsightDB') AS DatabaseId, SUSER_ID(N'wabiinsightdbowner') AS LoginId"
```

If both have values (an earlier installation), keep the existing password and go to **Verify**.

**Do:** paste the **password of `wabiinsightdbowner`** from step 7 when asked:

```powershell
. {
$dbPassword = Read-WiSecret 'Password of wabiinsightdbowner (step 7)'
Invoke-WiSql "IF DB_ID(N'WabiInsightDB') IS NULL CREATE DATABASE [WabiInsightDB];"
Invoke-WiSql "IF SUSER_ID(N'wabiinsightdbowner') IS NULL CREATE LOGIN [wabiinsightdbowner] WITH PASSWORD = N'$dbPassword', DEFAULT_DATABASE = [WabiInsightDB], CHECK_POLICY = ON;"
Invoke-WiSql -Database WabiInsightDB "IF DATABASE_PRINCIPAL_ID(N'wabiinsightdbowner') IS NULL CREATE USER [wabiinsightdbowner] FOR LOGIN [wabiinsightdbowner]; ALTER ROLE db_owner ADD MEMBER [wabiinsightdbowner];"
}
```

**Verify** by signing in as the new login:

```powershell
. {
if (-not $dbPassword) { $dbPassword = Read-WiSecret 'Password of wabiinsightdbowner (step 7)' }
$dbCredential = New-Object pscredential 'wabiinsightdbowner', (ConvertTo-SecureString $dbPassword -AsPlainText -Force)
Invoke-WiSql -Database WabiInsightDB -Credential $dbCredential "SELECT DB_NAME() AS Db, IS_ROLEMEMBER('db_owner') AS DbOwner, IS_SRVROLEMEMBER('sysadmin') AS Sysadmin"
# WabiInsightDB 1 0
}
```

`Login failed` means a wrong password, or that step 19 (mixed mode) isn't done.

## 22. Check the connections the server needs

**Verify** (each line must print `True`):

```powershell
Test-NetConnection localhost -Port 1433 -InformationLevel Quiet                  # SQL Server Express (step 19)
Test-NetConnection github.com -Port 443 -InformationLevel Quiet                  # GitHub
Test-NetConnection api.github.com -Port 443 -InformationLevel Quiet              # GitHub's API (the runner, the deploy script download)
Test-NetConnection api.anthropic.com -Port 443 -InformationLevel Quiet           # the AI provider
[bool](Get-Command curl.exe -ErrorAction SilentlyContinue)                       # the deploy script uses curl.exe for its final check
```

`False` on the first line means step 19 isn't done. `False` on the others means the VPS provider blocks outgoing
traffic: ask them to allow it (step 2). The SMTP server is checked in step 33.

---

# Part E — Configure the application and the website

## 23. Application pools and permissions

| Pool | Site | Rights |
|---|---|---|
| `WabiInsightApi` | API | read `web\api`; modify the files folder (`D:\wabi-insight\files`), `keys` and `logs` |
| `WabiInsightWeb` | Public site | read `web\app` |

**[VPS PowerShell] Check:**

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
# API pool: always running (background e-mails, monitoring and periodic reports), no idle shutdown, one recycle at 03:00
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
icacls "$($WI.Root)\keys"                              # SYSTEM, Administrators and IIS AppPool\WabiInsightApi only
icacls "$($WI.Root)\web\api" | Select-String 'WabiInsightApi'
icacls $WI.Files | Select-String 'WabiInsightApi'      # (OI)(CI)(M)
```

## 24. This server's settings: `appsettings.Local.json`

The repository's `appsettings.json` is installed with every deployment. It holds the shared settings (owner decision
G): the SMTP account, the AI key, and a connection string for the configuration database. **You don't enter the SMTP
account or the AI key.**

`appsettings.Local.json`, next to it on this server, adds what is specific to this server:
- the connection to **this VPS's** SQL Server Express (steps 18–21);
- the site's address (used in every e-mail link);
- the files folder, kept on this server's disk;
- the key-ring folder.

It loads last and **overrides** `appsettings.json` (and environment variables). **Deployments never overwrite or delete
it.**

**[VPS PowerShell] Check:** `Test-Path (Join-Path $WI.Root 'web\api\appsettings.Local.json')`

**Do:** paste the **password of `wabiinsightdbowner`** (step 7) when asked. If the file exists already, the block leaves
it alone; to write it again, delete it first.

```powershell
. {
if (-not $dbPassword) { $dbPassword = Read-WiSecret 'Password of wabiinsightdbowner (step 7)' }
$localSettings = [ordered]@{
    ConnectionStrings = [ordered]@{ ConfigurationDatabase = "Server=localhost,1433;Database=WabiInsightDB;User Id=wabiinsightdbowner;Password=$dbPassword;Encrypt=True;TrustServerCertificate=True;" }
    Platform          = [ordered]@{ BaseUrl = "https://$($WI.Domain)" }
    Storage           = [ordered]@{ Provider = 'FileSystem'; RootPath = $WI.Files }
    DataProtection    = [ordered]@{ KeyRingPath = (Join-Path $WI.Root 'keys') }
}
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
if (-not (Test-Path $localPath)) { $localSettings | ConvertTo-Json -Depth 5 | Set-Content -Path $localPath -Encoding UTF8 }
Remove-Variable dbPassword, dbCredential -ErrorAction SilentlyContinue
}
```

- **Database on another server:** change `ConfigurationDatabase` to that server.
- **To keep the committed connection string** from `appsettings.json` instead (for example when it already names this
  server and its password), remove the `ConnectionStrings` entry.
- **Other overrides** work the same way, for example `Email = @{ Password = '…' }`.
- **Don't use `//` comments** in this file: the deploy script reads it with PowerShell's JSON reader, which rejects them.
- **Don't add a `Bootstrap` section** to this file: it overrides environment variables, and step 32 turns bootstrap on
  through them for one command only.

**Verify:** this shows the file with the password hidden:

```powershell
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
(Get-Content $localPath -Raw) -replace 'Password=[^;]*', 'Password=***'
(Get-Content $localPath -Raw | ConvertFrom-Json).Platform.BaseUrl          # https://<your domain>
(Get-Content $localPath -Raw | ConvertFrom-Json).Storage.RootPath          # D:\wabi-insight\files (or C:\…)
```

## 25. The two IIS sites

The API gets its own site that answers **only on this machine** (`127.0.0.1:5080`). The public site serves the web app
and passes `/api` and `/health` to it, so the browser sees one address and the session cookie stays first-party.

> Don't mount the API as an IIS sub-application named `api`. IIS would remove `/api` from every path, and all API
> routes would return 404.

**[VPS PowerShell] Check:**

```powershell
Get-Website | Select-Object Name, State, @{n='Bindings';e={$_.bindings.Collection.bindingInformation -join ', '}}
```

**Do:** create both sites, and stop IIS's *Default Web Site*:

```powershell
Import-Module WebAdministration
if (-not (Get-Website -Name 'WabiInsightApi')) {
    New-Website -Name 'WabiInsightApi' -PhysicalPath (Join-Path $WI.Root 'web\api') -ApplicationPool 'WabiInsightApi' -IPAddress '127.0.0.1' -Port 5080 | Out-Null
}
Set-ItemProperty 'IIS:\Sites\WabiInsightApi' -Name applicationDefaults.preloadEnabled -Value $true
if (Get-Website -Name 'Default Web Site') {
    Stop-Website 'Default Web Site'
    Set-ItemProperty 'IIS:\Sites\Default Web Site' -Name serverAutoStart -Value $false
}
if (-not (Get-Website -Name 'WabiInsight')) {
    New-Website -Name 'WabiInsight' -PhysicalPath (Join-Path $WI.Root 'web\app') -ApplicationPool 'WabiInsightWeb' -Port 80 -HostHeader $WI.Domain | Out-Null
}
Start-Website 'WabiInsightApi'; Start-Website 'WabiInsight'
```

**Verify:**

```powershell
Get-Website 'WabiInsightApi', 'WabiInsight' | Select-Object Name, State, @{n='Binding';e={$_.bindings.Collection.bindingInformation -join ', '}}
# WabiInsightApi Started 127.0.0.1:5080:      WabiInsight Started *:80:<your domain>
```

Both folders are still empty, so the sites show errors until the first deployment (step 31). That's expected.

## 26. Windows Firewall and the HTTPS certificate

IIS opens ports 80 and 443 when it's installed. SQL Server Express adds no inbound rule, so port 1433 stays private.

**[VPS PowerShell] Check:**

```powershell
Get-NetFirewallRule -DisplayGroup 'World Wide Web Services (HTTP)', 'Secure World Wide Web Services (HTTPS)' -ErrorAction SilentlyContinue |
    Select-Object DisplayName, Enabled, Direction, Action
Get-NetFirewallPortFilter | Where-Object LocalPort -eq 1433 | Get-NetFirewallRule | Where-Object { $_.Enabled -eq 'True' -and $_.Direction -eq 'Inbound' } | Select-Object DisplayName
Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -like "*$($WI.Domain)*" -or $_.DnsNameList.Unicode -contains $WI.Domain } | Select-Object Subject, NotAfter, Thumbprint
```

**Do:**

1. Open ports 80 and 443 in Windows, if the IIS rules are missing or disabled:

   ```powershell
   if (-not (Get-NetFirewallRule -DisplayName 'Wabi Insight HTTP' -ErrorAction SilentlyContinue)) {
       New-NetFirewallRule -DisplayName 'Wabi Insight HTTP'  -Direction Inbound -Protocol TCP -LocalPort 80  -Action Allow | Out-Null
       New-NetFirewallRule -DisplayName 'Wabi Insight HTTPS' -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow | Out-Null
   }
   ```

2. Get the certificate. Choose **one** option.

   **Option A (recommended): a free Let's Encrypt certificate with win-acme.** It renews by itself. DNS (step 3) must
   already point at the VPS, and port 80 must be open (step 2):

   ```powershell
   $release = Invoke-RestMethod 'https://api.github.com/repos/win-acme/win-acme/releases/latest' -Headers @{ 'User-Agent' = 'wabi-insight-setup' }
   $asset = $release.assets | Where-Object name -match 'x64\.trimmed\.zip$' | Select-Object -First 1
   $zip = Get-WiDownload $asset.browser_download_url $asset.name
   $wacsDir = Join-Path $WI.Root 'tools\win-acme'
   Expand-Archive $zip -DestinationPath $wacsDir -Force
   $siteId = (Get-Website -Name 'WabiInsight').Id
   & (Join-Path $wacsDir 'wacs.exe') --source iis --siteid $siteId --host $WI.Domain --emailaddress $WI.CertEmail --accepttos
   ```

   win-acme validates over port 80, stores the certificate in the machine store, adds the `https` binding (port 443,
   SNI) to the site, and creates a daily scheduled task that renews it. If the command reports an argument error in
   your win-acme version, run `& (Join-Path $wacsDir 'wacs.exe')` without arguments and choose **N: Create certificate
   (default settings)** → the site **WabiInsight**.

   **Option B: a certificate you already have (`.pfx` file).** Copy the file to the VPS, then (replace the path):

   ```powershell
   . {
   $pfxPassword = Read-Host 'PFX password' -AsSecureString
   $cert = Import-PfxCertificate -FilePath 'C:\path\to\certificate.pfx' -CertStoreLocation Cert:\LocalMachine\My -Password $pfxPassword
   if (-not (Get-WebBinding -Name 'WabiInsight' -Protocol https)) {
       New-WebBinding -Name 'WabiInsight' -Protocol https -Port 443 -HostHeader $WI.Domain -SslFlags 1
   }
   (Get-WebBinding -Name 'WabiInsight' -Protocol https).AddSslCertificate($cert.Thumbprint, 'My')
   }
   ```

   You renew this certificate yourself before it expires: import the new file the same way.

**Verify:**

```powershell
Get-WebBinding -Name 'WabiInsight' | Select-Object protocol, bindingInformation, sslFlags        # http *:80:<domain> and https *:443:<domain> (sslFlags 1 = SNI)
netsh http show sslcert hostnameport="$($WI.Domain):443" | Select-String 'Certificate Hash|Store Name'
Get-ScheduledTask | Where-Object TaskName -like 'win-acme*' | Select-Object TaskName, State    # Option A: the renewal task, Ready
```

Run the **Check** block again too: the HTTP and HTTPS rules show `True / Inbound / Allow`, no enabled inbound rule lists
port 1433, and the certificate is listed with an expiry date (`NotAfter`) in the future.

---

# Part F — CI/CD: deploy from GitHub with one click

## 27. How CI and CD work in this repository

| Part | File | When it runs | What it does |
|---|---|---|---|
| **CI**, already active | `.github/workflows/ci.yml` | **Automatically** on every pull request and every merge to `main` | Builds the API, runs the unit and SQL Server integration tests, checks dependencies for known vulnerabilities, lints, tests and builds the web app, and builds the Docker images. A red ✗ on a pull request means don't merge it yet. |
| **CD** | `.github/workflows/deploy-windows.yml` (**Deploy (Windows)**) | **Only when you click Run workflow** | GitHub builds the parts you choose (`build-release.yml`). The runner on your VPS installs them with `C:\wabitex-apps\wabi-insight\deploy\Deploy-WabiInsight.ps1`. |

```
 You: GitHub → Actions → Deploy (Windows) → Run workflow
   │   component: frontend+backend | backend | frontend    migrate_database: on/off    run_tests: on/off    ref: (optional)
   ▼
 GitHub's computers: build and test → package the API and the web app
   ▼
 The runner service on YOUR VPS (it only makes outgoing connections to GitHub; no port is opened)
   └─ Deploy-WabiInsight.ps1:
        API     : stage → keep the previous version → take the API offline → install → [backup task
                  "Wabi Insight backup" + database migrations] → back online → wait until healthy
                  (backup, migration or health check fails → the previous API is put back automatically)
        web app : stage → keep the previous version → install (index.html last)
        both    : final check through IIS: https://<domain>/ answers 200 and /health/ready is Healthy
```

- **Before migrations**, the deploy script runs the nightly backup task (step 30) and waits for it, up to 15 minutes.
  If the backup fails, nothing is migrated and the previous API is put back.
- A migration that has run **stays applied**, even when the deployment fails afterwards. To undo it, restore the backup
  (step 39).
- **No server secret is stored in GitHub.** The server keeps its own `appsettings.Local.json`.
- The same workflow also serves the Azure setup ([DEPLOYMENT_WINDOWS_AZURE.md](DEPLOYMENT_WINDOWS_AZURE.md)). The deploy
  script tells the two apart from the connection string in `appsettings.Local.json`.

**The run options:**

| Option | Values | Meaning |
|---|---|---|
| **Use workflow from** | a branch (normally `main`) | The code that is built. |
| **component** | `frontend+backend` (default), `backend`, `frontend` | What to deploy. |
| **migrate_database** | off (default) / on | Apply database migrations, after the backup task. Only together with the backend; ignored, with a warning, for `frontend`. |
| **run_tests** | on (default) / off | Unit tests (API) and lint + tests (web) before packaging. CI has already run the full suite on the pull request. |
| **ref** | empty, or a commit SHA or tag | Deploy that exact version instead of the branch tip, for example to roll back (step 39). |

## 28. A GitHub environment for the server

**Where:** [GitHub] → the repository → **Settings → Environments**.

**Check:** is `production-windows` listed?

**Do:**
1. **New environment** → name `production-windows` → **Configure environment**.
2. **Deployment branches and tags:** *Selected branches and tags* → **Add deployment branch or tag rule** → `main`.
3. Optional: **Required reviewers**: add yourself, to approve each deployment before it touches the server.
4. **Environment variables → Add variable:** `WI_PUBLIC_URL` = `https://<your domain>`.

On GitHub Free, private repositories don't get branch rules and reviewers; the workflow still runs without them.

**Verify:** `production-windows` is listed with the variable.

## 29. Install the runner on the VPS (as a Windows service)

The runner runs as **LocalSystem**, because deployments write to protected folders, take the API offline, start the
backup task and run migrations.

**[VPS PowerShell] Check:**

```powershell
Test-Path 'C:\actions-runner\config.cmd'
Get-Service -Name 'actions.runner.*' -ErrorAction SilentlyContinue | Select-Object Name, Status, StartType
```

**Do, part 1:** download the runner into a folder only administrators can read (it will hold its credentials), and
check the download's fingerprint:

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

**Do, part 2:** register it:
1. **[GitHub]** **Settings → Actions → Runners → New self-hosted runner → Windows**. In the *Configure* box, copy only
   the token after `--token` (a long code). It's valid for one hour.
2. **[VPS PowerShell]** paste the token when asked:

```powershell
. {
Set-Location 'C:\actions-runner'
$token = Read-Host 'Runner registration token'
.\config.cmd --unattended --url "https://github.com/$($WI.Repo)" --token $token `
    --name "$env:COMPUTERNAME-wabi-insight" --labels wabi-insight --work _work --runasservice --replace
Remove-Variable token
$service = Get-Service -Name 'actions.runner.*' | Select-Object -First 1
sc.exe config $service.Name obj= LocalSystem | Out-Null          # deployments change protected folders
sc.exe config $service.Name start= delayed-auto | Out-Null
Restart-Service $service.Name
}
```

**Verify:**

```powershell
Get-CimInstance Win32_Service -Filter "Name LIKE 'actions.runner.%'" | Select-Object Name, State, StartName, StartMode   # Running, LocalSystem, Auto
Get-Content (Get-ChildItem 'C:\actions-runner\_diag' -Filter 'Runner_*.log' | Sort-Object LastWriteTime | Select-Object -Last 1).FullName -Tail 20 | Select-String 'Listening for Jobs'
```

On **[GitHub] Settings → Actions → Runners**, the runner shows **Idle**, with the labels `self-hosted`, `Windows`, `X64`
and `wabi-insight`.

## 30. Nightly backups: database, key ring and files

A restore needs **three things together**: the database, the key ring (`C:\wabitex-apps\wabi-insight\keys`) and the
files (`D:\wabi-insight\files`). SQL Server Express has no SQL Server Agent, so a Windows scheduled task named
**`Wabi Insight backup`** does it every night at 02:30. It writes to `D:\wabi-insight\backup`:

| What | How | Kept |
|---|---|---|
| `WabiInsightDB-<stamp>.bak` | `BACKUP DATABASE … WITH CHECKSUM` | 14 days |
| `keys-<stamp>.zip` | the key ring | 14 days |
| `files\` | a mirror of `D:\wabi-insight\files` (robocopy copies only the changes) | current |

`<stamp>` is the server's local date and time, `yyyyMMdd-HHmm`.

- **The deploy script runs this same task before every database migration**, and stops if it fails. Keep the task name
  exactly `Wabi Insight backup`.
- The task runs as `SYSTEM`, which gets backup rights on `WabiInsightDB` only, so no password is stored on disk.
- **The backups sit on the same server as the data.** If the VPS is lost, they're lost too. Copy
  `D:\wabi-insight\backup` somewhere else regularly (step 38).

**[VPS PowerShell] Check:**

```powershell
Get-ScheduledTask -TaskName 'Wabi Insight backup' -ErrorAction SilentlyContinue | Select-Object TaskName, State
Test-Path (Join-Path $WI.Backup 'backup.ps1')
```

**Do:** this lets SQL Server write into the backup folder, gives `SYSTEM` backup rights, writes the backup script
`backup.ps1` into the backup folder, and registers the task:

```powershell
. {
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
if (Get-ChildItem $Keys -Force) {        # the key ring stays empty until the API first starts (step 31)
    Compress-Archive -Path $Keys -DestinationPath (Join-Path $PSScriptRoot "keys-$stamp.zip") -Force
}
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
}
```

The key ring is still empty at this point; it gets its first key when the API starts (step 31). Test the task after
that.

**Verify** (after step 31). Checking the `.bak` file needs `sysadmin`, which your administrator account has:

```powershell
Start-ScheduledTask -TaskName 'Wabi Insight backup'; Start-Sleep -Seconds 30
(Get-ScheduledTaskInfo -TaskName 'Wabi Insight backup').LastTaskResult             # 0
$latest = Get-ChildItem $WI.Backup -Filter 'WabiInsightDB-*.bak' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Invoke-WiSql "RESTORE VERIFYONLY FROM DISK = N'$($latest.FullName)' WITH CHECKSUM;" -Verbose 4>&1 | Select-Object -Last 1   # The backup set on file 1 is valid.
Get-ChildItem $WI.Backup -Filter 'keys-*.zip' | Select-Object -Last 1 Name, Length
Get-ChildItem (Join-Path $WI.Backup 'files') | Select-Object Name                     # the same folders as D:\wabi-insight\files
```

## 31. Install the deploy script, then the first deployment

**Where:** first [GitHub], then [VPS PowerShell].

**Check:**

```powershell
$deployScript = Join-Path $WI.Root 'deploy\Deploy-WabiInsight.ps1'
if (Test-Path $deployScript) { & $deployScript -ScriptVersion } else { 'not installed' }     # 3 = the current version is installed
```

The runner runs this installed copy, never a copy from GitHub. Only Administrators and SYSTEM can change it (step 10),
so a workflow can't replace the deploy logic.

**Do, part 1:** create a short-lived GitHub token, used once to download the script:
1. **[GitHub]** your profile picture → **Settings → Developer settings → Personal access tokens → Fine-grained
   tokens → Generate new token**.
2. Fill in:
   - **Name:** `wabi-insight-deploy-script`;
   - **Resource owner:** the owner of the repository (`mtawfig`);
   - **Expiration:** 7 days;
   - **Repository access:** *Only select repositories* → `mtawfig/wabi-data-harbor`;
   - **Permissions → Repository permissions → Contents:** **Read-only**.
3. **Generate token** and copy it.

If you can't select the repository there, skip the token: on GitHub open `deploy/cicd/windows/Deploy-WabiInsight.ps1`
in the repository → **Download raw file**, copy the file into `C:\wabitex-apps\wabi-insight\deploy` on the VPS (copy
and paste work through Remote Desktop), then run only the first line and the last two lines of part 2.

**Do, part 2:** **[VPS PowerShell]** paste the token when asked:

```powershell
. {
$deployDir = Join-Path $WI.Root 'deploy'
$ghToken = Read-WiSecret 'GitHub token (Contents: read)'
Invoke-WebRequest -UseBasicParsing -OutFile (Join-Path $deployDir 'Deploy-WabiInsight.ps1') `
    -Uri "https://api.github.com/repos/$($WI.Repo)/contents/deploy/cicd/windows/Deploy-WabiInsight.ps1?ref=main" `
    -Headers @{ Authorization = "Bearer $ghToken"; Accept = 'application/vnd.github.raw'; 'User-Agent' = 'wabi-insight-setup' }
Remove-Variable ghToken
Unblock-File (Join-Path $deployDir 'Deploy-WabiInsight.ps1')                # downloaded files are blocked by default
& (Join-Path $deployDir 'Deploy-WabiInsight.ps1') -ScriptVersion            # 3
}
```

Then delete the token: **[GitHub]** *Fine-grained tokens* → the token → **Delete**.

**Verify** the script and the rights:

```powershell
Get-Content (Join-Path $WI.Root 'deploy\Deploy-WabiInsight.ps1') -TotalCount 5    # starts with <# .SYNOPSIS Installs a Wabi Insight release …
icacls (Join-Path $WI.Root 'deploy')                                              # SYSTEM and Administrators only
Get-ExecutionPolicy                                                               # RemoteSigned (the Windows Server default) or Unrestricted
```

**Do, part 3:** the first deployment. **Actions** lists **Deploy (Windows)** only once the workflow file is on `main`;
it is.
1. **[GitHub]** **Actions** → **Deploy (Windows)** (left list) → **Run workflow**.
2. **Use workflow from:** `main`.
3. **component:** `frontend+backend`.
4. **migrate_database:** ✅. This creates the tables in the empty database.
5. **run_tests:** ✅.
6. **ref:** empty.
7. **Run workflow**, then click the new run to watch it. With required reviewers (step 28), approve it with **Review
   deployments**.

| Job | Runs on | Duration (first run) |
|---|---|---|
| Build / Resolve version | GitHub | about 20 seconds |
| Build / Backend (.NET 10) | GitHub | 2–4 minutes |
| Build / Frontend (Angular 22) | GitHub | 2–4 minutes (at the same time) |
| Deploy to Windows server | your VPS | 1–3 minutes |

The deploy log shows each stage: `==> Backend: …`, `==> Database: backup before migration (scheduled task 'Wabi Insight
backup')`, `==> Database: applying migrations`, `==> Frontend: …`, `==> Site check through IIS: https://<domain>`. It
ends with `Deployment <release> succeeded.` The run summary lists the component, the migration flag, the version and
who started it.

**Verify** ([VPS PowerShell]):

```powershell
Get-Content (Join-Path $WI.Root 'logs\deploy.log') -Tail 3                                   # … started … / … succeeded release=…
Get-Content (Join-Path $WI.Root 'web\api\deploy-info.json')                                  # "version": the commit you deployed
Test-Path (Join-Path $WI.Root 'web\api\app_offline.htm')                                     # False
Invoke-WiSql -Database WabiInsightDB "SELECT COUNT(*) AS Applied, MAX(MigrationId) AS Latest FROM dbo.__EFMigrationsHistory"   # a number and the newest migration's name
(Invoke-WebRequest 'http://127.0.0.1:5080/health/ready' -UseBasicParsing).Content          # "status":"Healthy" … configuration-database … Healthy
Invoke-WebRequest 'http://127.0.0.1:5080/api/auth/csrf' -UseBasicParsing | Out-Null          # first use of the key ring
Get-ChildItem (Join-Path $WI.Root 'keys')                                                  # key-<id>.xml: the key ring now exists
(Invoke-WebRequest "https://$($WI.Domain)/health/ready" -UseBasicParsing).Content          # Healthy, through IIS and ARR
$r = Invoke-WebRequest "http://$($WI.Domain)/login" -MaximumRedirection 0 -UseBasicParsing -ErrorAction SilentlyContinue
"$($r.StatusCode) $($r.Headers.Location)"                                                  # 301 https://<your domain>/login
$r = Invoke-WebRequest "https://$($WI.Domain)/" -UseBasicParsing
$r.Headers['Strict-Transport-Security']                                                    # max-age=31536000; includeSubDomains
Get-ChildItem (Join-Path $WI.Root 'releases') | Select-Object Name                         # previous-… and one folder per release
```

Now run the **Verify** of step 30 (backups).

## 32. Create the first Platform Administrator (once)

**[VPS PowerShell] Check:** `0` means not done yet:

```powershell
Invoke-WiSql -Database WabiInsightDB "SELECT COUNT(*) AS Administrators FROM dbo.Administrators"
```

Bootstrap is **off** in `appsettings.json`. Environment variables override `appsettings.json`, so the variables below
turn it on for this one command only. (`appsettings.Local.json` would override them, which is why it must not contain a
`Bootstrap` section.)

**Do:**
1. Choose the administrator's first password: at least 12 characters with upper case, lower case, a digit and a symbol.
   You change it at first sign-in.
2. Run this. The bootstrap settings exist only inside this PowerShell process and are removed afterwards:

```powershell
. {
$adminPassword = Read-WiSecret 'First password for the Platform Administrator'
Push-Location (Join-Path $WI.Root 'web\api')
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:Bootstrap__Enabled = 'true'
$env:Bootstrap__AdministratorEmail = $WI.AdminEmail
$env:Bootstrap__AdministratorFullName = 'Platform Administrator'
$env:Bootstrap__AdministratorPassword = $adminPassword
$env:Bootstrap__InitializationSecret = New-WiSecret 40
dotnet .\WabiInsight.Api.dll bootstrap
'Bootstrap__Enabled','Bootstrap__AdministratorEmail','Bootstrap__AdministratorFullName','Bootstrap__AdministratorPassword','Bootstrap__InitializationSecret' |
    ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue }
Remove-Variable adminPassword
Pop-Location
}
```

**Verify:** the output includes `Bootstrap completed. AdministratorId=…`, the **Check** query returns `1`, and:

```powershell
Get-ChildItem Env: | Where-Object Name -like 'Bootstrap__*'          # no output
$appSettings = (Get-Content (Join-Path $WI.Root 'web\api\appsettings.json') | Where-Object { $_ -notmatch '^\s*//' }) -join "`n" | ConvertFrom-Json
$appSettings.Bootstrap.Enabled                                        # False: bootstrap stays off for the running site
```

(`appsettings.json` contains `//` comment lines, which Windows PowerShell's JSON reader rejects; the second line skips
them.)

## 33. Go-live checks

**[VPS PowerShell] Verify** that the server reaches the e-mail server and the AI provider:

```powershell
$appSettings = (Get-Content (Join-Path $WI.Root 'web\api\appsettings.json') | Where-Object { $_ -notmatch '^\s*//' }) -join "`n" | ConvertFrom-Json
Test-NetConnection $appSettings.Email.Host -Port $appSettings.Email.Port -InformationLevel Quiet   # True: the SMTP server is reachable
Test-NetConnection api.anthropic.com -Port 443 -InformationLevel Quiet                             # True: the AI provider is reachable
Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' -Name enabled   # True
```

**[Browser] Verify**, from your own computer:
1. Open `https://<your domain>`. The landing page loads with a padlock, and **العربية** switches the layout to right to
   left.
2. **Sign in** with the administrator e-mail and the first password. You're asked to change the password; do it.
3. **System Health** (in the platform menu): the database and the API are healthy.
4. **AI Providers:** the default provider is listed, and **Test** succeeds.
5. **Organizations → New organization**, with a real e-mail address for its administrator → **Create organization**.
   The **temporary password e-mail** arrives, which proves e-mail works. (Temporary passwords are only e-mailed, never
   shown on screen.)
6. Sign in as that organization administrator in a private window and change the password. Then **Organization
   Administration → Organization Settings → Report logo → Upload logo**. On the VPS,
   `Get-ChildItem (Join-Path $WI.Files 'organization-logos') -Recurse -File` lists a `.bin` file. This proves the files
   go to the files folder.
7. Register an application under **Organization Administration → Applications** (your organization's database,
   read-only), or load the samples (step 34). Then ask a question on **Ask Wabi Insight**.

---

# Part G — Optional extras

## 34. Optional: sample databases and sample users

This fills the platform with realistic data for a fictional company, Gulf Aviation Services: flight operations,
HR/payroll/attendance and procurement/stores, with four users of different roles. The details, the sample users and
their initial password are in [`scripts/sql/samples/README.md`](../scripts/sql/samples/README.md).

**[VPS PowerShell] Check:**

```powershell
Invoke-WiSql "SELECT name FROM sys.databases WHERE name IN (N'FlightOperationsDB', N'HumanResourcesDB', N'ProcurementStoresDB')"
```

**Do, part 1:** download the four scripts. You need a GitHub token again: create one exactly as in step 31, part 1, and
delete it afterwards.

```powershell
. {
$samples = Join-Path $WI.Root 'install\samples'
New-Item -ItemType Directory -Force -Path $samples | Out-Null
$sampleFiles = '01-create-flightoperationsdb.sql', '02-create-humanresourcesdb.sql', '03-create-procurementstoresdb.sql', '04-seed-wabiinsightdb.sql'
$ghToken = Read-WiSecret 'GitHub token (Contents: read)'
foreach ($f in $sampleFiles) {
    Invoke-WebRequest -UseBasicParsing -OutFile (Join-Path $samples $f) `
        -Uri "https://api.github.com/repos/$($WI.Repo)/contents/scripts/sql/samples/$($f)?ref=main" `
        -Headers @{ Authorization = "Bearer $ghToken"; Accept = 'application/vnd.github.raw'; 'User-Agent' = 'wabi-insight-setup' }
}
Remove-Variable ghToken
}
```

Without a token: download the four files from the repository's `scripts/sql/samples` folder on GitHub (each file →
**Download raw file**), copy them into `C:\wabitex-apps\wabi-insight\install\samples`, and run only the first three
lines of the block above.

Then save the files as UTF-8 with a byte-order mark, so the Arabic names are read correctly:

```powershell
foreach ($f in $sampleFiles) {
    $path = Join-Path $samples $f
    [IO.File]::WriteAllText($path, [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8), (New-Object Text.UTF8Encoding $true))
}
Get-ChildItem $samples | Select-Object Name, Length          # the four files, none of them 0 bytes
```

**Do, part 2:** run them in order (about 2 minutes):

```powershell
$sampleFiles | ForEach-Object {
    "== $_"
    Invoke-WiSql -InputFile (Join-Path $samples $_) -Tables
}
```

**Verify:**
1. The last script lists the four users and the three applications.
2. **[Browser]** sign in as `faisal.alshamsi@gulfaviation.example` (the organization administrator) and change the
   password.
3. **Organization Administration → Applications**. For each of the three applications:
   - **Replace connection** with `Server=localhost,1433;Database=…;User Id=…_reader;Password=…;Encrypt=True;TrustServerCertificate=True;`
     (the script prints the database and reader login of each; the passwords are in the README);
   - **Test connection**: it must report *read-only verified*;
   - **Approve for querying**.

This last part can't be scripted: connection strings are encrypted with this server's key ring.

**Change the sample passwords** (the users' and the SQL logins', see the README) before anyone else uses the server.

A second sample organization, **Wabi Bank**, works the same way with the scripts in
[`scripts/sql/samples/wabi-bank`](../scripts/sql/samples/wabi-bank/README.md) (01–05, then 06; 07 must report 0).

## 35. Optional: a dedicated login for Platform Insights

Platform Insights can run under a login that can read only the `insights` schema. Without it, the API logs a warning
and uses the configuration login. Recommended before production. This does the same as
`scripts/sql/create-insights-login.sql`, for the database `WabiInsightDB`.

**[VPS PowerShell] Check:** empty output means not configured:

```powershell
(Get-Content (Join-Path $WI.Root 'web\api\appsettings.Local.json') -Raw | ConvertFrom-Json).Insights.ConnectionString
```

**Do:** the password is generated and written straight into `appsettings.Local.json`; you don't need to type it
anywhere. Running the block again sets a new password in both places.

```powershell
$insightsPassword = New-WiSecret
Invoke-WiSql "IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'wabi_insight_insights') CREATE LOGIN [wabi_insight_insights] WITH PASSWORD = N'$insightsPassword', CHECK_POLICY = ON ELSE ALTER LOGIN [wabi_insight_insights] WITH PASSWORD = N'$insightsPassword';"
Invoke-WiSql -Database WabiInsightDB "IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'wabi_insight_insights') CREATE USER [wabi_insight_insights] FOR LOGIN [wabi_insight_insights];"
Invoke-WiSql -Database WabiInsightDB "GRANT SELECT ON SCHEMA::[insights] TO [wabi_insight_insights]; DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[dbo] TO [wabi_insight_insights];"
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
$settings = Get-Content $localPath -Raw | ConvertFrom-Json
$settings | Add-Member -Force -NotePropertyName Insights -NotePropertyValue ([pscustomobject]@{
    ConnectionString = "Server=localhost,1433;Database=WabiInsightDB;User Id=wabi_insight_insights;Password=$insightsPassword;Encrypt=True;TrustServerCertificate=True;" })
$settings | ConvertTo-Json -Depth 6 | Set-Content $localPath -Encoding UTF8
Remove-Variable insightsPassword
Restart-WebAppPool 'WabiInsightApi'
```

**Verify:** **[Browser]** **Platform Insights Configuration** shows *Confined to the insights schema*.

## 36. Optional: keep the files in Azure Blob Storage

Files stay on the VPS disk unless you switch them to a private Azure Blob Storage container. You can do this at any
time. The database stays in SQL Server Express.

To create the storage account and the container, follow [DEPLOYMENT_WINDOWS_AZURE.md](DEPLOYMENT_WINDOWS_AZURE.md):
step 8 (only part 1, the resource group), step 11, step 12 (only the container `wabi-insight-files`) and step 13. The
VPS then needs outgoing HTTPS to `<account>.blob.core.windows.net`. The settings are explained in
[DEPLOYMENT.md, File storage](DEPLOYMENT.md#file-storage-server-disk-or-azure-blob-storage).

**[VPS PowerShell] Do:** paste the storage account's **connection string** (*Access keys → key1*) when asked. It's read
without being shown:

```powershell
. {
$cs = Read-WiSecret 'Azure Storage connection string'
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
$settings = Get-Content $localPath -Raw | ConvertFrom-Json
$settings.Storage | Add-Member -Force -NotePropertyName Provider -NotePropertyValue 'AzureBlob'
$settings.Storage | Add-Member -Force -NotePropertyName AzureBlob -NotePropertyValue ([pscustomobject]@{ ConnectionString = $cs })
$settings | ConvertTo-Json -Depth 6 | Set-Content $localPath -Encoding UTF8
Remove-Variable cs
Push-Location (Join-Path $WI.Root 'web\api'); $env:ASPNETCORE_ENVIRONMENT = 'Production'
dotnet .\WabiInsight.Api.dll storage-check     # Storage check passed: a test file was written, read back identical and deleted.
Pop-Location
Restart-WebAppPool 'WabiInsightApi'
}
```

`storage-check` never prints the key. On failure it says why (HTTP 403: wrong key or network rules; 404: wrong
account).

**Then, once:** copy the files already on the disk into the container. It's safe to repeat; it skips files already
there:

```powershell
Push-Location (Join-Path $WI.Root 'web\api'); $env:ASPNETCORE_ENVIRONMENT = 'Production'
dotnet .\WabiInsight.Api.dll storage-copy      # Storage copy finished: … copied, … already in the container, 0 failed.
Pop-Location
```

**Verify:** upload a logo again (step 33, item 6). In the Azure portal → the storage account → **Containers →
wabi-insight-files**, a folder `organization-logos` appears. Files saved before the switch still open from the disk
until they're copied.

- After the switch, every backend deployment runs `storage-check` and reports a problem with the container.
- The nightly task (step 30) still mirrors the disk folder, which no longer receives new files. Protect the container
  with Azure's soft delete instead (step 11 of the Azure guide).
- To switch back, set `Provider` to `FileSystem` in `appsettings.Local.json` and restart the pool. Files created while
  on Azure stay in the container.
- Switching the line in the repository's `appsettings.json` would not change this server: its `appsettings.Local.json`
  sets `Provider` itself (step 24).

---

# Part H — Running it day to day

## 37. Everyday deployments

**[GitHub] Actions → Deploy (Windows) → Run workflow**, with `main`:

| What changed | component | migrate_database |
|---|---|---|
| Only the web app (screens, texts, translations) | `frontend` | off |
| API code without a new migration | `backend` | off |
| A change that adds a migration (new files under `src/WabiInsight.Infrastructure/Persistence/Migrations/`) | `frontend+backend` | **on** |
| Both, no migration | `frontend+backend` | off |
| Not sure | `frontend+backend` | on (an empty migration step is harmless: *Configuration database is up to date*, and you get a fresh backup) |

`C:\wabitex-apps\wabi-insight\web\api\deploy-info.json` shows which commit is live. To see what changed since, open
`https://github.com/mtawfig/wabi-data-harbor/compare/<deployed sha>...main` and look for new files under
`Persistence/Migrations`.

## 38. Regular upkeep

| How often | What | How |
|---|---|---|
| Monthly | Windows updates | Step 8 (restart after) |
| Monthly | .NET 10 security updates for the API | Re-run step 13's **Do** with a fresh download: delete `C:\wabitex-apps\wabi-insight\install\dotnet-hosting-10.0-win.exe` first |
| Monthly | SQL Server updates | **Settings → Update & Security → Windows Update → Advanced options → Give me updates for other Microsoft products when I update Windows** (once), then step 8 |
| Weekly | **Copy the backups off the server** | Copy `D:\wabi-insight\backup` to another computer or storage, or use your VPS provider's backup or snapshot service. The copy holds the database and its keys together: keep it private. |
| Weekly | Check the backups run | `Get-ScheduledTaskInfo -TaskName 'Wabi Insight backup' \| Select-Object LastRunTime, LastTaskResult` → last night and `0`. Step 30 **Verify** also checks a `.bak` file. |
| Monthly | Database size (Express limit: 10 GB on 2019/2022, 50 GB on 2025) | `Invoke-WiSql -Database WabiInsightDB "EXEC sp_spaceused"` |
| When the deploy job warns "differs" | The deploy script changed in the repository | Step 31, parts 1 and 2 |
| Automatic | TLS certificate | win-acme renews it (scheduled task). A `.pfx` certificate (option B) you renew yourself. |
| Automatic | The runner | It updates itself |

## 39. Rolling back

**Code only (no migration in between):**
- **With GitHub:** **Run workflow** (from `main`) with **ref** = the commit you want back. It's in
  `C:\wabitex-apps\wabi-insight\logs\deploy.log` (`version=`) or in an earlier run's summary. Choose the same component
  and leave **migrate_database** off.
- **Instantly, without GitHub:** the version before the last deployment is kept on the server:

```powershell
$r = $WI.Root
Set-Content "$r\web\api\app_offline.htm" 'Updating'; Start-Sleep -Seconds 5
robocopy "$r\releases\previous-api" "$r\web\api" /MIR /XF appsettings.Local.json app_offline.htm /NFL /NDL /NJH /NJS
Remove-Item "$r\web\api\app_offline.htm"
robocopy "$r\releases\previous-web" "$r\web\app" /MIR /NFL /NDL /NJH /NJS
```

Older versions are in `releases\<date>-<version>\api` and `\web` (the last 5). Use those folders in the same commands.

**Database back to before a migration:** the old code may not work with the new tables. Restore the `.bak` file that
the deployment made just before it migrated, then put the old code back.

1. Take the API offline and list the newest backups:

   ```powershell
   Set-Content (Join-Path $WI.Root 'web\api\app_offline.htm') 'Restoring'; Start-Sleep -Seconds 5      # the API stops and lets go of the database
   Get-ChildItem $WI.Backup -Filter 'WabiInsightDB-*.bak' | Sort-Object LastWriteTime -Descending | Select-Object -First 5 Name, LastWriteTime
   ```

   Pick the newest file made **before** the deployment. The times in `deploy.log` are UTC; the file names and
   `LastWriteTime` use the server's local time.

2. Restore it (replace the file name):

   ```powershell
   $bak = Join-Path $WI.Backup 'WabiInsightDB-<yyyyMMdd-HHmm>.bak'          # CHANGE: the file you picked
   try {
       Invoke-WiSql "ALTER DATABASE [WabiInsightDB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;"
       Invoke-WiSql "RESTORE DATABASE [WabiInsightDB] FROM DISK = N'$bak' WITH REPLACE;"
   } finally {
       Invoke-WiSql "ALTER DATABASE [WabiInsightDB] SET MULTI_USER;"
   }
   Invoke-WiSql -Database WabiInsightDB "SELECT COUNT(*) AS Applied, MAX(MigrationId) AS Latest FROM dbo.__EFMigrationsHistory"   # the newest migration is the one from before
   ```

3. Put the old code back: the *instant* block above (it removes `app_offline.htm` itself), or **Run workflow** with
   **ref** and **migrate_database** off (the deployment removes `app_offline.htm` too).

**Verify:** `(Invoke-WebRequest "https://$($WI.Domain)/health/ready" -UseBasicParsing).Content` reports Healthy, and you
can sign in.

## 40. Restoring after a disaster

| Lost | Restore from |
|---|---|
| The database | Step 39, *Database back to before a migration*, with the newest `.bak` |
| Some files (reports, attachments, logos) | `robocopy "$($WI.Backup)\files" $WI.Files /E` copies back what's missing from last night's mirror, without touching newer files |
| The key ring | `Expand-Archive (Get-ChildItem $WI.Backup -Filter 'keys-*.zip' \| Sort-Object LastWriteTime \| Select-Object -Last 1).FullName -DestinationPath $WI.Root -Force` (the zip contains the `keys` folder), then the `keys` line of step 23, then `Restart-WebAppPool WabiInsightApi`. **Use the key ring from the same night as the database.** |
| The whole VPS | A new Windows Server 2019 VPS. Do steps 4–30, with the same SQL Server version as before (the restore puts the database files in the same folders). Copy your off-server backup folder into `D:\wabi-insight\backup` (or `C:\…`). Restore the database (step 39, item 2), then re-link its user to the new login: `Invoke-WiSql -Database WabiInsightDB "ALTER USER [wabiinsightdbowner] WITH LOGIN = [wabiinsightdbowner];"`. Unpack the key ring and copy the files back (rows above). Then step 31 with **migrate_database** on, and skip step 32 (the administrator is in the database). On **[GitHub] Settings → Actions → Runners**, remove the old, offline runner. If the IP changed, update DNS (step 3). |

## 41. Security checklist

- **Remote Desktop:** port 3389 open to your own IP only (step 2), and a long Administrator password.
- **SQL Server:**
  - Port 1433 is closed to the internet, in the provider panel (step 2) and in Windows (step 26).
  - The application uses `wabiinsightdbowner`, which owns `WabiInsightDB` only, not `sa`.
  - The `sa` password is in your password manager and nowhere else.
- **Secrets on the VPS:**
  - `appsettings.Local.json` holds the database password. Only Administrators, SYSTEM and the API pool can read it
    (steps 10 and 23).
  - Never e-mail it or put it in GitHub.
- **Backups:** the backup folder holds the database and the key ring that decrypts its secrets. Keep the off-server
  copy as private as the server.
- **Runner:**
  - The runner belongs to this private repository only, and deployments start only when someone with write access
    clicks **Run workflow**.
  - Never use the self-hosted runner for pull-request workflows; CI runs on GitHub's own computers.
- **Samples:** if you loaded them (step 34), change their passwords.
- **Platform Insights:** set up the dedicated login (step 35).
- **Updates:** keep Windows, the Hosting Bundle and SQL Server updated (step 38).

## 42. Costs (approximate, per month)

| Item | About |
|---|---|
| SQL Server 2025 Express | Free |
| TLS certificate (Let's Encrypt) | Free |
| GitHub Actions | About 10 build minutes per deployment, covered by the included minutes |
| Off-server copy of the backups | Your provider's backup service or a storage service: usually a few USD |
| Your VPS | Unchanged |

## 43. Troubleshooting

| What you see | Likely cause | Fix |
|---|---|---|
| `HTTP Error 500.19` | An IIS module is missing | `Get-WebGlobalModule -Name AspNetCoreModuleV2, RewriteModule, ApplicationRequestRouting`; install the missing one (steps 13–15). Run `net stop was /y; net start w3svc` after the Hosting Bundle. |
| `HTTP Error 500.30 / 500.31` (the API couldn't start) | Usually the connection string or a missing .NET 10 runtime | **Event Viewer → Windows Logs → Application**, source *IIS AspNetCore Module V2*: the error is there. For more detail, in `C:\wabitex-apps\wabi-insight\web\api\web.config` set `stdoutLogEnabled="true"` and `stdoutLogFile="C:\wabitex-apps\wabi-insight\logs\stdout"`, reload the site, read the newest log, then set it back to `false` (the next backend deployment also resets it). |
| `HTTP Error 500.50` on `/api/…` | The server variable of step 15 is missing | Step 15 **Do** (the last three lines) |
| `404` on `/api/…`, but the site loads | The ARR proxy is off, or the web app's `web.config` is missing | Step 15 **Verify** (`enabled`); `Select-String 'API reverse proxy' C:\wabitex-apps\wabi-insight\web\app\web.config` must find it, otherwise deploy `frontend` again |
| `502.3` after about 2 minutes on a study | ARR timeout | Step 15: timeout `00:10:00` |
| Everyone shares one login limit (`429`) | The API trusts `X-Forwarded-For` from 127.0.0.1 only | Keep the API site bound to `127.0.0.1:5080` (step 25) and `includePortInXForwardedFor` off (step 15) |
| *Login failed for user 'wabiinsightdbowner'* | Wrong password in `appsettings.Local.json`, or SQL Server still accepts Windows accounts only | Step 21 **Verify**; step 19 **Check** (`LoginMode` 2). A forgotten password: `Invoke-WiSql "ALTER LOGIN [wabiinsightdbowner] WITH PASSWORD = N'<new password>';"`, put the same password after `Password=` in `appsettings.Local.json` (`notepad (Join-Path $WI.Root 'web\api\appsettings.Local.json')`), then `Restart-WebAppPool WabiInsightApi` |
| *A network-related or instance-specific error* connecting to SQL | TCP off or not on port 1433 | Step 19 **Check** (`TcpPort`, `Test-NetConnection`), then **Do** |
| Signed out right after signing in | The site was opened with `http://` | Use `https://`; the session cookie is HTTPS-only |
| E-mails don't arrive | SMTP settings or outgoing port | `Invoke-WiSql -Database WabiInsightDB "SELECT TOP 10 Status, RecipientEmail, LastError, CreatedAt FROM dbo.EmailMessages ORDER BY CreatedAt DESC"` shows the error; check the SMTP port is allowed out (steps 2 and 33). Failed messages are retried. |
| Background e-mails stop when nobody uses the site | The API pool sleeps | Step 23 **Verify** (`AlwaysRunning`, idle timeout 0) and step 25 (`preloadEnabled`) |
| Stored connection strings fail after a restore | The key ring doesn't match the database | Restore the key ring from the same night as the database (step 40) |
| Database close to the Express limit | Usage and audit history | `Invoke-WiSql -Database WabiInsightDB "EXEC sp_spaceused"`; archive old usage events, or move to Express 2025 (50 GB) or Standard |
| A **Build** job fails | Tests or the build failed. Nothing reached the server. | Fix it in a pull request and run again |
| Deploy: *Check the server's deploy script* fails | The script isn't installed | Step 31, parts 1 and 2 |
| The deploy job warns the script *differs* | The repository has a newer deploy script | Step 31, parts 1 and 2 |
| Deploy: *appsettings.Local.json is missing* | Step 24 not done | Step 24 |
| Deploy: *Backup task finished with result …; nothing was migrated* | The backup failed (SQL rights, folder rights or a full disk). The previous API was put back. | Step 30 **Do** again, then its **Verify**. To see the error, run `powershell -NoProfile -ExecutionPolicy Bypass -File "$($WI.Backup)\backup.ps1"` |
| Deploy: *did not report Healthy* or *Migration failed* | The previous API was put back and is running | The job log shows the startup error (also in **Event Viewer**, source *IIS AspNetCore Module V2*). Migrations already applied stay applied: restore the backup if you must go back (step 39). |
| Deploy: *The release is installed, but …* | IIS, ARR or the certificate didn't answer correctly | Steps 15, 25 and 26 **Verify** |
| Deploy: *robocopy … failed* | A file was locked | Run again. If it persists, `Restart-WebAppPool WabiInsightApi` and run again. |
| Deploy waits forever on *Waiting for a runner* | The runner service is stopped | `Get-Service 'actions.runner.*' \| Start-Service` |
| Deploy: *Another deployment is running* | Two runs at once | Wait for the first one, then run again |
| `https://<domain>` works from your computer but times out on the VPS | Your provider doesn't route the VPS to its own public address | Check from your own computer instead. The deploy script isn't affected: it checks through `127.0.0.1`. |
| Certificate step: validation fails | DNS not pointing at the VPS yet, or port 80 closed | Steps 3 and 6; step 2 (port 80 from anywhere) |

---

## Appendix A — Installing without GitHub Actions (build on the server)

Use this only if you can't use the one-click deployments (for example, nobody can add a runner to the repository). The
server then needs the build tools, and you install every new version by hand.

**Order:** steps 1–26 and [step 30](#30-nightly-backups-database-key-ring-and-files), then A.1–A.4 here **instead of
steps 27–31**, then [steps 32](#32-create-the-first-platform-administrator-once) and [33](#33-go-live-checks). All of it in
**[VPS PowerShell]**, with step 5 loaded.

### A.1 Build tools: Git, .NET 10 SDK, Node.js 22

**Check:**

```powershell
Update-WiPath
if (Get-Command git -ErrorAction SilentlyContinue) { git --version } else { 'git not installed' }                  # git version 2.x.windows.x
if (Get-Command dotnet -ErrorAction SilentlyContinue) { dotnet --list-sdks | Select-String '^10\.' }               # 10.0.x [C:\Program Files\dotnet\sdk]
if (Get-Command node -ErrorAction SilentlyContinue) { node --version } else { 'node not installed' }               # v22.22.3 or later
```

**Do:** install what's missing. Git:

```powershell
$release = Invoke-RestMethod 'https://api.github.com/repos/git-for-windows/git/releases/latest' -Headers @{ 'User-Agent' = 'wabi-insight-setup' }
$asset = $release.assets | Where-Object name -match '^Git-[\d\.]+-64-bit\.exe$' | Select-Object -First 1
$git = Get-WiDownload $asset.browser_download_url $asset.name
$p = Start-Process $git -ArgumentList '/VERYSILENT', '/NORESTART', '/NOCANCEL', '/SP-' -Wait -PassThru; "exit code $($p.ExitCode)"
Update-WiPath
```

The .NET 10 SDK:

```powershell
$sdk = Get-WiDownload 'https://aka.ms/dotnet/10.0/dotnet-sdk-win-x64.exe' 'dotnet-sdk-10.0-win-x64.exe'
$p = Start-Process $sdk -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru; "exit code $($p.ExitCode)"
Update-WiPath
```

Node.js 22 (Angular 22 needs Node `^22.22.3`, `^24.15` or newer). This downloads the current Node 22 installer and
checks its SHA-256 against nodejs.org:

```powershell
. {
$sums = (Invoke-WebRequest 'https://nodejs.org/dist/latest-v22.x/SHASUMS256.txt' -UseBasicParsing).Content -split "`n"
$line = $sums | Where-Object { $_ -match '\snode-v22\.[\d\.]+-x64\.msi$' } | Select-Object -First 1
$hash, $name = $line.Trim() -split '\s+'
$msi = Get-WiDownload "https://nodejs.org/dist/latest-v22.x/$name" $name
if ((Get-FileHash $msi -Algorithm SHA256).Hash -ne $hash.ToUpper()) { throw "Checksum mismatch for $name" }
$p = Start-Process msiexec.exe -ArgumentList '/i', "`"$msi`"", '/qn', '/norestart' -Wait -PassThru; "exit code $($p.ExitCode)"
Update-WiPath
}
```

**Verify:** the **Check** block shows the three versions, and `npm --version` prints a version.

### A.2 Get the source code

**Check:** `if (Test-Path (Join-Path $WI.Root 'src\.git')) { git -C (Join-Path $WI.Root 'src') log -1 --oneline } else { 'no checkout yet' }`

**Do:** Git Credential Manager opens a sign-in window. Sign in to GitHub, or use your user name and a token with
*Contents: read* (step 31, part 1):

```powershell
git clone --branch main "https://github.com/$($WI.Repo).git" (Join-Path $WI.Root 'src')
```

**Verify:**

```powershell
git -C (Join-Path $WI.Root 'src') log -1 --oneline        # the commit you expect
Test-Path (Join-Path $WI.Root 'src\WabiInsight.slnx')      # True
```

### A.3 Build and install the API and the web app

**Check:** `Test-Path (Join-Path $WI.Root 'web\api\WabiInsight.Api.dll'), (Join-Path $WI.Root 'web\app\index.html')`
(True means built before; building again is safe).

**Do:** the API. `dotnet publish` writes a generic `web.config`; the repository's replaces it:

```powershell
$src = Join-Path $WI.Root 'src'; $api = Join-Path $WI.Root 'web\api'; $web = Join-Path $WI.Root 'web\app'
dotnet publish "$src\src\WabiInsight.Api\WabiInsight.Api.csproj" -c Release -o $api
Copy-Item "$src\deploy\iis\web.config" (Join-Path $api 'web.config') -Force
```

The published `appsettings.json` holds the committed configuration (database connection string, SMTP, AI key: owner
decision G). The publish never includes `appsettings.Local.json` or `appsettings.Development.json`, and never touches
the server's own `appsettings.Local.json`.

Then the web app:

```powershell
Push-Location "$src\src\web"
npm ci
npm run build -- --configuration production
Pop-Location
robocopy "$src\src\web\dist\wabi-insight-web\browser" $web /MIR /XF web.config /NFL /NDL /NJH /NJS | Out-Null
Copy-Item "$src\deploy\iis\web.spa.config" (Join-Path $web 'web.config') -Force
```

**Verify:**

```powershell
Get-ChildItem $api -Filter 'appsettings*.json' | Select-Object Name                  # appsettings.json and appsettings.Local.json
Select-String -Path (Join-Path $api 'web.config') -Pattern 'AspNetCoreModuleV2|maxAllowedContentLength' | Select-Object -ExpandProperty Line
Test-Path (Join-Path $web 'index.html'), (Join-Path $web 'web.config')                 # True True
Select-String -Path (Join-Path $web 'web.config') -Pattern 'API reverse proxy' -Quiet   # True
```

### A.4 Create the tables (migrations)

**Check:** `0  none` means no tables yet:

```powershell
Invoke-WiSql -Database WabiInsightDB "IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NULL SELECT 0 AS Applied, N'none' AS Latest ELSE SELECT COUNT(*) AS Applied, MAX(MigrationId) AS Latest FROM dbo.__EFMigrationsHistory"
```

**Do:**

```powershell
Push-Location (Join-Path $WI.Root 'web\api')
$env:ASPNETCORE_ENVIRONMENT = 'Production'
dotnet .\WabiInsight.Api.dll migrate
Pop-Location
```

**Verify:** run the **Do** block again. It must print `Configuration database is up to date.` The **Check** query now
shows the number of migrations and the newest one. Then run the **Verify** block of step 31 (from the `health/ready`
lines on) and the **Verify** of step 30, and continue with step 32.

### A.5 Updating to a new version by hand

```powershell
$WI = Import-Clixml C:\wabitex-apps\wabi-insight\wabi-insight.vars.xml     # plus the session settings and helpers from step 5
$src = Join-Path $WI.Root 'src'; $api = Join-Path $WI.Root 'web\api'; $web = Join-Path $WI.Root 'web\app'
git -C $src fetch origin main; git -C $src checkout main; git -C $src pull --ff-only; git -C $src log -1 --oneline
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

**Verify:** `(Invoke-WebRequest "https://$($WI.Domain)/health/ready" -UseBasicParsing).Content` reports Healthy. Sign
in, and **System Health** is green.

To switch to one-click deployments later, do steps 27–31 (in step 31, leave **migrate_database** off if nothing new
needs migrating; on is harmless). The `src` folder is then no longer used.

## Appendix B — Moving an existing installation from `C:\WabiInsight`

Earlier versions of this guide used `C:\WabiInsight\{src,api,web,data\storage,data\keys,backup}`. To move such a server
to this layout, open an administrator PowerShell with the step 5 session settings, values (the new ones) and helpers
loaded.

**Check:** `Test-Path C:\WabiInsight\api\WabiInsight.Api.dll` (False means nothing to move).

**Do:** first create the folders (step 10 **Check** and **Do**). Then stop the sites and copy everything across:

```powershell
. {
Import-Module WebAdministration
$old = 'C:\WabiInsight'
Stop-Website 'WabiInsight'; Stop-Website 'WabiInsightApi'; Stop-WebAppPool 'WabiInsightApi'
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
$settings | ConvertTo-Json -Depth 6 | Set-Content $localPath -Encoding UTF8
# IIS sites point at the new folders
Set-ItemProperty 'IIS:\Sites\WabiInsightApi' -Name physicalPath -Value (Join-Path $WI.Root 'web\api')
Set-ItemProperty 'IIS:\Sites\WabiInsight' -Name physicalPath -Value (Join-Path $WI.Root 'web\app')
}
```

Then:
- apply the permissions of **step 23** (the `icacls` lines);
- register the backup task again with the new paths (**step 30**; same task name, it replaces the old one);
- with one-click deployments, install the deploy script (**step 31**, parts 1 and 2).

Finally, start everything again:

```powershell
Start-WebAppPool 'WabiInsightApi'; Start-Website 'WabiInsightApi'; Start-Website 'WabiInsight'
```

**Verify:**

```powershell
(Get-Website 'WabiInsightApi').physicalPath; (Get-Website 'WabiInsight').physicalPath      # …\web\api and …\web\app
(Invoke-WebRequest "https://$($WI.Domain)/health/ready" -UseBasicParsing).Content         # Healthy
Get-ChildItem $WI.Files | Select-Object Name                                               # analysis-reports, … (your existing files)
```

Open an earlier analysis in **My History** and download its report. This proves the files and the key ring were found.
When everything works, delete `C:\WabiInsight`.
