# Wabi Insight on a Windows Server 2019 VPS with SQL Server Express — the complete beginner's guide (deployment, CI/CD, backups, demo organizations)

This guide assumes **little or no server knowledge**. It takes you from a fresh **Windows Server 2019** VPS to a live
Wabi Insight at `https://<your domain>` that you open from your PC's browser, with **everything on one server**:

- the **web app and the API** in IIS (Microsoft's web server, part of Windows);
- **all the databases** in **SQL Server 2025 Express** (free):
  - `WabiInsightDB`, the platform's own database (organizations, users, permissions, history, alerts…);
  - your **organizations' databases**, when a customer's data is copied to this server (step 44);
  - the **demo organizations' databases**: Gulf Aviation Services (3 databases) and Wabi Bank (5 databases);
- **every file** the application keeps (reports, exports, attachments, logos) on the VPS disk;
- a **nightly backup** of every database, the logins, the encryption keys and the files;
- **one-click deployments** from GitHub (**Actions → Deploy (Windows)**). GitHub builds the code, and a small agent on
  your VPS installs it. The VPS needs **no Git, .NET SDK or Node.js**.

```
 Users' browsers (your PC, phones…)
      │ HTTPS (443)
      ▼
 ┌──────────── Your VPS: Windows Server 2019 ──────────────────────────────────────────────────┐
 │ IIS site "WabiInsight"   ── /            → the web app   (C:\wabitex-apps\wabi-insight\web\app)
 │   (URL Rewrite + ARR)    ── /api, /health → IIS site "WabiInsightApi" on 127.0.0.1:5080
 │                                             (C:\wabitex-apps\wabi-insight\web\api)
 │                                                  │ SQL on localhost, port 1433 or 14333 (never open to the internet)
 │                                                  ▼
 │ SQL Server 2025 Express (instance SQLEXPRESS, or SQLEXPRESS2025 next to an existing SQL Server: step 16)
 │   WabiInsightDB · organizations' databases · demo databases (FlightOperationsDB, … WabiBankITDB)
 │ Files:    D:\wabi-insight\files\<kind>          Key ring: C:\wabitex-apps\wabi-insight\keys
 │ Nightly task "Wabi Insight backup" → D:\wabi-insight\backup\<date-time>\ (every database + logins + key ring)
 │                                      and D:\wabi-insight\backup\files (a mirror of the files)
 │ GitHub Actions runner (service) ← receives the deployments you start on GitHub
 └──────────────┬──────────────────────────────┬──────────────────────────┬──────────────────┘
                │ outgoing HTTPS only          │ HTTPS (443)              │ SMTP (587 or 465)
                ▼                              ▼                          ▼
   GitHub (builds each version)        The AI provider            Your e-mail server
```

Without a second disk (`D:`), the files and the backups go to `C:\wabi-insight` instead. Step 5 chooses this for you.

**Time:** about 4 hours the first time (much of it waiting for installers and builds).
**Extra monthly cost** on top of your VPS: none is required (see *Costs*, step 48).

**Other guides:** [DEPLOYMENT_WINDOWS_AZURE.md](DEPLOYMENT_WINDOWS_AZURE.md) is the same server with the **files and the
backup copies in Azure Blob Storage** (UAE North); [DEPLOYMENT_LINUX_DOCKER.md](DEPLOYMENT_LINUX_DOCKER.md) runs
everything in containers on Ubuntu; [DEVOPS_WINDOWS.md](DEVOPS_WINDOWS.md) explains the deployment pipeline; every
setting is in [DEPLOYMENT.md](DEPLOYMENT.md). Use **one** guide for a server, not several.

---

## How to read this guide

- **Where a step happens** is shown at its start:
  - **[Your computer]**: your own Windows or Mac computer: its browser, or a terminal.
  - **[Provider panel]**: your VPS provider's control panel, in your browser.
  - **[Domain registrar]**: the website where you manage your domain's DNS.
  - **[GitHub]**: the GitHub website, signed in with an account that has **admin** rights on `mtawfig/wabi-data-harbor`.
  - **[VPS PowerShell]**: the administrator PowerShell window on the VPS (step 5).
  - **[VPS browser]**: a browser on the VPS itself (rarely needed).
- Each step is written the same way:
  - **Why:** what the step is for.
  - **Check:** tells you whether the step is already done. If it is, skip **Do**.
  - **Do:** the actions or commands.
  - **You should see:** what happens when it works.
  - **If not:** the usual causes and what to do.
  - **Verify:** proves the step worked. **Don't continue until it does.**
- **Copy and paste** commands exactly. In PowerShell, paste with a **right-click** (or `Ctrl + V`), a whole box at once,
  then press **Enter**. Copying from the VPS to your computer works too: select the text and press `Ctrl + C`.
- **Boxes that start with `. {` and end with `}`** ask you for a password or must stop at the first error. Paste the
  whole box, **including those two lines**: nothing runs until the closing `}`, then PowerShell asks its questions one at
  a time. If the prompt shows `>>` after pasting, press **Enter** once more. If such a box stops with red text, nothing
  after the error has run: fix the cause and paste the whole box again.
- Lines starting with `#` are comments and are ignored. A comment at the end of a line, such as `# True`, shows the
  **output you should see**.
- When PowerShell asks for a password or token (`Read-WiSecret`), it shows `*` for each character. Paste it with a
  right-click and press **Enter**.
- Text in `<angle brackets>` is a placeholder. Replace it with your own value, **without** the brackets.
- **Red text in PowerShell means an error.** Stop, read it, and look it up in *Troubleshooting* (step 49).
- **Never paste a password, key or token into a chat, an e-mail or GitHub.** Keep each one in a password manager
  (Bitwarden, 1Password, KeePass…): an app on **your own computer or phone**, not on the server, that keeps your
  secrets in one encrypted vault. When this guide says *store it in your password manager*, create an entry there and
  paste the value in.

### Windows basics you'll use

| You do | What it does |
|---|---|
| `Win + R`, type a name, **Enter** | Starts a program by name (for example `mstsc` for Remote Desktop, `notepad`). |
| **Start** → type `PowerShell` → right-click **Windows PowerShell** → **Run as administrator** | Opens PowerShell with full rights. The window title starts with *Administrator:*. |
| `↑` (arrow up) in PowerShell | Brings back your previous command. |
| `Ctrl + C` in PowerShell with nothing selected | Stops the running command. |
| `cls` | Clears the PowerShell window. |
| `notepad <file>` | Opens a file in Notepad. **File → Save** (`Ctrl + S`) saves it. |
| **File Explorer** (`Win + E`) | Browse folders. Type a folder such as `C:\wabitex-apps\wabi-insight` in its address bar. |

Windows writes folders with `\` (for example `C:\wabitex-apps\wabi-insight`). Names are **not** case-sensitive.

### Words you'll meet

| Word | Meaning |
|---|---|
| VPS | A virtual server you rent from a hosting company. Yours runs Windows Server 2019. |
| RDP / Remote Desktop | The program you use to see and control the VPS screen from your computer. |
| PowerShell (as administrator) | Windows' command window with full rights. Almost everything on the VPS is done there. |
| IIS | Internet Information Services, the web server built into Windows. |
| Application pool | The IIS process that runs a site. |
| URL Rewrite, ARR | Two IIS add-ons. Together they pass the requests for `/api` and `/health` from the public site to the API site (a *reverse proxy*). |
| SQL Server Express | Microsoft's free edition of SQL Server, the database engine. It runs on your VPS. |
| Instance | One installed copy of SQL Server. Express installs as the instance `SQLEXPRESS`; its Windows service is `MSSQL$SQLEXPRESS`. |
| `sa` | SQL Server's built-in administrator login. You keep its password for emergencies only. |
| SQL login | A user name and password that SQL Server checks itself. The application signs in with `wabiinsightdbowner`. |
| Mixed mode | SQL Server accepts both Windows accounts and SQL logins. The application needs it. |
| Connection string | One line of text that says where a database is and how to sign in. It contains a password: treat it as a secret. |
| Migration | A change to the database tables that comes with a new version. A deployment applies it when you tick **migrate_database**. |
| Key ring | Files that encrypt the secrets the application stores in the database (customers' connection strings, provider keys). **If you lose them, those secrets can't be read.** Backed up every night. |
| `.bak` file | A complete backup of one SQL Server database, in one file. |
| Scheduled task | A job that Windows runs at set times (Task Scheduler). Here: the nightly backup. |
| DNS A record | The setting at your domain registrar that points `insight.example.com` to your VPS IP address. |
| TLS certificate | What makes the padlock and `https://` work. Here it's free, from Let's Encrypt, and renews itself. |
| CI / CD | CI: GitHub builds and tests every change automatically (already active). CD: installing a version on your server, here with one click. |
| Self-hosted runner | A small GitHub agent installed on your VPS as a Windows service. It receives the deployments you start. |
| Fine-grained token | A short-lived GitHub password for a script, limited to one repository and to reading. |

## Your information sheet

Fill this in as you go. Keep the secret rows in your password manager, not in a text file.

| Item | Example | Secret? |
|---|---|---|
| VPS public IP address | `203.0.113.10` | no |
| VPS Administrator user and password | `Administrator` / … | **yes** |
| Domain for the site | `insight.example.com` | no |
| E-mail for certificate notices | `it@example.com` | no |
| Folder for the files and backups | `D:\wabi-insight` (or `C:\wabi-insight`) | no |
| SQL Server `sa` password (step 7) | … | **yes** |
| Password of the SQL login `wabiinsightdbowner` (step 7) | … | **yes** |
| Runner registration token (step 29; valid one hour) | … | **yes** |
| GitHub fine-grained tokens (steps 31 and 35; deleted after use) | `github_pat_…` | **yes** |
| First Platform Administrator e-mail and password (step 32) | `support@wabitex.com` / … | **yes** |

---

# Part A — Before you start

## 1. What you need

**Why:** to have everything at hand before you start.

| You need | Where to get it | Notes |
|---|---|---|
| The VPS: **Windows Server 2019 with Desktop Experience**, 64-bit | Your hosting provider's control panel shows its IP address and the Administrator password | **4 vCPU / 8 GB RAM recommended** (SQL Server and IIS share it; 4 GB works for testing). At least **60 GB** disk: the demo databases take about 3 GB and their backups as much again, plus the files. A second disk (`D:`) for the files and backups is welcome, not required. |
| A domain name and access to its DNS settings | Your domain registrar (GoDaddy, Namecheap, Cloudflare…) | For `https://` and for the links in e-mails |
| A GitHub account with **admin** rights on `mtawfig/wabi-data-harbor` | <https://github.com> | Needed to add the runner and the environment |
| A Remote Desktop app on your computer | Windows: *Remote Desktop Connection* (`mstsc`) is built in. Mac: *Windows App* (Microsoft Remote Desktop) from the App Store. | |
| A password manager | Bitwarden, 1Password, KeePass… | You'll create several passwords |

The SMTP account (for e-mails) and the AI key are already in the repository's `appsettings.json` (owner decision G).
**You don't enter them.** This server gets its own database, which step 24 writes into
`appsettings.Local.json`: that file overrides `appsettings.json` on this server only.

**Is SQL Server Express enough?** Yes. Wabi Insight runs on Express 2019, 2022 and 2025 (the full integration test suite
passes on each). Express 2025 allows **50 GB per database**, 1,410 MB of memory for its cache and 4 cores. It has no SQL
Server Agent, so the nightly backup is a Windows scheduled task (step 30).

**Verify:** you have the VPS IP address and Administrator password, and you can sign in to GitHub.

## 2. Open the VPS ports in your provider's control panel

**Where:** [Provider panel].

**Why:** many providers have a firewall in their panel, outside Windows. It must let the website and your Remote
Desktop in, and nothing else.

**Do:** make sure it allows these **incoming** connections:

| Port | Why | From |
|---|---|---|
| 80 (HTTP) | Certificate validation by Let's Encrypt, and the redirect to HTTPS | Anywhere |
| 443 (HTTPS) | The website | Anywhere |
| 3389 (Remote Desktop) | Your access | **Only your own IP address**, if the panel allows it (strongly recommended) |

**Don't open** ports 1433 or 14333 (SQL Server) or 5080 (the API). Only programs on the VPS itself use them.

Outgoing connections must be allowed (they usually are by default):
- HTTPS (443) to GitHub: `github.com`, `api.github.com`, `codeload.github.com`, `objects.githubusercontent.com`,
  `*.actions.githubusercontent.com`, and `*.blob.core.windows.net` (GitHub keeps the built versions there);
- HTTPS (443) to Microsoft's download sites and the PowerShell Gallery (installers), and to Let's Encrypt;
- HTTPS (443) to the AI provider, `api.anthropic.com`;
- your SMTP server, on port 587 or 465.

> If your own IP address changes (home internet often does), the 3389 rule shuts you out until you update it. Find the
> **web console** in the provider's panel now (called *Console*, *VNC* or *KVM*): it works even when Remote Desktop
> doesn't.

**Verify:** the panel lists 80, 443 and 3389 as allowed (or the provider has no panel firewall).

## 3. Point your domain at the VPS (DNS)

**Where:** [Domain registrar].

**Why:** browsers find your server through its name, and Let's Encrypt only gives a certificate for a name that points
at the server asking for it.

**Do:**
1. Open the DNS settings of your domain.
2. Add a record of type **A**:
   - **Name/Host:** `insight` (for `insight.example.com`; `@` for the bare domain);
   - **Value/Points to:** your VPS IP address;
   - **TTL:** 300 (5 minutes) if you can choose: a later move to another server is then quicker.
3. Save.

**You should see:** the new record in the list. Changes take from a few minutes to a few hours to work everywhere.

**Verify:** [Your computer] open a terminal (Windows: **Start** → type `cmd` → **Command Prompt**; Mac: **Terminal**)
and type `nslookup insight.example.com` (your domain): it shows your VPS IP address. **If not:** wait 15 minutes; check
the record's name for typos. You can continue meanwhile: the address is needed from step 26.

---

# Part B — Connect to the VPS and prepare PowerShell

## 4. Connect with Remote Desktop

**Where:** [Your computer].

**Do:**
- **Windows:** press `Win + R`, type `mstsc`, press **Enter**. In *Computer*, enter the VPS IP address → **Connect**.
  User `Administrator` and the password from your provider → **OK**. Accept the certificate warning (**Yes**).
- **Mac:** open *Windows App* → **+** → *Add PC* → *PC name*: the IP address → **Add**, then double-click it and sign
  in.

**You should see:** the Windows Server desktop. *Server Manager* may open by itself: you can close it.

**If not:** *Remote Desktop can't connect* → port 3389 closed in the provider panel (step 2), or the wrong IP. *Your
credentials did not work* → check the user (`Administrator`) and the password from the provider's e-mail.

**Recommended now:** change the Administrator password to a long, unique one: on the VPS press `Ctrl + Alt + End` →
*Change a password*. Store it in your password manager.

Copy and paste work between your computer and the VPS. You can do the **[GitHub]** steps in the
browser on your own computer and paste the results into the VPS.

## 5. Open PowerShell as administrator, and set your values

**Where:** the VPS. Click **Start**, type `PowerShell`, right-click **Windows PowerShell** → **Run as administrator** →
**Yes**. Use this window for every **[VPS PowerShell]** step.

**Why:** the following steps use a few values (your domain, the folders, the SQL Server instance) and helper commands.
You set them once; they're saved to a file, so a new window loads them back in one line.

**Check:**

```powershell
$PSVersionTable.PSVersion.ToString()                                                   # 5.1.x
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)   # True
```

`False` on the second line means the window isn't *as administrator*: close it and open it again as described above.

**Do, part 1 — session settings.** **Paste these again every time you open a new PowerShell window:**

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'      # makes downloads much faster
$ErrorActionPreference = 'Stop'
```

**Do, part 2 — your values.** First copy the box into Notepad on your computer, **edit the lines marked `# CHANGE`**,
then paste it into PowerShell:

```powershell
$dataDisk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='D:' AND DriveType=3"    # a local disk D:, not a DVD drive
$dataRoot = if ($dataDisk) { 'D:\wabi-insight' } else { 'C:\wabi-insight' }
$WI = [ordered]@{
    Domain         = 'insight.example.com'           # CHANGE: the domain from step 3
    CertEmail      = 'it@example.com'                # CHANGE: receives certificate expiry notices
    AdminEmail     = 'support@wabitex.com'           # CHANGE: first Platform Administrator (a real mailbox)
    Repo           = 'mtawfig/wabi-data-harbor'
    Root           = 'C:\wabitex-apps\wabi-insight'  # the application: web\app, web\api, keys, logs, releases, deploy
    Files          = "$dataRoot\files"               # generated and uploaded files, one folder per kind
    Backup         = "$dataRoot\backup"              # nightly backups (step 30)
    SqlInstance    = 'SQLEXPRESS'                    # CHANGE to 'SQLEXPRESS2025' if this server already runs SQL Server for other applications (step 16)
    SqlPort        = 1433                            # CHANGE to 14333 in that case
    BackupDatabases = @()                            # empty = back up every database of that SQL Server; list names only when it is shared with other applications (step 16)
    RunnerDir      = 'C:\actions-runner'             # CHANGE to 'C:\actions-runner-wabi-insight' if this server already has a GitHub runner for another project (step 29)
}
New-Item -ItemType Directory -Force -Path $WI.Root | Out-Null
$WI | Export-Clixml (Join-Path $WI.Root 'wabi-insight.vars.xml')
```

**Already have SQL Server on this VPS for other applications** (for example SQL Server 2019 Express used by other web
sites)? Then set `SqlInstance = 'SQLEXPRESS2025'` and `SqlPort = 14333`: Wabi Insight gets its own SQL Server 2025
instance on its own port, and the existing one is **not touched** (step 16 explains). If you're not sure, run step 16's
**Check** first, then come back here.

To change a value later, edit it and paste the box again: the saved copy is updated. To change **one** value without
retyping the others (for example a mistyped name):

```powershell
$WI = Import-Clixml C:\wabitex-apps\wabi-insight\wabi-insight.vars.xml
$WI.Domain = 'insight.example.com'            # the value to correct
$WI | Export-Clixml C:\wabitex-apps\wabi-insight\wabi-insight.vars.xml
``` **In a new window**, load the
values back with:

```powershell
$WI = Import-Clixml C:\wabitex-apps\wabi-insight\wabi-insight.vars.xml
```

**Do, part 3 — helper commands.** **Paste them into every new window too:**

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
    # Signs in with your Windows administrator account (a SQL Server administrator), or with -Credential.
    [CmdletBinding()]
    # -Tables shows every result of a script as its own table (the sample scripts return several).
    param([string]$Query, [string]$InputFile, [string]$Database = 'master', [pscredential]$Credential, [switch]$Tables)
    Import-Module SqlServer -Verbose:$false
    $p = @{ ServerInstance = "localhost,$($WI.SqlPort)"; Database = $Database; TrustServerCertificate = $true; QueryTimeout = 1800; DisableVariables = $true; ErrorAction = 'Stop' }
    if ($InputFile) { $p.InputFile = $InputFile } else { $p.Query = $Query }
    if ($Credential) { $p.Credential = $Credential }
    if ($Tables) { Invoke-Sqlcmd @p -OutputAs DataTables | ForEach-Object { $_ | Format-Table -AutoSize | Out-String -Width 250 } }
    else { Invoke-Sqlcmd @p }
}
function Get-WiRepoFile([string]$RepoPath, [string]$OutFile, [string]$Token) {
    # Downloads one file of the private repository with a fine-grained token (Contents: read).
    Invoke-WebRequest -UseBasicParsing -OutFile $OutFile -Uri "https://api.github.com/repos/$($WI.Repo)/contents/$($RepoPath)?ref=main" `
        -Headers @{ Authorization = "Bearer $Token"; Accept = 'application/vnd.github.raw'; 'User-Agent' = 'wabi-insight-setup' }
}
```

**You should see:** nothing (no red text). Defining functions prints nothing.

**Verify:**

```powershell
$WI                                                                      # your values
Get-Command New-WiSecret, Read-WiSecret, Invoke-WiSql, Get-WiRepoFile | Select-Object Name   # four names
```

> **Every time you open a new PowerShell window** (after a restart, for example), paste part 1, the `Import-Clixml` line
> and part 3 again. The steps below assume they're loaded.

## 6. Check the server, its disks and its public IP address

**Where:** [VPS PowerShell].

**Why:** to confirm the server matches what you ordered before installing anything.

**Do and verify:**

```powershell
(Get-CimInstance Win32_OperatingSystem) | Select-Object Caption, Version, OSArchitecture   # Windows Server 2019 …, 10.0.17763, 64-bit
Get-CimInstance Win32_ComputerSystem | Select-Object NumberOfLogicalProcessors, @{n='RAM_GB';e={[math]::Round($_.TotalPhysicalMemory/1GB,1)}}   # 4 and about 8
Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | Select-Object DeviceID, @{n='Free_GB';e={[math]::Round($_.FreeSpace/1GB,1)}}   # C: with at least 40 GB free (and D:, if you have one)
$WI.Backup                                                                               # D:\wabi-insight\backup, or C:\wabi-insight\backup without a D: disk
Invoke-RestMethod -Uri 'https://api.ipify.org' -UseBasicParsing                          # the VPS public IP: write it on your sheet
(Resolve-DnsName $WI.Domain -Type A | Where-Object Type -eq 'A' | Select-Object -First 1).IPAddress   # must be the same IP (step 3)
```

**If not:**
- The last line shows an error or another address → DNS isn't ready yet. Continue; it's needed from step 26.
- Your provider added a second disk without a drive letter → run `diskmgmt.msc`, bring the disk **Online**,
  **Initialize** it, create a **New Simple Volume** with the letter `D`, then paste the values box of step 5 again. If a
  DVD drive already uses `D:`, first give it another letter: right-click it → **Change Drive Letter and Paths**.

## 7. Two passwords for SQL Server

**Where:** [VPS PowerShell].

**Why:** SQL Server needs an administrator password (`sa`), and the application gets its own login with its own
password.

**Do:** generate them and **store both in your password manager** right away:

```powershell
"sa password (SQL Server's own administrator):              $(New-WiSecret)"
"Password of wabiinsightdbowner (the application's login):  $(New-WiSecret)"
```

**You should see:** two lines, each with a password like `Wi-…-7`. Select each password with the mouse, `Ctrl + C`, and
save it. Then `cls` to clear the screen.

- **`sa`** is SQL Server's built-in administrator. Setup needs a password for it (step 18). Neither you nor the
  application uses it day to day: you sign in to SQL Server with your Windows administrator account.
- **`wabiinsightdbowner`** is the SQL login the application uses. It owns the database `WabiInsightDB` and nothing else
  (step 21). Its password goes into `appsettings.Local.json` (step 24).

They contain only letters, digits and `-`, which meets SQL Server's rules and is safe inside connection strings. Don't use
`;`, `'` or `"` in these passwords.

These two lines only **create and show** two random passwords. They change nothing on the server and save nothing:
you copy them into your password manager, and they take effect when you paste them in steps 18, 21 and
24. Running the lines again shows two **different** passwords: keep one pair and use it throughout.

**Your own passwords instead?** Skip the two lines and store your own in the password manager. Each needs at least 12
characters with three of: upper case, lower case, digits, symbols (SQL Server refuses weaker ones), and must not
contain `;`, `'` or `"`.

**Verify:** both passwords are in your password manager.

---

# Part C — Install the software on the VPS

Run every step in **[VPS PowerShell]**, with the session settings, `$WI` and the helpers from step 5 loaded.

## 8. Windows updates

**Why:** security fixes, and some installers below need a recent Windows.

**Check:**

```powershell
Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 3 HotFixID, InstalledOn   # a recent cumulative update
$pending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
           (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
"Reboot pending: $pending"
```

**Do:**
1. **Start → Settings → Update & Security → Windows Update → Check for updates**, then **Install**. (Or type `sconfig`
   in PowerShell and choose option **6**.) On a fresh VPS this can take 30–60 minutes.
2. Restart when asked: **Start → Power → Restart**. Remote Desktop disconnects.
3. Wait two minutes, reconnect (step 4), open PowerShell as administrator, and paste the session settings, the
   `Import-Clixml` line and the helpers again (step 5).

**Verify:** the **Check** box shows an update from the last month, and `Reboot pending: False`.

**If not:** updates fail with an error code → run **Check for updates** again (it often succeeds the second time).

## 9. Time zone and clock

**Why:** a correct clock matters for certificates and sign-in. The application stores times in UTC and shows each
organization its own time zone, so the server's zone only affects the backup file names and the 02:30 schedule.

**Check:**

```powershell
Get-TimeZone | Select-Object Id
w32tm /query /status | Select-String 'Source|Last Successful'
```

**Do** (only if needed):

```powershell
Set-TimeZone -Id 'Arabian Standard Time'      # UAE time
w32tm /resync
```

**A VPS outside the UAE?** The time zone is your choice: it only decides when 02:30 is for the nightly backup and how
the backup folders are named. Keep the server's zone, or set UAE time anyway; in both cases run `w32tm /resync`.

**Verify:** `Get-TimeZone` shows the zone you chose (for example `Arabian Standard Time`), and `w32tm /query /status` shows a recent `Last Successful Sync
Time`. **If** `w32tm /resync` says *The computer did not resync*: run `net start w32time` and try again.

## 10. Folders

**Why:** the application, its keys, logs, files and backups each get a fixed place, readable only by administrators.

| Folder | Contents |
|---|---|
| `C:\wabitex-apps\wabi-insight\web\app` | The web app (IIS site `WabiInsight`) |
| `C:\wabitex-apps\wabi-insight\web\api` | The API (IIS site `WabiInsightApi`) and `appsettings.Local.json` (this server's settings) |
| `C:\wabitex-apps\wabi-insight\keys` | The key ring. **Losing it makes every stored connection string and provider key unreadable.** Backed up every night (step 30). |
| `C:\wabitex-apps\wabi-insight\logs` | The deploy log `deploy.log` and the API's own log, one file a day: `wabi-insight-<yyyyMMdd>.log` (UTC; kept 30 days) |
| `C:\wabitex-apps\wabi-insight\releases` | The last 5 releases, plus `previous-api` and `previous-web` for rolling back |
| `C:\wabitex-apps\wabi-insight\deploy` | The deploy script the runner runs (step 31), and the backup copy settings (step 30) |
| `C:\wabitex-apps\wabi-insight\install`, `tools` | Downloaded installers and scripts; the certificate tool (win-acme) |
| `D:\wabi-insight\files\<kind>` | Generated and uploaded files (the setting `Storage:RootPath`), one folder per kind. Inside each: `yyyy\MM\<random>.bin`. |
| `D:\wabi-insight\backup` | Nightly backups, one folder per night, and a mirror of the files (step 30) |

The API keeps every file under `Storage:RootPath`, in the folder of its kind:

| Folder under `D:\wabi-insight\files` | What is stored there |
|---|---|
| `analysis-reports` | Study PDFs generated by Ask Wabi Insight |
| `analysis-results` | Stored result sets of every answer (reopening history never re-queries the source) |
| `analysis-exports` | PDF, Word and Excel exports from My History and My Reports |
| `report-deliveries` | Files of the periodic reports sent to users |
| `organization-logos` | Uploaded organization logos |
| `support-attachments` | Files attached to support tickets |

**Check:**

```powershell
$appFolders = 'web\app', 'web\api', 'keys', 'logs', 'releases', 'deploy', 'install', 'tools'
$fileFolders = 'analysis-reports', 'analysis-results', 'analysis-exports', 'report-deliveries', 'organization-logos', 'support-attachments'
$appFolders  | ForEach-Object { '{0,-48} {1}' -f (Join-Path $WI.Root $_), (Test-Path (Join-Path $WI.Root $_)) }
$fileFolders | ForEach-Object { '{0,-48} {1}' -f (Join-Path $WI.Files $_), (Test-Path (Join-Path $WI.Files $_)) }
'{0,-48} {1}' -f $WI.Backup, (Test-Path $WI.Backup)
```

**Do:** create them, and remove the general `Users` access on both roots (the application folder will hold secrets, and
a `D:` drive grants `Users` access by default):

```powershell
$appFolders | ForEach-Object { New-Item -ItemType Directory -Force -Path (Join-Path $WI.Root $_) | Out-Null }
$fileFolders | ForEach-Object { New-Item -ItemType Directory -Force -Path (Join-Path $WI.Files $_) | Out-Null }
New-Item -ItemType Directory -Force -Path $WI.Backup | Out-Null
$dataRoot = Split-Path $WI.Backup -Parent                                 # D:\wabi-insight (or C:\wabi-insight)
foreach ($folder in $WI.Root, $dataRoot) {
    icacls $folder /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' 'BUILTIN\Administrators:(OI)(CI)F' | Out-Null
}
```

**Verify:**

```powershell
icacls $WI.Root                          # only NT AUTHORITY\SYSTEM and BUILTIN\Administrators
icacls (Split-Path $WI.Backup -Parent)   # only NT AUTHORITY\SYSTEM and BUILTIN\Administrators
Get-ChildItem $WI.Files | Select-Object Name   # the six folders above
```

## 11. IIS (the web server)

**Why:** IIS serves the web app and runs the API.

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

**You should see:** a progress bar, then a table with `Success: True` (2–5 minutes).

**Verify:**

```powershell
(Get-WindowsFeature $iisFeatures | Where-Object InstallState -ne 'Installed').Count     # 0
Get-Service W3SVC | Select-Object Status, StartType                                      # Running, Automatic
```

## 12. Microsoft Visual C++ runtime (x64)

**Why:** PDF reports and charts use native libraries that need it. Without it, the first report fails with
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

**Why:** it installs the .NET 10 runtime and the IIS module that runs the API. It must come **after** IIS (step 11).
The deploy script stops if it's missing.

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

**You should see:** `exit code 0` (or `3010`), then *The World Wide Web Publishing Service was started successfully*.

**Verify:**

```powershell
Get-WebGlobalModule -Name AspNetCoreModuleV2 | Select-Object Name, Image
dotnet --list-runtimes | Select-String 'Microsoft.AspNetCore.App 10\.'          # Microsoft.AspNetCore.App 10.0.x
```

A server that already runs other .NET sites has `dotnet` and the IIS module from an older version (8 or 9). Wabi
Insight needs **version 10**: the `10.0.x` line above must be there. Installing it does not remove or change the older
versions; the two `net` lines restart IIS, so every site on the server is offline for a few seconds.

**If not:** `dotnet` is *not recognized* → close PowerShell, open a new one as administrator, load step 5 again, and run
**Verify** again.

## 14. IIS URL Rewrite 2.1

**Why:** the public site uses it to pass `/api` to the API and to redirect `http://` to `https://`.

**Check:** `Get-WebGlobalModule -Name RewriteModule -ErrorAction SilentlyContinue`

**Do:**

```powershell
$rw = Get-WiDownload 'https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi' 'rewrite_amd64_en-US.msi'
$p = Start-Process msiexec.exe -ArgumentList '/i', "`"$rw`"", '/qn', '/norestart' -Wait -PassThru; "exit code $($p.ExitCode)"
```

**Verify:** `Get-WebGlobalModule -Name RewriteModule` returns `RewriteModule …\rewrite.dll`.

## 15. Application Request Routing 3.0 (passes `/api` to the API)

**Why:** ARR does the actual passing of requests to the API. Its timeout is raised to 10 minutes because a study report
with a PDF takes 1–3 minutes (the default is 2). The last lines let the API know the visitor used HTTPS.

**Check:**

```powershell
Get-WebGlobalModule -Name ApplicationRequestRouting -ErrorAction SilentlyContinue
Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' | Select-Object enabled, timeout, preserveHostHeader, includePortInXForwardedFor
```

**Do:**

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

**If not:** `Set-WebConfigurationProperty` reports that `system.webServer/proxy` doesn't exist → the ARR installer
didn't finish: run the first two lines again and check the exit code is `0`.

---

# Part D — SQL Server 2025 Express (all the databases)

Run every step in **[VPS PowerShell]**, with the session settings, `$WI` and the helpers from step 5 loaded.

## 16. Look for an existing SQL Server Express

**Why:** some VPS images come with SQL Server already installed.

**Check:**

```powershell
Get-Service -Name 'MSSQL$*' -ErrorAction SilentlyContinue | Select-Object Name, Status, StartType
```

Then choose your case:

| What you see | Your case | What to do |
|---|---|---|
| No output | SQL Server isn't installed | Keep step 5's `SqlInstance = 'SQLEXPRESS'` and `SqlPort = 1433`. Continue with step 17. |
| `MSSQL$SQLEXPRESS` (or another `MSSQL$…`) **used by other applications** on this server, for example SQL Server 2019 Express behind your other web sites | **Side by side** (recommended): Wabi Insight gets its own SQL Server 2025 instance | In step 5 set `SqlInstance = 'SQLEXPRESS2025'` and `SqlPort = 14333`, paste the values box again, then continue with step 17. The existing instance, its databases, its port and its logins are **not changed**; your other applications keep working as before. |
| `MSSQL$SQLEXPRESS` that **nothing else uses** | Reuse it | Keep `SQLEXPRESS` / `1433`. **Skip steps 17 and 18**, and do step 19 on it. With Express 2019 or 2022 each database is limited to 10 GB (2025: 50 GB). |

**Side by side, what to know:**
- Two SQL Server versions on one Windows server is a normal, supported setup. Each instance has its own Windows service
  (`MSSQL$SQLEXPRESS` and `MSSQL$SQLEXPRESS2025`), its own databases, logins and `sa` password.
- The existing instance keeps its name and port, so the connection strings of your other applications don't change.
  Port `14333` must be free: `Test-NetConnection localhost -Port 14333 -InformationLevel Quiet` must print `False` now.
  If it prints `True`, choose another free port (for example `14334`) in step 5.
- **Memory:** each Express instance uses up to about 1.4 GB for its cache, plus overhead. Two instances need about
  3–4 GB together: comfortable on 8 GB.
- **Backups:** Wabi Insight's nightly backup (step 30) covers only **its own** instance. Keep backing up the other
  instance's databases the way you do today.
- Microsoft's installer adds the new instance without touching the existing one. Uninstalling one later (**Settings →
  Apps → Microsoft SQL Server 20xx → Uninstall → Remove**, choosing that instance) leaves the other in place.
- Later, if you want, the other applications' databases can move to the 2025 instance (a 2019 backup restores on 2025;
  the reverse is not possible).

**Reusing an instance that other applications also use** works too (keep `SQLEXPRESS` / `1433`, skip steps 17 and
18), with three things to know:
- step 19 changes the port and the sign-in mode of that instance: do it only if its **Check** shows other values, and
  expect a restart of SQL Server (the other applications lose their connection for a few seconds);
- the nightly backup (step 30) would include the other applications' databases and logins. To back up **only Wabi
  Insight's**, list them in step 5 and paste its values box again:

  ```powershell
  BackupDatabases = @('WabiInsightDB', 'FlightOperationsDB', 'HumanResourcesDB', 'ProcurementStoresDB',
                      'WabiBankPersonnelDB', 'WabiBankCoreBankingDB', 'WabiBankFinanceDB', 'WabiBankDigitalDB', 'WabiBankITDB')
  ```

  (the platform database and the eight demo databases; names that don't exist yet are skipped). Add each
  organization's database to the list when you add it (step 44);
- the instance's version decides the limits: Express 2019 and 2022 allow 10 GB per database, and a customer's `.bak`
  from a newer SQL Server cannot be restored on an older one.

If you reuse an existing instance, make sure your Windows administrator account can manage it:
`sqlcmd -S localhost\SQLEXPRESS -E -Q "SELECT IS_SRVROLEMEMBER('sysadmin')"` must print `1` (if `sqlcmd` isn't found,
see whether step 20's **Verify** works).

## 17. Download the installation media

**Why:** the small downloader fetches the full Express installer once.

**Check:** `Get-ChildItem (Join-Path $WI.Root 'install\sql') -Filter 'SQLEXPR*.exe' -ErrorAction SilentlyContinue`

**Do:**

```powershell
$ssei = Get-WiDownload 'https://go.microsoft.com/fwlink/?linkid=2216019' 'SQL2025-SSEI-Expr.exe'
$media = Join-Path $WI.Root 'install\sql'
New-Item -ItemType Directory -Force -Path $media | Out-Null
Start-Process $ssei -ArgumentList '/ACTION=Download', "/MEDIAPATH=$media", '/MEDIATYPE=Core', '/QUIET' -Wait
```

**You should see:** nothing for 1–5 minutes, then the prompt again.

**If not** (no `SQLEXPR*.exe` appears): open `C:\wabitex-apps\wabi-insight\install` in File Explorer, double-click
`SQL2025-SSEI-Expr.exe`, choose **Download Media → Express Core**, and save to `C:\wabitex-apps\wabi-insight\install\sql`.

**Verify:** the **Check** command lists `SQLEXPR_x64_ENU.exe` (or similar).

## 18. Install SQL Server Express (mixed mode, TCP on)

**Why:** this installs SQL Server unattended, with the choices the application needs:
- the instance named in step 5 (`SQLEXPRESS`, or `SQLEXPRESS2025` side by side), started automatically with Windows;
- **mixed mode**, with the `sa` password from step 7;
- the local **Administrators** group as SQL Server administrators, so your Windows account (and the nightly backup
  task) can manage it;
- **TCP on**, named pipes off; on a server without another instance, the SQL Server Browser off (not needed with a fixed
  port). Side by side, the Browser is left as it is, because the existing applications may use it.

**Check:** `Get-Service "MSSQL`$$($WI.SqlInstance)" -ErrorAction SilentlyContinue` (a result means it's installed: go to step 19).

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
    "/INSTANCENAME=$($WI.SqlInstance)", "/SQLSVCACCOUNT=`"NT Service\MSSQL`$$($WI.SqlInstance)`"", '/SQLSVCSTARTUPTYPE=Automatic',
    '/SQLSYSADMINACCOUNTS="BUILTIN\Administrators"', '/SECURITYMODE=SQL', "/SAPWD=`"$sa`"",
    '/TCPENABLED=1', '/NPENABLED=0', '/UPDATEENABLED=False'
)
if (-not (Get-Service 'MSSQL$*' -ErrorAction SilentlyContinue)) { $arguments += '/BROWSERSVCSTARTUPTYPE=Disabled' }   # alone on this server
$p = Start-Process (Join-Path $setupDir 'setup.exe') -ArgumentList $arguments -Wait -PassThru
"setup exit code $($p.ExitCode)"      # 0 = success, 3010 = success, reboot required
Remove-Variable sa
}
```

**You should see:** `setup exit code 0` (or `3010`).

**If not:**
- **Exit code 3010:** restart the VPS (**Start → Power → Restart**), reconnect, and load step 5 before **Verify**.
- **Any other code:** open `C:\Program Files\Microsoft SQL Server\*\Setup Bootstrap\Log\Summary.txt` in Notepad; the
  lines *Exit message* and *Final result* say why. A frequent cause is a pending Windows update restart (step 8).

**Verify:**

```powershell
Get-Service "MSSQL`$$($WI.SqlInstance)" | Select-Object Status, StartType        # Running, Automatic
Get-Service 'MSSQL$*' | Select-Object Name, Status                         # side by side: both instances, both Running
Get-Content "$env:ProgramFiles\Microsoft SQL Server\*\Setup Bootstrap\Log\Summary.txt" | Select-String 'Final result|Exit code' | Select-Object -First 2   # Passed, 0
```

## 19. Fixed TCP port (1433, or 14333 side by side) and mixed-mode sign-in

**Why:** the API connects to `localhost,1433` (or `localhost,14333` side by side): the port in step 5. Express uses a
changing port by default, and an existing installation may accept Windows accounts only. This step changes **only** the
instance named in step 5.

**Check** (the **Do** box uses `$sqlKey` from here, so run this first):

```powershell
$instanceId = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').($WI.SqlInstance)   # e.g. MSSQL17.SQLEXPRESS2025
$sqlKey = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\MSSQLServer"
[pscustomobject]@{
    LoginMode       = (Get-ItemProperty $sqlKey).LoginMode                                         # 2 = mixed mode
    TcpEnabled      = (Get-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp").Enabled                  # 1
    TcpPort         = (Get-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp\IPAll").TcpPort            # 1433 (14333 side by side)
    TcpDynamicPorts = (Get-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp\IPAll").TcpDynamicPorts    # (empty)
}
Test-NetConnection -ComputerName localhost -Port $WI.SqlPort -InformationLevel Quiet              # True
```

**Do:**

```powershell
Set-ItemProperty $sqlKey -Name LoginMode -Value 2
Set-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp" -Name Enabled -Value 1
Set-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp\IPAll" -Name TcpDynamicPorts -Value ''
Set-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp\IPAll" -Name TcpPort -Value "$($WI.SqlPort)"
Restart-Service "MSSQL`$$($WI.SqlInstance)" -Force
```

**Verify:** run the **Check** box again: `2, 1, 1433` (or `14333`)`, (empty)` and `True`. Side by side, your other
applications still work (open one of their pages).

**If not:** `Test-NetConnection` stays `False` → `Get-Service "MSSQL`$$($WI.SqlInstance)"` must be *Running*; if it
stopped, **Event Viewer** (`eventvwr`) → **Windows Logs → Application**, source *MSSQL$SQLEXPRESS2025* (or
*MSSQL$SQLEXPRESS*), says why: often *the port is already in use* → choose another free port in step 5 and run step 19
again.

## 20. The PowerShell `SqlServer` module

**Why:** this guide (and the nightly backup) run SQL with Microsoft's `Invoke-Sqlcmd`.

**Check:** `Get-Module -ListAvailable SqlServer | Select-Object Name, Version`

**Do:**

```powershell
Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force | Out-Null
Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
Install-Module -Name SqlServer -Scope AllUsers -AllowClobber -Force
```

**You should see:** the prompt again after 1–2 minutes.

**Verify** the edition, the version and mixed mode:

```powershell
Invoke-WiSql "SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(64)) AS Edition, CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(32)) AS Version, CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int) AS WindowsAuthOnly, IS_SRVROLEMEMBER('sysadmin') AS YouAreAdmin"
# Express Edition (64-bit)   17.0.xxxx.x   0   1
```

**If not:** *Login failed for user '…\Administrator'* → SQL Server was installed earlier without the Administrators
group (step 16). Sign in with `sa` once to add it:
`Invoke-WiSql -Credential (Get-Credential sa) "CREATE LOGIN [BUILTIN\Administrators] FROM WINDOWS; ALTER SERVER ROLE sysadmin ADD MEMBER [BUILTIN\Administrators];"`.

## 21. The platform database and its login

**Why:** `WabiInsightDB` is the platform's own database. The application signs in with `wabiinsightdbowner`, which owns
this database only (no server-wide rights). The tables are created by the first deployment (step 31).

**Check:**

```powershell
Invoke-WiSql "SELECT DB_ID(N'WabiInsightDB') AS DatabaseId, SUSER_ID(N'wabiinsightdbowner') AS LoginId"
```

Both have values (an earlier installation) → keep the existing password and go to **Verify**.

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

**If not:** `Login failed` → a wrong password, or step 19 (mixed mode) isn't done.

## 22. Check the connections the server needs

**Why:** to find a blocked connection now rather than during the first deployment.

**Verify** (each line must print `True`):

```powershell
Test-NetConnection localhost -Port $WI.SqlPort -InformationLevel Quiet                       # SQL Server Express (step 19)
Test-NetConnection github.com -Port 443 -InformationLevel Quiet                              # GitHub
Test-NetConnection api.github.com -Port 443 -InformationLevel Quiet                          # GitHub's API (the runner, downloads)
Test-NetConnection api.anthropic.com -Port 443 -InformationLevel Quiet                       # the AI provider
[bool](Get-Command curl.exe -ErrorAction SilentlyContinue)                                   # the deploy script uses curl.exe
```

**If not:** `False` on the first line → step 19. `False` on the others → the VPS provider blocks outgoing traffic: ask them to allow it
(step 2). The SMTP server is checked in step 33.

---

# Part E — Configure the application and the website

## 23. Application pools and permissions

**Why:** each IIS site runs in its own pool (process) with only the rights it needs.

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
$dataRoot = Split-Path $WI.Backup -Parent
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
```

## 24. This server's settings: `appsettings.Local.json`

**Why:** every deployment installs the repository's `appsettings.json` (the shared settings: SMTP account, AI key).
`appsettings.Local.json`, next to it, holds what belongs to **this** server, and **overrides** `appsettings.json`.
Deployments never overwrite or delete it. It sets:
- the connection to **this VPS's** SQL Server Express (steps 18–21);
- the site's address (used in every e-mail link);
- the files folder on this server's disk (step 10);
- the key-ring folder, and the folder of the application's log file. Under IIS the API has no console, so without this
  its messages (e-mails sent, scheduled runs, start-up notes) are written nowhere.

**[VPS PowerShell] Check:** `Test-Path (Join-Path $WI.Root 'web\api\appsettings.Local.json')`

**Do:** paste the **password of `wabiinsightdbowner`** (step 7) when asked. If the file exists already, the box leaves it
alone; to write it again, delete it first.

```powershell
. {
if (-not $dbPassword) { $dbPassword = Read-WiSecret 'Password of wabiinsightdbowner (step 7)' }
$localSettings = [ordered]@{
    ConnectionStrings = [ordered]@{ ConfigurationDatabase = "Server=localhost,$($WI.SqlPort);Database=WabiInsightDB;User Id=wabiinsightdbowner;Password=$dbPassword;Encrypt=True;TrustServerCertificate=True;" }
    Platform          = [ordered]@{ BaseUrl = "https://$($WI.Domain)" }
    Storage           = [ordered]@{ Provider = 'FileSystem'; RootPath = $WI.Files }
    DataProtection    = [ordered]@{ KeyRingPath = (Join-Path $WI.Root 'keys') }
    Logging           = [ordered]@{ File = [ordered]@{ Directory = (Join-Path $WI.Root 'logs') } }
}
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
if (-not (Test-Path $localPath)) { $localSettings | ConvertTo-Json -Depth 6 | Set-Content -Path $localPath -Encoding UTF8 }
Remove-Variable dbPassword, dbCredential -ErrorAction SilentlyContinue
}
```

- **Other overrides** work the same way, for example your own SMTP password: `Email = @{ Password = '…' }`.
- **Don't use `//` comments** in this file: the deploy script reads it with PowerShell's JSON reader, which rejects them.
- **Don't add a `Bootstrap` section**: step 32 turns bootstrap on through environment variables for one command only,
  and this file would override them.

**Verify:** this shows the file with the password hidden:

```powershell
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
(Get-Content $localPath -Raw) -replace 'Password=[^;]*', 'Password=***'
$s = Get-Content $localPath -Raw | ConvertFrom-Json
$s.Platform.BaseUrl                      # https://<your domain>
$s.Storage.RootPath                      # D:\wabi-insight\files (or C:\…)
$s.Logging.File.Directory                # C:\wabitex-apps\wabi-insight\logs
icacls $localPath                        # SYSTEM, Administrators, and the API pool (read) only
```

## 25. The two IIS sites

**Why:** the API gets its own site that answers **only on this machine** (`127.0.0.1:5080`). The public site serves the
web app and passes `/api` and `/health` to it, so the browser sees one address.

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

**Why:** browsers need ports 80 and 443 open and a trusted certificate for the padlock.

**[VPS PowerShell] Check:**

```powershell
Get-NetFirewallRule -DisplayGroup 'World Wide Web Services (HTTP)', 'Secure World Wide Web Services (HTTPS)' -ErrorAction SilentlyContinue |
    Select-Object DisplayName, Enabled, Direction, Action
Get-NetFirewallPortFilter | Where-Object { $_.LocalPort -in '1433', "$($WI.SqlPort)" } | Get-NetFirewallRule | Where-Object { $_.Enabled -eq 'True' -and $_.Direction -eq 'Inbound' } | Select-Object DisplayName   # (nothing)
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

2. Get a free Let's Encrypt certificate with **win-acme**. It renews by itself. DNS (step 3) must already point at the
   VPS (step 6's last line), and port 80 must be open (step 2). The box uses a win-acme that is already on the server
   (for example in `C:\Tools\win-acme`, when other sites use it) or downloads it:

   ```powershell
   . {
   $wacs = @('C:\Tools\win-acme\wacs.exe', (Join-Path $WI.Root 'tools\win-acme\wacs.exe'), "$env:ProgramFiles\win-acme\wacs.exe") |
       Where-Object { Test-Path $_ } | Select-Object -First 1
   if (-not $wacs) {
       $release = Invoke-RestMethod 'https://api.github.com/repos/win-acme/win-acme/releases/latest' -Headers @{ 'User-Agent' = 'wabi-insight-setup' }
       $asset = $release.assets | Where-Object name -match 'x64.*\.zip$' |
           Sort-Object { $_.name -notmatch 'trimmed' }, { $_.name -match 'pluggable' } | Select-Object -First 1
       if (-not $asset) { throw "No x64 zip found in win-acme $($release.tag_name): download it from https://www.win-acme.com and extract it into $(Join-Path $WI.Root 'tools\win-acme')." }
       $zip = Get-WiDownload $asset.browser_download_url $asset.name
       $wacsDir = Join-Path $WI.Root 'tools\win-acme'
       Expand-Archive $zip -DestinationPath $wacsDir -Force
       $wacs = Join-Path $wacsDir 'wacs.exe'
   }
   "Using $wacs"
   $siteId = (Get-Website -Name 'WabiInsight').Id
   & $wacs --source iis --siteid $siteId --host $WI.Domain --emailaddress $WI.CertEmail --accepttos `
       --installation iis --installationsiteid $siteId --sslport 443
   }
   ```

**You should see:** `Using …\wacs.exe`, then win-acme validates over port 80 (*Authorization result: valid*), stores the
certificate, **adds the `https` binding (port 443) to the site**, and creates or reuses its daily renewal task. An
existing win-acme keeps the certificates of your other sites as they are and adds one renewal for this site.

**If not:**
- *Authorization failed* / *Timeout* → DNS doesn't point at the VPS yet (step 6, last line), or port 80 is closed
  (step 2). Fix it and paste the box again.
- The certificate is created but **no `https` binding** appears (the output has *Store with CertificateStore* and no
  line about IIS) → your win-acme didn't run the IIS installation: paste the box again; if it still adds none, run
  `& $wacs` without arguments → **M: Create certificate (full options)** → source **IIS**, site **WabiInsight**, store
  **Certificate Store**, installation **IIS Web**. Don't add the binding by hand: a renewal would not update it.
- *No x64 zip found* or a download error → get the x64 zip from <https://www.win-acme.com>, extract it into
  `C:\wabitex-apps\wabi-insight\tools\win-acme`, and paste the box again.
- *An argument error* in your win-acme version → run `& $wacs` without arguments and choose **N: Create certificate
  (default settings)** → the site **WabiInsight**.
- **You already have a certificate (`.pfx` file)?** Copy it to the VPS and import it instead:
  `Import-PfxCertificate -FilePath <path> -CertStoreLocation Cert:\LocalMachine\My -Password (Read-Host -AsSecureString)`,
  then `New-WebBinding -Name 'WabiInsight' -Protocol https -Port 443 -HostHeader $WI.Domain -SslFlags 1` and
  `(Get-WebBinding -Name 'WabiInsight' -Protocol https).AddSslCertificate('<thumbprint>', 'My')`. You renew it yourself.

**Verify:**

```powershell
Get-WebBinding -Name 'WabiInsight' | Select-Object protocol, bindingInformation, sslFlags        # http *:80:<domain> and https *:443:<domain> (sslFlags 1)
netsh http show sslcert hostnameport="$($WI.Domain):443" | Select-String 'Certificate Hash|Store Name'
Get-ScheduledTask | Where-Object TaskName -like 'win-acme*' | Select-Object TaskName, State    # the renewal task, Ready
```

Run the **Check** box again too: the HTTP and HTTPS rules show `True / Inbound / Allow`, no inbound rule lists the SQL
port, and the certificate is listed with an expiry date (`NotAfter`) in the future.

---
# Part F — CI/CD and backups: deploy from GitHub with one click

## 27. How CI and CD work in this repository

| Part | File | When it runs | What it does |
|---|---|---|---|
| **CI**, already active | `.github/workflows/ci.yml` | **Automatically** on every pull request and every merge to `main` | Builds the API, runs the unit and SQL Server integration tests (and the unit tests on Windows), lints, tests and builds the web app. A red ✗ on a pull request means don't merge it yet. |
| **CD** | `.github/workflows/deploy-windows.yml` (**Deploy (Windows)**) | **Only when you click Run workflow** | GitHub builds the parts you choose. The runner on your VPS installs them with `C:\wabitex-apps\wabi-insight\deploy\Deploy-WabiInsight.ps1`. |

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

- **Before migrations**, the deploy script runs the nightly backup task (step 30) and waits for it (up to 15 minutes).
  If the backup fails, nothing is migrated and the previous API is put back.
- A migration that has run **stays applied**, even when the deployment fails afterwards. To undo it, restore the backup
  (step 43).
- **No server secret is stored in GitHub.** The server keeps its own `appsettings.Local.json`.
- **Nothing is deployed by itself.** Merging a pull request starts only the **CI** run on `main` (build and tests).
  A deployment starts when you click **Run workflow** on **Deploy (Windows)**, and you don't have to wait for the CI
  run first: the deployment builds and tests the code itself.

**The run options:**

| Option | Values | Meaning |
|---|---|---|
| **Use workflow from** | a branch (normally `main`) | The code that is built. |
| **component** | `frontend+backend` (default), `backend`, `frontend` | What to deploy. |
| **migrate_database** | off (default) / on | Apply database migrations, after the backup task. Only together with the backend. |
| **run_tests** | on (default) / off | Unit tests (API) and lint + tests (web) before packaging. |
| **ref** | empty, or a commit SHA or tag | Deploy that exact version instead of the branch tip, for example to roll back (step 43). |

## 28. A GitHub environment for the server

**Where:** [GitHub] → the repository → **Settings → Environments**.

**Why:** each deployment is recorded there (history, link to the site, optional approval).

**Check:** is `production-windows` listed?

**Do:**
1. **New environment** → name `production-windows` → **Configure environment**.
2. **Deployment branches and tags:** *Selected branches and tags* → **Add deployment branch or tag rule** → `main`.
3. Optional: **Required reviewers**: add yourself, to approve each deployment before it touches the server.
4. **Environment variables → Add variable:** name `WI_PUBLIC_URL`, value `https://<your domain>`.

On GitHub Free, private repositories don't get branch rules and reviewers; the workflow still runs without them.

**Verify:** `production-windows` is listed with the variable.

## 29. Install the runner on the VPS (as a Windows service)

**Why:** the runner receives the deployments you start on GitHub. It runs as **LocalSystem**, because deployments write
to protected folders, take the API offline, start the backup task and run migrations.

**Another GitHub runner already on this server** (for another project, usually in `C:\actions-runner`)? Leave it alone.
Set `RunnerDir = 'C:\actions-runner-wabi-insight'` in step 5 and paste its values box again: Wabi Insight gets its own
runner folder and its own Windows service. A runner only takes jobs of the repository it is registered to, so the two
never pick up each other's work.

**[VPS PowerShell] Check:**

```powershell
$WI.RunnerDir                                                  # C:\actions-runner, or C:\actions-runner-wabi-insight next to another runner
Test-Path (Join-Path $WI.RunnerDir 'config.cmd')               # True = already downloaded
Get-Service -Name 'actions.runner.*' -ErrorAction SilentlyContinue | Select-Object Name, Status, StartType   # every runner on this server
```

**Do, part 1:** download the runner into a folder only administrators can read (it will hold its credentials), and
check the download's fingerprint:

```powershell
. {
$runnerDir = $WI.RunnerDir
if ((Test-Path (Join-Path $runnerDir '.runner')) -and -not (Select-String -Path (Join-Path $runnerDir '.runner') -Pattern $WI.Repo -SimpleMatch -Quiet)) {
    throw "$runnerDir holds a runner of another repository: set RunnerDir = 'C:\actions-runner-wabi-insight' in step 5 and run this again."
}
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

**You should see:** `checksum OK`. **If** it prints `Runner checksum mismatch`: delete the folder in `$WI.RunnerDir` and
run the box again. **If** it says the folder *holds a runner of another repository*: follow the message.

**Do, part 2:** register it:
1. **[GitHub]** the repository `mtawfig/wabi-data-harbor` → **Settings → Actions → Runners → New self-hosted runner →
   Windows**. In the *Configure* box, copy only the token after `--token` (a long code). It's valid for **one hour**, and
   it is a secret: paste it only into the PowerShell prompt below, never into a chat or an e-mail.
2. **[VPS PowerShell]** paste the token when asked:

```powershell
. {
Set-Location $WI.RunnerDir
$token = Read-Host 'Runner registration token'
.\config.cmd --unattended --url "https://github.com/$($WI.Repo)" --token $token `
    --name "$env:COMPUTERNAME-wabi-insight" --labels wabi-insight --work _work --runasservice --replace
Remove-Variable token
$serviceName = (Get-Content (Join-Path $WI.RunnerDir '.service') -Raw).Trim()     # this runner's own service, not another runner's
"Wabi Insight runner service: $serviceName"
sc.exe config $serviceName obj= LocalSystem | Out-Null           # deployments change protected folders
sc.exe config $serviceName start= delayed-auto | Out-Null
Restart-Service $serviceName
Set-Location $HOME
}
```

**You should see:** *√ Runner successfully added*, *√ Settings Saved*, then `Wabi Insight runner service:
actions.runner.mtawfig-wabi-data-harbor.…`.

**If not:** *Http response code: NotFound* or *token expired* → get a new token (item 1) and paste the box again.

**Verify:**

```powershell
$serviceName = (Get-Content (Join-Path $WI.RunnerDir '.service') -Raw).Trim()
Get-CimInstance Win32_Service -Filter "Name = '$serviceName'" | Select-Object Name, State, StartName, StartMode   # Running, LocalSystem, Auto
Get-Content (Get-ChildItem (Join-Path $WI.RunnerDir '_diag') -Filter 'Runner_*.log' | Sort-Object LastWriteTime | Select-Object -Last 1).FullName -Tail 20 | Select-String 'Listening for Jobs'
```

**[GitHub] Settings → Actions → Runners** shows the runner **Idle**, with the labels `self-hosted`, `Windows`, `X64` and
`wabi-insight`.

## 30. Nightly backups: every database, the logins, the key ring and the files

**Why:** a restore needs **four things from the same night**: the databases, the SQL logins (they live outside the
databases), the key ring (it decrypts the secrets stored in the database) and the files. SQL Server Express has no SQL
Server Agent, so a Windows scheduled task named **`Wabi Insight backup`** does it every night at 02:30:

| What | Where |
|---|---|
| `<database>.bak` for **every** database on the server (`WabiInsightDB`, the demo and the organizations' databases, including ones added later) | `D:\wabi-insight\backup\<yyyyMMdd-HHmm>\` |
| `logins.sql`: the SQL logins with their password hashes, so a restored database finds its logins again | same folder |
| `keys.zip`: the key ring | same folder |
| `files\`: a mirror of `D:\wabi-insight\files` (robocopy copies only the changes) | `D:\wabi-insight\backup\files\` |

- It backs up the Wabi Insight instance only (step 5's port); side by side, the other instance is not included.
- On an instance **shared** with other applications, it backs up only the databases listed in step 5
  (`BackupDatabases`) and the SQL logins those databases use; `backup.log` names the listed databases that don't exist
  yet. With an empty list it backs up every database and login of the instance.
- The VPS keeps the last **7** nights (about 0.5 GB a night with the demo organizations).
- **The deploy script runs this same task before every database migration**, and stops if it fails. Keep the task name
  exactly `Wabi Insight backup`.
- The task runs as `SYSTEM`, made a SQL Server administrator here so that it can back up every database and read the
  logins. No password is stored on disk.
- Each run writes a line per step to `D:\wabi-insight\backup\backup.log`.
- **The backups sit on the same server as the data.** If the VPS is lost, they're lost too: copy them off the server
  regularly (step 42), or keep the copies in Azure automatically with
  [DEPLOYMENT_WINDOWS_AZURE.md](DEPLOYMENT_WINDOWS_AZURE.md) (step 46).

**[VPS PowerShell] Check:**

```powershell
Get-ScheduledTask -TaskName 'Wabi Insight backup' -ErrorAction SilentlyContinue | Select-Object TaskName, State
Test-Path (Join-Path $WI.Backup 'backup.ps1')
```

**Do:** the box lets SQL Server write into the backup folder, makes `SYSTEM` a SQL Server administrator, writes the
backup script `backup.ps1` into the backup folder, and registers the task:

```powershell
. {
$backup = $WI.Backup
icacls $backup /grant "NT Service\MSSQL`$$($WI.SqlInstance):(OI)(CI)M" | Out-Null    # SQL Server writes the .bak files
Invoke-WiSql "IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS; ALTER SERVER ROLE sysadmin ADD MEMBER [NT AUTHORITY\SYSTEM];"
$script = @'
# Nightly backup of every database, the SQL logins, the key ring and the files.
# Runs as SYSTEM from the "Wabi Insight backup" task (docs/DEPLOYMENT_WINDOWS_SERVER_2019.md, step 30). Log: backup.log here.
param([string]$Keys = '__KEYS__', [string]$Files = '__FILES__', [int]$KeepDays = 7, [string[]]$Only = @(__ONLY__))
$ErrorActionPreference = 'Stop'
$log = Join-Path $PSScriptRoot 'backup.log'
function Write-Log([string]$Message) { Add-Content -Path $log -Value ('{0:yyyy-MM-dd HH:mm:ss} {1}' -f (Get-Date), $Message) }
try {
    Import-Module SqlServer
    $sql = @{ ServerInstance = '__SERVER__'; TrustServerCertificate = $true; QueryTimeout = 3600; DisableVariables = $true }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmm'
    $night = Join-Path $PSScriptRoot $stamp
    New-Item -ItemType Directory -Force -Path $night | Out-Null
    Write-Log "start $stamp"
    # $Only empty: every database of this SQL Server. $Only filled (step 5, BackupDatabases): those databases and their logins only.
    $which = if ($Only.Count) { 'name IN (' + (($Only | ForEach-Object { "N'" + $_.Replace("'", "''") + "'" }) -join ', ') + ')' } else { 'database_id > 4' }
    $databases = @((Invoke-Sqlcmd @sql -Query "SELECT name FROM sys.databases WHERE $which AND source_database_id IS NULL AND state_desc = N'ONLINE' ORDER BY name").name)
    foreach ($db in $databases) {
        $bak = Join-Path $night "$db.bak"
        Invoke-Sqlcmd @sql -Query "BACKUP DATABASE [$db] TO DISK = N'$bak' WITH INIT, CHECKSUM;"
        Write-Log "database $db $([math]::Round((Get-Item $bak).Length / 1MB, 1)) MB"
    }
    $missing = @($Only | Where-Object { $databases -notcontains $_ })
    if ($missing.Count) { Write-Log "not on this server (yet): $($missing -join ', ')" }
    $ownLogins = if ($Only.Count -and $databases.Count) { 'AND sid IN (' + (($databases | ForEach-Object { "SELECT sid FROM [$_].sys.database_principals WHERE type = 'S' AND sid IS NOT NULL" }) -join ' UNION ') + ')' } else { '' }
    $logins = Invoke-Sqlcmd @sql -MaxCharLength 8000 -Query "SELECT CONCAT(N'IF SUSER_ID(N''', name, N''') IS NULL CREATE LOGIN [', name, N'] WITH PASSWORD = ', CONVERT(nvarchar(max), CAST(LOGINPROPERTY(name, 'PasswordHash') AS varbinary(256)), 1), N' HASHED, SID = ', CONVERT(nvarchar(max), sid, 1), N', CHECK_POLICY = OFF, DEFAULT_DATABASE = [master];') AS Line FROM sys.sql_logins WHERE name <> N'sa' AND name NOT LIKE N'##%' $ownLogins ORDER BY name;"
    @($logins | ForEach-Object { $_.Line }) | Set-Content -Path (Join-Path $night 'logins.sql') -Encoding UTF8
    if (Get-ChildItem $Keys -Force) {        # the key ring stays empty until the API first starts (step 31)
        Compress-Archive -Path $Keys -DestinationPath (Join-Path $night 'keys.zip') -Force
    }
    $ErrorActionPreference = 'Continue'
    robocopy.exe $Files (Join-Path $PSScriptRoot 'files') /MIR /R:2 /W:5 /NP /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy of $Files failed with exit code $LASTEXITCODE" }
    $ErrorActionPreference = 'Stop'
    Write-Log "saved: $night and the files mirror"
    Get-ChildItem $PSScriptRoot -Directory | Where-Object { $_.Name -match '^\d{8}-\d{4}$' -and $_.CreationTime -lt (Get-Date).AddDays(-$KeepDays) } |
        Remove-Item -Recurse -Force
    Write-Log 'done'
    exit 0
} catch {
    Write-Log "FAILED: $($_.Exception.Message)"
    exit 1
}
'@
$script = $script.Replace('__KEYS__', (Join-Path $WI.Root 'keys')).Replace('__FILES__', $WI.Files).Replace('__SERVER__', "localhost,$($WI.SqlPort)")
$script = $script.Replace('__ONLY__', ((@($WI.BackupDatabases) | Where-Object { $_ } | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ', '))
Set-Content -Path (Join-Path $backup 'backup.ps1') -Value $script -Encoding UTF8
$action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$backup\backup.ps1`""
$trigger   = New-ScheduledTaskTrigger -Daily -At '02:30'
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName 'Wabi Insight backup' -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
'Backup task registered.'
}
```

**You should see:** `Backup task registered.`

**Verify now** (the key ring and the tables don't exist yet, so this proves the mechanism; step 31 checks it again):

```powershell
Start-ScheduledTask -TaskName 'Wabi Insight backup'
do { Start-Sleep -Seconds 5 } while ((Get-ScheduledTask -TaskName 'Wabi Insight backup').State -eq 'Running')
(Get-ScheduledTaskInfo -TaskName 'Wabi Insight backup').LastTaskResult                   # 0
Get-Content (Join-Path $WI.Backup 'backup.log') -Tail 5                                    # start … / database WabiInsightDB … / saved … / done
$latest = Get-ChildItem $WI.Backup -Directory | Where-Object Name -match '^\d{8}-\d{4}$' | Sort-Object Name | Select-Object -Last 1
Get-ChildItem $latest.FullName | Select-Object Name, Length                                 # WabiInsightDB.bak, logins.sql
Invoke-WiSql "RESTORE VERIFYONLY FROM DISK = N'$($latest.FullName)\WabiInsightDB.bak' WITH CHECKSUM;" -Verbose 4>&1 | Select-Object -Last 1   # The backup set on file 1 is valid.
Get-ChildItem (Join-Path $WI.Backup 'files') | Select-Object Name                            # the same folders as D:\wabi-insight\files
```

**If not:** `LastTaskResult` is `1` → the last line of `backup.log` (`FAILED: …`) says why:

| `backup.log` says | Cause | Fix |
|---|---|---|
| *Cannot open backup device … Access is denied* | SQL Server can't write to the backup folder | The `icacls` line at the top of **Do** |
| *Login failed for user 'NT AUTHORITY\SYSTEM'* | The SQL line at the top of **Do** didn't run | Paste **Do** again |
| *robocopy of … failed* | The files folder or the disk | `Get-PSDrive C, D -ErrorAction SilentlyContinue` (free space); step 10 (folders) |
| *There is not enough space on the disk* | The backup disk is full | Free space, or lower `KeepDays` at the top of `backup.ps1` |

---

## 31. Install the deploy script, then the first deployment

**Where:** first [GitHub], then [VPS PowerShell], then [GitHub] again.

**Why:** the runner runs an **installed** copy of the deploy script, never a copy from GitHub. Only Administrators and
SYSTEM can change it (step 10), so a workflow can't replace the deploy logic.

**Check:**

```powershell
$deployScript = Join-Path $WI.Root 'deploy\Deploy-WabiInsight.ps1'
if (Test-Path $deployScript) { & $deployScript -ScriptVersion } else { 'not installed' }     # 5 = the current version is installed
```

**Do, part 1:** create a short-lived GitHub token, used once to download the script:
1. **[GitHub]** your profile picture → **Settings → Developer settings → Personal access tokens → Fine-grained
   tokens → Generate new token**.
2. Fill in:
   - **Name:** `wabi-insight-server-download`;
   - **Resource owner:** the owner of the repository (`mtawfig`);
   - **Expiration:** 7 days (you use it again in step 35);
   - **Repository access:** *Only select repositories* → `mtawfig/wabi-data-harbor`;
   - **Permissions → Repository permissions → Contents:** **Read-only**.
3. **Generate token** and copy it (starts with `github_pat_`). Keep it in your password manager until step 35 is done.

**Do, part 2:** **[VPS PowerShell]** paste the token when asked:

```powershell
. {
$deployDir = Join-Path $WI.Root 'deploy'
$ghToken = Read-WiSecret 'GitHub token (Contents: read)'
Get-WiRepoFile 'deploy/cicd/windows/Deploy-WabiInsight.ps1' (Join-Path $deployDir 'Deploy-WabiInsight.ps1') $ghToken
Remove-Variable ghToken
Unblock-File (Join-Path $deployDir 'Deploy-WabiInsight.ps1')                # downloaded files are blocked by default
& (Join-Path $deployDir 'Deploy-WabiInsight.ps1') -ScriptVersion            # 5
}
```

**If not:** *(401) Unauthorized* or *(404) Not Found* → the token was pasted wrongly, expired, or can't see the
repository: make a new one (part 1). Without any token: on GitHub open `deploy/cicd/windows/Deploy-WabiInsight.ps1` →
**Download raw file**, copy it into `C:\wabitex-apps\wabi-insight\deploy` (copy and paste work through Remote Desktop),
then run the `Unblock-File` and `-ScriptVersion` lines.

**Verify** the script and the rights:

```powershell
Get-Content (Join-Path $WI.Root 'deploy\Deploy-WabiInsight.ps1') -TotalCount 3    # starts with <# .SYNOPSIS Installs a Wabi Insight release …
icacls (Join-Path $WI.Root 'deploy')                                              # SYSTEM and Administrators only
Get-ExecutionPolicy                                                               # RemoteSigned (the Windows Server default) or Unrestricted
```

**Do, part 3:** the first deployment.
1. **[GitHub]** **Actions** → **Deploy (Windows)** (left list) → **Run workflow**.
2. **Use workflow from:** `main`. **component:** `frontend+backend`. **migrate_database:** ✅ (this creates the tables
   in the empty database). **run_tests:** ✅. **ref:** empty.
3. **Run workflow**, then click the new run to watch it. With required reviewers (step 28), approve it with **Review
   deployments**.

**You should see:**

| Job | Runs on | Duration (first run) |
|---|---|---|
| Build / Resolve version | GitHub | about 20 seconds |
| Build / Backend (.NET 10) | GitHub | 2–4 minutes |
| Build / Frontend (Angular 22) | GitHub | 2–4 minutes (at the same time) |
| Deploy to Windows server | your VPS | 2–4 minutes |

The deploy log shows each stage: `==> Backend: …`, `==> Database: backup before migration (scheduled task 'Wabi Insight
backup')`, `==> Database: applying migrations`, `==> Frontend: …`, `==> Site check through IIS: https://<domain>`. It
ends with `Deployment <release> succeeded.` All jobs are green ✓.

**If not:**

| What you see | Cause | Fix |
|---|---|---|
| The deploy job waits *Waiting for a runner to pick up this job* | The runner service is stopped | Step 29 **Verify**; `Start-Service (Get-Content (Join-Path $WI.RunnerDir '.service') -Raw).Trim()` |
| *Check the server's deploy script* fails | Part 2 not done | Part 2 |
| *appsettings.Local.json is missing* | Step 24 not done | Step 24 |
| *The server has no .NET 10 runtime* (or, with an older deploy script, *You must install or update .NET to run this application* during the migration) | The Hosting Bundle 10 is not installed; an older .NET on the server is not enough | Step 13 **Do**, then run the deployment again. Nothing was changed in the database. |
| A **Build** job fails (red, on GitHub) | The code did not build or a test failed. Nothing reached the server. | Open the red step: its last lines name the file and the error. Fix it in a pull request, merge, and run the deployment again. |
| *Backup task finished with result 1; nothing was migrated* | The backup failed | Step 30, the **If not** table (`backup.log`) |
| *did not report Healthy* | The API couldn't start; the job prints the reason | Usually a typo in `appsettings.Local.json` or a wrong database password (step 24) |
| `file-storage` or `key-ring` *Unhealthy* in the health report | The API's account can't write to the files or key ring folder; the text names it | Step 23 **Do** (the `icacls` lines), step 24 **Verify** (`Storage.RootPath`), then run again |
| *The release is installed, but …* (site check) | IIS, ARR or the certificate | Steps 15, 25 and 26 **Verify** |

**Verify** ([VPS PowerShell]):

```powershell
Get-Content (Join-Path $WI.Root 'logs\deploy.log') -Tail 3                                   # … started … / … succeeded release=…
Get-Content (Join-Path $WI.Root 'web\api\deploy-info.json')                                  # "version": the commit you deployed
Invoke-WiSql -Database WabiInsightDB "SELECT COUNT(*) AS Applied, MAX(MigrationId) AS Latest FROM dbo.__EFMigrationsHistory"   # a number and the newest migration's name
(Invoke-WebRequest 'http://127.0.0.1:5080/health/ready' -UseBasicParsing).Content          # "status":"Healthy" … configuration-database, database-schema, file-storage, key-ring … Healthy
Invoke-WebRequest 'http://127.0.0.1:5080/api/auth/csrf' -UseBasicParsing -Headers @{ 'X-Forwarded-Proto' = 'https' } | Out-Null   # first use of the key ring
Get-ChildItem (Join-Path $WI.Root 'keys')                                                  # key-<id>.xml: the key ring now exists
Get-Content (Get-ChildItem (Join-Path $WI.Root 'logs\wabi-insight-*.log') | Sort-Object Name | Select-Object -Last 1) -Tail 5   # the API's own log
```

[Your computer] open `https://<your domain>` in your browser: the Wabi Insight landing page appears with a **padlock**,
and `http://<your domain>` switches to `https://` by itself.

Now run the **Verify** of step 30 again: the newest backup folder now also has `keys.zip`, and `WabiInsightDB.bak` holds
the tables.

---

# Part G — First use, from your PC's browser

## 32. Create the first Platform Administrator (once)

**Where:** [VPS PowerShell].

**Why:** nobody can sign in until there is a first Platform Administrator.

**Check:** `0` means not done yet:

```powershell
Invoke-WiSql -Database WabiInsightDB "SELECT COUNT(*) AS Administrators FROM dbo.Administrators"
```

**Do:**
1. Choose the administrator's first password: at least 12 characters with upper case, lower case, a digit and a symbol.
   You change it at the first sign-in.
2. Run this. The bootstrap settings exist only inside this PowerShell window and are removed afterwards:

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

**You should see:** `Bootstrap completed. AdministratorId=…`.

Type the password at the hidden prompt of the box. **Don't put it on a command line** (for example
`$env:Bootstrap__AdministratorPassword = "…"`): inside double quotes PowerShell replaces `$` signs, so a password such
as `Pa$$word` is stored as something else and the sign-in fails. If that happened, use **Forgot password?** on the
sign-in page: the reset link goes to the administrator's mailbox.

**If not:** *bootstrap_already_completed* → it was done before: sign in with that account, or use **Forgot password?**
on the sign-in page.

**Verify:** the **Check** query returns `1`, and `Get-ChildItem Env: | Where-Object Name -like 'Bootstrap__*'` prints
nothing.

## 33. Go-live checks

**Where:** [VPS PowerShell] for the first box, then [Your computer].

**Verify, part 1** — the server reaches the e-mail server and the AI provider:

```powershell
$appSettings = (Get-Content (Join-Path $WI.Root 'web\api\appsettings.json') | Where-Object { $_ -notmatch '^\s*//' }) -join "`n" | ConvertFrom-Json
Test-NetConnection $appSettings.Email.Host -Port $appSettings.Email.Port -InformationLevel Quiet   # True: the SMTP server is reachable
Test-NetConnection api.anthropic.com -Port 443 -InformationLevel Quiet                             # True: the AI provider is reachable
```

(`appsettings.json` contains `//` comment lines, which Windows PowerShell's JSON reader rejects; the first line skips
them.)

**Verify, part 2** — from your own computer (not the VPS: IIS treats callers on the server itself differently). In a
terminal (Windows: **Command Prompt** or **PowerShell**; Mac: **Terminal**), with your domain:

```text
curl -s -i -X POST "https://<your domain>/api/auth/login" -H "Content-Type: application/json" -d "{}"
curl -s -I "https://<your domain>/index.html"
```

**You should see:** the first answers `HTTP/1.1 400` with `Content-Type: application/problem+json` (the API's own error
reaches you through IIS, not an IIS error page); the second shows `Cache-Control: no-cache` (browsers pick up each new
release at once).

**Verify, part 3** — [Your computer] in your browser:
1. Open `https://<your domain>`. The landing page loads with a padlock; **العربية** switches the layout to right to left.
2. **Sign in** with the administrator e-mail and the first password. You're asked to choose a new password: do it and
   store it.
3. **System Health** (in the platform menu): the database, the API, the files storage and the key ring are
   **Healthy**. `deployment-settings` may be *Degraded* until step 34; its text names the setting.
4. **AI Providers:** the default provider is listed, and **Test** succeeds.
5. Sign out. On the sign-in page choose **Forgot password?**, enter the administrator e-mail and open the link in the
   e-mail that arrives: it must open `https://<your domain>/reset-password` (a link to `localhost` means
   `Platform:BaseUrl` of step 24 is wrong). Close it without changing anything, and sign in again.
6. **Organizations → New organization**, with a real e-mail address for its administrator → **Create organization**.
   The **temporary password e-mail** arrives, which proves e-mail works (temporary passwords are only e-mailed). Sign in
   as that administrator in a private window, change the password, then **Organization Administration → Organization
   Settings → Report logo → Upload logo**. On the VPS, `Get-ChildItem (Join-Path $WI.Files 'organization-logos') -Recurse -File`
   lists a `.bin` file: the files go to the files folder.

**If not:**
- The page doesn't open → DNS (step 3), ports (steps 2 and 26), `Get-Website` both *Started* (step 25).
- Signed out right after signing in → you used `http://`: use `https://`.
- No e-mail arrives → **System Health → e-mail**; and *Troubleshooting* (step 49).

## 34. A dedicated login for Platform Insights

**Where:** [VPS PowerShell], then [Your computer].

**Why:** Platform Insights (organization dashboards) should read the database through a login that can read only the
`insights` views. Without it, `deployment-settings` stays *Degraded* and the API uses its own login.

**Check:** empty output means not configured:

```powershell
(Get-Content (Join-Path $WI.Root 'web\api\appsettings.Local.json') -Raw | ConvertFrom-Json).Insights.ConnectionString
```

**Do:** the password is generated and written straight into `appsettings.Local.json`; you never type it. Running the
box again sets a new password in both places.

```powershell
$insightsPassword = New-WiSecret
Invoke-WiSql "IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'wabi_insight_insights') CREATE LOGIN [wabi_insight_insights] WITH PASSWORD = N'$insightsPassword', CHECK_POLICY = ON ELSE ALTER LOGIN [wabi_insight_insights] WITH PASSWORD = N'$insightsPassword';"
Invoke-WiSql -Database WabiInsightDB "IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'wabi_insight_insights') CREATE USER [wabi_insight_insights] FOR LOGIN [wabi_insight_insights];"
Invoke-WiSql -Database WabiInsightDB "GRANT SELECT ON SCHEMA::[insights] TO [wabi_insight_insights]; DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[dbo] TO [wabi_insight_insights];"
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
$settings = Get-Content $localPath -Raw | ConvertFrom-Json
$settings | Add-Member -Force -NotePropertyName Insights -NotePropertyValue ([pscustomobject]@{
    ConnectionString = "Server=localhost,$($WI.SqlPort);Database=WabiInsightDB;User Id=wabi_insight_insights;Password=$insightsPassword;Encrypt=True;TrustServerCertificate=True;" })
$settings | ConvertTo-Json -Depth 6 | Set-Content $localPath -Encoding UTF8
Remove-Variable insightsPassword
Restart-WebAppPool 'WabiInsightApi'
```

**Verify:** [Your computer] after a minute, refresh **System Health**: `deployment-settings` is **Healthy**. **Platform
Insights Configuration** shows *Confined to the insights schema*.

**If not:** the organization dashboard or **Organization Insights** answers *The insights query failed* after this
step → the server was first migrated by a release before P13.43, where the `insights` schema belonged to the
application's login instead of the database owner, so the new login is refused on every view. Deploy the backend again
with **Apply database migrations** ticked (the migration *AlignInsightsSchemaOwner* corrects the owner and keeps this
login's grant); **Platform Insights Configuration** then no longer shows *The insights login cannot read the insights
views*.

---

# Part H — The demo organizations (Gulf Aviation Services and Wabi Bank)

Two complete, fictional organizations with their own databases on this server, users and example questions, to test
everything before real customers:

| Organization | Databases | Users | Administrator |
|---|---|---|---|
| **Gulf Aviation Services** (GAS) | `FlightOperationsDB`, `HumanResourcesDB`, `ProcurementStoresDB` | 4 | `faisal.alshamsi@gulfaviation.example` |
| **Wabi Bank** (WBK) | `WabiBankPersonnelDB`, `WabiBankCoreBankingDB`, `WabiBankFinanceDB`, `WabiBankDigitalDB`, `WabiBankITDB` | 8 | Clara Ashby, `w1001@wabibank.example` |

Every sample user starts with the password `Wabi#Sample#2026` and must change it at the first sign-in. Details:
[scripts/sql/samples/README.md](../scripts/sql/samples/README.md) and
[scripts/sql/samples/wabi-bank/README.md](../scripts/sql/samples/wabi-bank/README.md).

## 35. Download the sample scripts

**Where:** [VPS PowerShell].

**Why:** the scripts create the demo databases and register both organizations in `WabiInsightDB`.

**Check:**

```powershell
Get-ChildItem (Join-Path $WI.Root 'install\samples') -Filter '*.sql' -ErrorAction SilentlyContinue | Measure-Object | Select-Object Count   # 11 = done
```

**Do:** paste the GitHub token from step 31 (or make a new one the same way) when asked:

```powershell
. {
$samples = Join-Path $WI.Root 'install\samples'
New-Item -ItemType Directory -Force -Path $samples | Out-Null
$gas  = '01-create-flightoperationsdb.sql', '02-create-humanresourcesdb.sql', '03-create-procurementstoresdb.sql', '04-seed-wabiinsightdb.sql'
$bank = '01-create-wabibankpersonneldb.sql', '02-create-wabibankcorebankingdb.sql', '03-create-wabibankfinancedb.sql',
        '04-create-wabibankdigitaldb.sql', '05-create-wabibankitdb.sql', '06-seed-wabiinsightdb.sql', '07-check-consistency.sql'
$ghToken = Read-WiSecret 'GitHub token (Contents: read)'
foreach ($f in $gas)  { Get-WiRepoFile "scripts/sql/samples/$f" (Join-Path $samples "gas-$f") $ghToken }
foreach ($f in $bank) { Get-WiRepoFile "scripts/sql/samples/wabi-bank/$f" (Join-Path $samples "bank-$f") $ghToken }
Remove-Variable ghToken
# Save them as UTF-8 with a byte-order mark, so the Arabic names are read correctly.
foreach ($file in Get-ChildItem $samples -Filter '*.sql') {
    [IO.File]::WriteAllText($file.FullName, [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8), (New-Object Text.UTF8Encoding $true))
}
Get-ChildItem $samples | Select-Object Name, @{n='KB';e={[math]::Round($_.Length/1KB)}}
}
```

**You should see:** 11 files, `gas-01-…` to `gas-04-…` and `bank-01-…` to `bank-07-…`. Some are large (tens of MB):
the download takes a few minutes.

**If not:** *(401)* or *(404)* → the token (step 31, part 1). Without a token: download each file from the repository's
`scripts/sql/samples` and `scripts/sql/samples/wabi-bank` folders on GitHub (**Download raw file**), copy them into
`C:\wabitex-apps\wabi-insight\install\samples` with the names above, then run the last five lines of the box.

**Then delete the token:** [GitHub] *Fine-grained tokens* → the token → **Delete**.

## 36. Load Gulf Aviation Services

**Where:** [VPS PowerShell].

**Check:**

```powershell
Invoke-WiSql "SELECT name FROM sys.databases WHERE name IN (N'FlightOperationsDB', N'HumanResourcesDB', N'ProcurementStoresDB')"   # three names = done
```

**Do** (about 2 minutes):

```powershell
$samples = Join-Path $WI.Root 'install\samples'
Get-ChildItem $samples -Filter 'gas-*.sql' | Sort-Object Name | ForEach-Object {
    "== $($_.Name)"
    Invoke-WiSql -InputFile $_.FullName -Tables
}
```

**You should see:** a summary after each script; the last one lists the four users (with `Wabi#Sample#2026 (change at
first sign-in)`) and the three applications.

**If not:** *Invalid object name 'dbo.Organizations'* → the first deployment (step 31) didn't create the tables yet.
Arabic names show as `???` → the byte-order-mark lines of step 35 didn't run: run step 35 again.

## 37. Load Wabi Bank

**Where:** [VPS PowerShell].

**Check:**

```powershell
Invoke-WiSql "SELECT name FROM sys.databases WHERE name LIKE N'WabiBank%'"   # five names = done
```

**Do** (4–6 minutes; Core Banking alone takes about 2):

```powershell
$samples = Join-Path $WI.Root 'install\samples'
Get-ChildItem $samples -Filter 'bank-*.sql' | Sort-Object Name | ForEach-Object {
    "== $($_.Name)"
    Invoke-WiSql -InputFile $_.FullName -Tables
}
```

**You should see:** a summary after each script; `bank-06` lists the eight users and the five applications, and the last
lines of `bank-07` report **0 failed checks**.

**Verify** both organizations, and make a backup that includes the demo databases:

```powershell
Invoke-WiSql -Database WabiInsightDB "SELECT OrganizationCode, OrganizationName FROM dbo.Organizations ORDER BY OrganizationCode"   # GAS, WBK (and any you created)
Invoke-WiSql "SELECT COUNT(*) AS Databases FROM sys.databases WHERE database_id > 4"           # 9 (WabiInsightDB + 8 demo)
Start-ScheduledTask -TaskName 'Wabi Insight backup'
do { Start-Sleep -Seconds 5 } while ((Get-ScheduledTask -TaskName 'Wabi Insight backup').State -eq 'Running')
Get-Content (Join-Path $WI.Backup 'backup.log') -Tail 13                                    # nine "database …" lines, "saved", "done"
```

## 38. Connect the demo applications (in the browser)

**Where:** [Your computer], in your browser.

**Why:** each organization's applications need a connection to their database. This can't be scripted: the application
encrypts connection strings with this server's key ring.

**Do,** for each organization:
1. Open `https://<your domain>` → **Sign in** as its administrator (table above), password `Wabi#Sample#2026` → choose a
   new password and store it.
2. **Organization Administration → Applications** → for each application, **Replace connection** → paste its
   connection string from the table below → **Save** → **Test connection** → it must report **read-only verified** →
   **Approve for querying**.

| Organization | Application | Connection string |
|---|---|---|
| GAS | Flight Operations | `Server=localhost,1433;Database=FlightOperationsDB;User Id=flightops_reader;Password=FlightOps#Reader#2026;Encrypt=True;TrustServerCertificate=True;` |
| GAS | HR, Payroll and Attendance | `Server=localhost,1433;Database=HumanResourcesDB;User Id=hr_reader;Password=HumanRes#Reader#2026;Encrypt=True;TrustServerCertificate=True;` |
| GAS | Procurement and Stores | `Server=localhost,1433;Database=ProcurementStoresDB;User Id=procurement_reader;Password=Procure#Reader#2026;Encrypt=True;TrustServerCertificate=True;` |
| WBK | Personnel and Attendance | `Server=localhost,1433;Database=WabiBankPersonnelDB;User Id=wbpersonnel_reader;Password=WbPersonnel#Reader#2026;Encrypt=True;TrustServerCertificate=True;` |
| WBK | Core Banking | `Server=localhost,1433;Database=WabiBankCoreBankingDB;User Id=wbcore_reader;Password=WbCore#Reader#2026;Encrypt=True;TrustServerCertificate=True;` |
| WBK | Finance | `Server=localhost,1433;Database=WabiBankFinanceDB;User Id=wbfinance_reader;Password=WbFinance#Reader#2026;Encrypt=True;TrustServerCertificate=True;` |
| WBK | Digital Banking | `Server=localhost,1433;Database=WabiBankDigitalDB;User Id=wbdigital_reader;Password=WbDigital#Reader#2026;Encrypt=True;TrustServerCertificate=True;` |
| WBK | IT Help Desk | `Server=localhost,1433;Database=WabiBankITDB;User Id=wbit_reader;Password=WbIt#Reader#2026;Encrypt=True;TrustServerCertificate=True;` |

**Side by side (step 16):** write your port instead of `1433` in each string, for example `Server=localhost,14333;…`.

These sample passwords are published in the repository's READMEs; they're safe here only because SQL Server is not
reachable from the internet (steps 2 and 26).

**If not:** *Test connection* fails with *Login failed* → the string was pasted incompletely (it must end with
`TrustServerCertificate=True;`). *A network-related error* → the port in the string isn't step 5's port, or step 19.

**Verify:** sign in as another sample user, for example `khalid.alsuwaidi@gulfaviation.example` (GAS CEO) → change the
password → **Ask** → choose *Flight Operations* → click one of the example questions → an answer with a table or chart
appears.

## 39. Protect the sample accounts

**Why:** the sample users' first password is published in the repository, and their e-mail addresses are fictional
(nobody receives their e-mails). While the site is on the internet, anyone could sign in to an account nobody has used
yet.

**Do (choose one):**
- **Sign in once to every sample account** (4 for GAS, 8 for Wabi Bank; the lists are in the two READMEs) and set your
  own password: the first sign-in forces a change. Or
- As each organization's administrator, **Organization Administration → Users** → **Deactivate** the sample users you
  don't need.

**Verify:** signing in with `Wabi#Sample#2026` fails for every sample account.

---
# Part I — The full test after deployment

## 40. Test everything, end to end

**Where:** [Your computer]'s browser, then [VPS PowerShell] and [GitHub]. Tick each line; any line that fails: note
what you saw, then see *Troubleshooting* (step 49).

| # | Test | Expected |
|---|---|---|
| 1 | Open `http://<your domain>` | It switches to `https://`, padlock shown |
| 2 | Landing page, then **العربية** | The layout switches to right to left |
| 3 | Sign in as the Platform Administrator → **System Health** | Everything **Healthy** (database, schema, files, key ring, e-mail, deployment settings) |
| 4 | **Organizations** | GAS and Wabi Bank listed |
| 5 | **Forgot password?** with the administrator's real mailbox | The reset e-mail arrives; the link opens `https://<your domain>/reset-password` |
| 6 | As the GAS administrator: **Organization Administration → Applications** | 3 applications, each connected and approved |
| 7 | As the GAS CEO (Khalid): **Ask** an example question, in English | An answer with a table or chart |
| 8 | The same question in Arabic | An Arabic answer |
| 9 | **Export** the answer to PDF, Word and Excel | The three files download and open |
| 10 | On the VPS: `(Get-ChildItem $WI.Files -Recurse -File).Count` | A number above 0: answers and exports are kept in the files folder |
| 11 | **Share** the answer with another GAS user; sign in as that user | It appears under *Shared with me* |
| 12 | As Arjun (GAS, own data only), ask "Show my net pay for each month of 2025" | Only his own payslips |
| 13 | Wabi Bank: as Clara Ashby and as another bank user, ask an example question | Answers, each within that user's permissions |
| 14 | Upload an organization logo (**Organization Settings → Report logo**) | It shows; a new `.bin` file appears under `organization-logos` in the files folder |
| 15 | Next morning: `Get-Content (Join-Path $WI.Backup 'backup.log') -Tail 13` | Last night's run: nine databases, *saved*, *done* |
| 16 | **Restore test** (box below) | `Administrators: 1`, then the test copy is removed |
| 17 | [GitHub] **Deploy (Windows)** again with **component** `frontend` | Green; the site stays up |
| 18 | `Restart-Computer` on the VPS; wait 3 minutes | The site comes back by itself; **System Health** all **Healthy** |

**Restore test (16).** A backup you never restored is not a backup. This restores last night's `WabiInsightDB.bak` under
**another name**, checks it, and removes it. The live database isn't touched:

```powershell
$latest = Get-ChildItem $WI.Backup -Directory | Where-Object Name -match '^\d{8}-\d{4}$' | Sort-Object Name | Select-Object -Last 1
$bak = Join-Path $latest.FullName 'WabiInsightDB.bak'
$dataPath = (Invoke-WiSql "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(260)) AS P").P
$moves = Invoke-WiSql "RESTORE FILELISTONLY FROM DISK = N'$bak'" | ForEach-Object {
    $file = 'RestoreTest_' + $_.LogicalName + $(if ($_.Type -eq 'L') { '.ldf' } else { '.mdf' })
    "MOVE N'$($_.LogicalName)' TO N'$(Join-Path $dataPath $file)'"
}
Invoke-WiSql "RESTORE DATABASE [WabiInsightDB_RestoreTest] FROM DISK = N'$bak' WITH $($moves -join ', ');"
Invoke-WiSql -Database WabiInsightDB_RestoreTest "SELECT COUNT(*) AS Administrators FROM dbo.Administrators"   # 1
Invoke-WiSql "DROP DATABASE [WabiInsightDB_RestoreTest];"
```

---

# Part J — Running it day to day

## 41. Everyday deployments

**[GitHub] Actions → Deploy (Windows) → Run workflow**, with `main`:

| What changed | component | migrate_database |
|---|---|---|
| Only the web app (screens, texts, translations) | `frontend` | off |
| API code without a new migration | `backend` | off |
| A change that adds a migration (new files under `src/WabiInsight.Infrastructure/Persistence/Migrations/`) | `frontend+backend` | **on** |
| Not sure | `frontend+backend` | **on** (an empty migration step is harmless, and you get a fresh backup) |

A release that contains a migration can't be installed with **migrate_database** off: the new API reports *not ready*
while the database is behind it, so the deployment stops with *The database is behind this release*, puts the previous
API back and changes nothing. Run it again with **migrate_database** ticked.

`C:\wabitex-apps\wabi-insight\web\api\deploy-info.json` shows which commit is live.

## 42. Regular upkeep

| How often | What | How |
|---|---|---|
| Weekly | Check the backups ran | `Get-Content (Join-Path $WI.Backup 'backup.log') -Tail 13` → last night, *saved*, *done* |
| Weekly | **Copy the backups off the server** | Copy the newest night's folder and the `files` folder from `D:\wabi-insight\backup` to another computer or storage, or use your VPS provider's backup or snapshot service. The copy holds the databases and the key ring together: keep it private. Automatic copies in Azure: step 46. |
| Monthly | Windows and SQL Server updates | Step 8 (restart after). For SQL Server, once: **Windows Update → Advanced options → Give me updates for other Microsoft products** |
| Monthly | .NET 10 security updates for the API | Delete `C:\wabitex-apps\wabi-insight\install\dotnet-hosting-10.0-win.exe`, then step 13 **Do** again |
| Monthly | Database sizes (Express 2025: 50 GB per database) | `Invoke-WiSql "SELECT DB_NAME(database_id) AS Db, SUM(size) * 8 / 1024 AS MB FROM sys.master_files GROUP BY database_id ORDER BY MB DESC"` |
| Monthly | Free disk space | `Get-PSDrive C, D -ErrorAction SilentlyContinue` (keep at least 15 GB free) |
| When the deploy job warns *differs* | The deploy script changed in the repository | Step 31, parts 1 and 2 |
| Automatic | TLS certificate | win-acme renews it (scheduled task) |
| Automatic | The runner | It updates itself |

## 43. Rolling back

**Code only (no migration in between):**
- **With GitHub:** **Run workflow** (from `main`) with **ref** = the commit you want back. It's in
  `C:\wabitex-apps\wabi-insight\logs\deploy.log` (`version=`). Choose the same component and leave **migrate_database**
  off.
- **Instantly, without GitHub:** the version before the last deployment is kept on the server:

```powershell
$r = $WI.Root
Set-Content "$r\web\api\app_offline.htm" 'Updating'
do { Start-Sleep -Seconds 2; try { [IO.File]::Open("$r\web\api\WabiInsight.Api.dll", 'Open', 'ReadWrite', 'None').Dispose(); $free = $true } catch { $free = $false } } until ($free)   # the API has stopped and let go of its files
robocopy "$r\releases\previous-api" "$r\web\api" /MIR /XF appsettings.Local.json app_offline.htm /NFL /NDL /NJH /NJS
Remove-Item "$r\web\api\app_offline.htm"
robocopy "$r\releases\previous-web" "$r\web\app" /MIR /NFL /NDL /NJH /NJS
```

**Database back to before a migration:** the old code may not work with the new tables. Restore the backup that the
deployment made just before it migrated, then put the old code back.

1. Take the API offline and list the newest backups:

   ```powershell
   Set-Content (Join-Path $WI.Root 'web\api\app_offline.htm') 'Restoring'; Start-Sleep -Seconds 5      # the API stops and lets go of the database
   Get-ChildItem $WI.Backup -Directory | Where-Object Name -match '^\d{8}-\d{4}$' | Sort-Object Name -Descending | Select-Object -First 5 Name
   ```

   Pick the newest folder made **before** the deployment. The times in `deploy.log` are UTC; the folder names use the
   server's local time.

2. Restore it (replace the folder name):

   ```powershell
   $bak = Join-Path $WI.Backup '<yyyyMMdd-HHmm>\WabiInsightDB.bak'          # CHANGE: the folder you picked
   try {
       Invoke-WiSql "ALTER DATABASE [WabiInsightDB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;"
       Invoke-WiSql "RESTORE DATABASE [WabiInsightDB] FROM DISK = N'$bak' WITH REPLACE;"
   } finally {
       Invoke-WiSql "ALTER DATABASE [WabiInsightDB] SET MULTI_USER;"
   }
   Invoke-WiSql -Database WabiInsightDB "SELECT COUNT(*) AS Applied, MAX(MigrationId) AS Latest FROM dbo.__EFMigrationsHistory"   # the newest migration is the one from before
   ```

3. Put the old code back: the *instant* box above (it removes `app_offline.htm` itself), or **Run workflow** with **ref**
   and **migrate_database** off.

**Verify:** `(Invoke-WebRequest "https://$($WI.Domain)/health/ready" -UseBasicParsing).Content` reports Healthy, and you
can sign in.

## 44. Adding an organization's database to this server

**Why:** when a customer's data is to be analysed from this server, its database is copied here as a `.bak` file and
restored. Wabi Insight only ever **reads** it, through a login that can only read.

**Where:** [VPS PowerShell], then [Your computer].

**Do:**
1. Copy the customer's `.bak` file to `D:\wabi-insight\import` (create the folder; copy and paste through Remote
   Desktop works for small files, otherwise use the customer's file transfer).
2. Restore it and create a read-only login. Change the two values on top; the reader's password is generated and shown
   once — **store it in your password manager**:

   ```powershell
   . {
   $db  = 'AcmeErpDB'                                   # CHANGE: the database name on this server (letters and digits)
   $bak = 'D:\wabi-insight\import\AcmeErp.bak'          # CHANGE: the file
   $reader = ($db.ToLower() -replace '[^a-z0-9]', '') + '_reader'
   icacls (Split-Path $bak -Parent) /grant "NT Service\MSSQL`$$($WI.SqlInstance):(OI)(CI)RX" | Out-Null
   $dataPath = (Invoke-WiSql "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(260)) AS P").P
   $logPath  = (Invoke-WiSql "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(260)) AS P").P
   $moves = Invoke-WiSql "RESTORE FILELISTONLY FROM DISK = N'$bak'" | ForEach-Object {
       $folder = if ($_.Type -eq 'L') { $logPath } else { $dataPath }
       "MOVE N'$($_.LogicalName)' TO N'$(Join-Path $folder "$($db)_$($_.LogicalName).$(if ($_.Type -eq 'L') { 'ldf' } else { 'mdf' })")'"
   }
   Invoke-WiSql "RESTORE DATABASE [$db] FROM DISK = N'$bak' WITH REPLACE, $($moves -join ', ');"
   $readerPassword = New-WiSecret
   Invoke-WiSql "IF SUSER_ID(N'$reader') IS NULL CREATE LOGIN [$reader] WITH PASSWORD = N'$readerPassword', DEFAULT_DATABASE = [$db], CHECK_POLICY = ON ELSE ALTER LOGIN [$reader] WITH PASSWORD = N'$readerPassword';"
   Invoke-WiSql -Database $db "IF DATABASE_PRINCIPAL_ID(N'$reader') IS NULL CREATE USER [$reader] FOR LOGIN [$reader] ELSE ALTER USER [$reader] WITH LOGIN = [$reader]; ALTER ROLE db_datareader ADD MEMBER [$reader];"
   "Connection string (store it; it is shown once):"
   "Server=localhost,$($WI.SqlPort);Database=$db;User Id=$reader;Password=$readerPassword;Encrypt=True;TrustServerCertificate=True;"
   Remove-Variable readerPassword
   }
   ```

3. [Your computer] as that organization's administrator: **Organization Administration → Applications** → the
   application (or **New application**) → **Replace connection** → paste the connection string → **Test connection**
   (*read-only verified*) → **Approve for querying**.

**You should see:** *RESTORE DATABASE successfully processed …*, then the connection string.

**If not:** *Operating system error 5 (Access is denied)* → SQL Server can't read the folder: the `icacls` line.
*The database was backed up on a server running version …* → the customer's SQL Server is **newer** than this one:
ask for a backup from the same or an older version, or install the newer Express. *exceeds the license limit* → the
database is larger than Express allows (50 GB on 2025).

The nightly backup includes the new database by itself, unless you listed databases in step 5 (`BackupDatabases`,
a shared SQL Server): then add the new name to the `$Only = @(…)` list at the top of `backup.ps1`
(`notepad (Join-Path $WI.Backup 'backup.ps1')`) and to step 5's list, and check `backup.log` after the next run. To refresh the data later, restore a newer `.bak` with the same
box: it replaces the database and keeps the reader working (with a new password: update it with **Replace connection**).

## 45. A new VPS from the backups (disaster, or moving to another server)

**Why:** if the VPS is lost, or you move to another provider, one night's backup rebuilds everything: databases,
logins, key ring and files. This works only with a copy **off** the old server (step 42).

**You need:** the `wabiinsightdbowner` password (step 7), and from your off-server copy: one night's folder
(`<yyyyMMdd-HHmm>` with the `.bak` files, `logins.sql` and `keys.zip`) and the `files` folder.

**Do:**
1. On the new VPS, do steps **4–20** (a new Windows Server 2019 VPS), **skip 21**, then steps **22, 23, 25** and **29**
   (runner). Step 26 (certificate) needs DNS: for a **move**, do it after item 7.
2. Step **30** (the backup task).
3. Copy the night's folder into `D:\wabi-insight\backup\` (or `C:\…`) and the `files` folder into
   `D:\wabi-insight\files` (copy and paste through Remote Desktop works for small amounts; otherwise your provider's
   transfer or a network share). Then **restore** — change the folder name on the first line:

   ```powershell
   . {
   $night = Join-Path $WI.Backup '<yyyyMMdd-HHmm>'                              # CHANGE: the night's folder you copied
   Invoke-WiSql -InputFile (Join-Path $night 'logins.sql')                      # the SQL logins, with their old passwords
   $dataPath = (Invoke-WiSql "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(260)) AS P").P
   $logPath  = (Invoke-WiSql "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(260)) AS P").P
   foreach ($bak in Get-ChildItem $night -Filter '*.bak') {
       $db = $bak.BaseName
       $moves = Invoke-WiSql "RESTORE FILELISTONLY FROM DISK = N'$($bak.FullName)'" | ForEach-Object {
           $folder = if ($_.Type -eq 'L') { $logPath } else { $dataPath }
           "MOVE N'$($_.LogicalName)' TO N'$(Join-Path $folder ([IO.Path]::GetFileName($_.PhysicalName)))'"
       }
       "restoring $db"
       Invoke-WiSql "RESTORE DATABASE [$db] FROM DISK = N'$($bak.FullName)' WITH REPLACE, $($moves -join ', ');"
   }
   Expand-Archive (Join-Path $night 'keys.zip') -DestinationPath $WI.Root -Force   # the key ring of the same night
   icacls (Join-Path $WI.Root 'keys') /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' 'BUILTIN\Administrators:(OI)(CI)F' 'IIS AppPool\WabiInsightApi:(OI)(CI)M' | Out-Null
   icacls $WI.Files /grant 'IIS AppPool\WabiInsightApi:(OI)(CI)M' | Out-Null
   }
   ```

4. Step **24** (`appsettings.Local.json`) with the **same** `wabiinsightdbowner` password as before.
5. Step **31**, part 2 (deploy script) and part 3 (deployment, **migrate_database** on). **Skip step 32**: the
   administrator is in the restored database.
6. Step **34** again (it gives the Platform Insights login a new password and writes it to the settings).
7. **For a move:** switch DNS to the new VPS (step 3), wait until `nslookup` shows the new IP, do step 26, then stop the
   old server's sites (`Stop-Website WabiInsight, WabiInsightApi` on the old VPS). **[GitHub] Settings → Actions →
   Runners**: remove the old runner.

**You should see:** one `restoring …` line per database, and no red text.

**If not:** *Operating system error 5 (Access is denied)* → SQL Server can't read the backup folder: step 30's `icacls`
line. *Login failed for user 'wabiinsightdbowner'* after the deployment → step 24 used another password than before.

**Verify:** `(Invoke-WebRequest 'http://127.0.0.1:5080/health/ready' -UseBasicParsing).Content` reports **Healthy**;
sign in as the Platform Administrator with your **current** password; a demo user can ask a question (the connections
still work because the key ring came back with the databases); an old report still opens (the files came back). Then
run the full test (step 40).

## 46. Optional: the files and the backup copies in Azure Blob Storage

Files and backups stay on this server unless you move them. To keep the **files** in a private Azure Blob Storage
container (UAE North) and send a **copy of every nightly backup** to Azure automatically, follow
[DEPLOYMENT_WINDOWS_AZURE.md](DEPLOYMENT_WINDOWS_AZURE.md) on this same server:

1. Its **Part C** (steps 8–13): the storage account, the containers, the connection string, the firewall, the upload
   link, the lifecycle rule.
2. In `appsettings.Local.json` (`notepad (Join-Path $WI.Root 'web\api\appsettings.Local.json')`), change the `Storage`
   section as in its step 30, then `Restart-WebAppPool WabiInsightApi`.
3. Copy the files already on the disk into the container, once (it skips files already there):

   ```powershell
   Push-Location (Join-Path $WI.Root 'web\api'); $env:ASPNETCORE_ENVIRONMENT = 'Production'
   dotnet .\WabiInsight.Api.dll storage-check     # Storage check passed: a test file was written, read back identical and deleted.
   dotnet .\WabiInsight.Api.dll storage-copy      # Storage copy finished: … copied, … already in the container, 0 failed.
   Pop-Location
   ```

4. Its **step 36 Do** (the backup task with copies to Azure). It replaces this guide's task (same name).

**Verify:** upload a logo again; in the Azure portal → **Containers → wabi-insight-files**, a folder
`organization-logos` appears. After the next backup, `wabi-backups` lists the night's folder. Every backend deployment
then runs `storage-check` too.

## 47. Security checklist

- **Remote Desktop:** port 3389 open to your own IP only (step 2), and a long Administrator password.
- **SQL Server:** its port (1433 or 14333) closed to the internet, in the provider panel (step 2) and in Windows (step 26). The
  application uses `wabiinsightdbowner`, which owns `WabiInsightDB` only; organizations' databases are read through
  read-only logins. The `sa` password is in your password manager only.
- **Backups** hold the databases **and** the key ring that decrypts their secrets. Keep the off-server copy as private
  as the server.
- **Secrets on the VPS:** `appsettings.Local.json` (database password) is readable only by Administrators, SYSTEM and
  the API pool.
- **Runner:** belongs to this private repository only; deployments start only when someone with write access clicks
  **Run workflow**. Never use it for pull-request workflows.
- **Samples:** their passwords changed or accounts deactivated (step 39).
- **Updates:** Windows, the Hosting Bundle and SQL Server kept current (step 42).

## 48. Costs (approximate, per month)

| Item | About |
|---|---|
| Your VPS (for example 4 vCPU / 8 GB / 80 GB, Windows) | Your provider's price |
| Off-server copy of the backups | Your provider's backup service or a storage service: usually a few USD |
| SQL Server 2025 Express, Let's Encrypt, the GitHub runner | Free |
| GitHub Actions | About 10 build minutes per deployment, covered by the included minutes |

Not included: the AI provider's usage, your domain, your e-mail service.

## 49. Troubleshooting

| What you see | Likely cause | Fix |
|---|---|---|
| `HTTP Error 500.19` | An IIS module is missing | `Get-WebGlobalModule -Name AspNetCoreModuleV2, RewriteModule, ApplicationRequestRouting`; install the missing one (steps 13–15). Run `net stop was /y; net start w3svc` after the Hosting Bundle. |
| `HTTP Error 500.30 / 500.31` (the API couldn't start) | Usually the connection string or a missing .NET 10 runtime | **Event Viewer → Windows Logs → Application**, source *IIS AspNetCore Module V2*. The API's own log: `Get-Content (Get-ChildItem C:\wabitex-apps\wabi-insight\logs\wabi-insight-*.log \| Sort-Object Name \| Select-Object -Last 1) -Tail 50` |
| `HTTP Error 500.50` on `/api/…` | The server variable of step 15 is missing | Step 15 **Do** (the last three lines) |
| `404` on `/api/…`, but the site loads | ARR proxy off, or the web app's `web.config` missing | Step 15 **Verify**; deploy `frontend` again |
| `502.3` after about 2 minutes on a study | ARR timeout | Step 15: timeout `00:10:00` |
| *Login failed for user 'wabiinsightdbowner'* | Wrong password in `appsettings.Local.json`, or mixed mode off | Step 21 **Verify**; step 19 **Check**. A forgotten password: `Invoke-WiSql "ALTER LOGIN [wabiinsightdbowner] WITH PASSWORD = N'<new password>';"`, put it after `Password=` in `appsettings.Local.json` (`notepad (Join-Path $WI.Root 'web\api\appsettings.Local.json')`), then `Restart-WebAppPool WabiInsightApi` |
| *A network-related or instance-specific error* connecting to SQL | TCP off, or not on step 5's port | Step 19 |
| Side by side: your other applications can't reach their database any more | Step 19 was run on their instance | Step 5 must name `SQLEXPRESS2025` / `14333`; set the other instance's port back to what it was (step 19 with its name) |
| Deploy: the health report says `file-storage` or `key-ring` is *Unhealthy* | The API's account can't write to the files or key ring folder; the text names it | Step 23 **Do** (the `icacls` lines), then step 24 **Verify**, and run again |
| Signed out right after signing in | The site was opened with `http://` | Use `https://` |
| E-mails don't arrive | SMTP settings or outgoing port | `Invoke-WiSql -Database WabiInsightDB "SELECT TOP 10 Status, RecipientEmail, LastError, CreatedAt FROM dbo.EmailMessages ORDER BY CreatedAt DESC"`; ask the provider to allow outgoing 587/465 |
| Background e-mails stop when nobody uses the site | The API pool sleeps | Step 23 **Verify** (`AlwaysRunning`) and step 25 (`preloadEnabled`) |
| Stored connection strings fail after a restore | The key ring doesn't match the database | Restore the `keys.zip` of the **same night** as the database (step 45, item 3) |
| The backup task result is `1` | See the last line of `backup.log` | Step 30, **If not** table |
| Deploy: *Backup task finished with result …; nothing was migrated* | The backup failed; the previous API was put back | Step 30, **If not** table; then run the deployment again |
| Deploy: *did not report Healthy* or *Migration failed* | The previous API was put back | The job log shows the startup error. Applied migrations stay: restore the backup to go back (step 43). |
| Deploy: *The database is behind this release* | **migrate_database** was off | Run again with it ticked |
| Deploy waits forever on *Waiting for a runner* | The runner service is stopped | `Start-Service (Get-Content (Join-Path $WI.RunnerDir '.service') -Raw).Trim()` |
| Deploy: *The API did not release its files within 120 seconds* | A long request was running. Nothing changed. | Run again in a few minutes |
| `https://<domain>` works from your computer but times out on the VPS | The provider doesn't route the VPS to its own public address | Check from your own computer instead; the deploy script checks through `127.0.0.1` |
| Certificate step: validation fails | DNS not pointing at the VPS yet, or port 80 closed | Steps 3, 6 and 2 |
| A sample script fails with *Invalid object name 'dbo.…'* | The tables don't exist yet | The first deployment (step 31) first |

---

## Appendix A — Installing without GitHub Actions (build on the server)

Use this only if you can't use the one-click deployments (for example, nobody can add a runner to the repository). The
server then needs the build tools, and you install every new version by hand.

**Order:** steps 1–26 and [step 30](#30-nightly-backups-every-database-the-logins-the-key-ring-and-the-files), then A.1–A.4 here **instead of
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
