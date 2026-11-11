# Wabi Insight on a Windows Server 2019 VPS with Azure SQL and Azure Blob Storage — the complete beginner's guide (deployment + CI/CD)

This guide assumes **no previous knowledge**. It takes you from a fresh Windows Server 2019 VPS to a live Wabi Insight
at `https://<your domain>`, with:

- the **web app and the API** on your VPS, in IIS (Microsoft's web server, part of Windows);
- the **database** in **Azure SQL Database** (Microsoft runs it, with backups included);
- the **generated and uploaded files** (reports, exports, attachments, logos) in **Azure Blob Storage**;
- **one-click deployments** from GitHub (**Actions → Deploy (Windows)**). GitHub builds the code, and a small agent on
  your VPS installs it;
- optionally, the two **demo organizations** (Gulf Aviation Services and Wabi Bank), whose business databases stay on
  your VPS in a free SQL Server Express ([Part H](#part-h--optional-the-demo-organizations-with-their-databases-on-the-vps)).

```
 Users' browsers
      │ HTTPS (443)
      ▼
 ┌──────────── Your VPS: Windows Server 2019 ────────────────────────────────────┐
 │ IIS site "WabiInsight" ── /            → the web app (C:\wabitex-apps\wabi-insight\web\app)
 │   (URL Rewrite + ARR)  ── /api, /health → IIS site "WabiInsightApi" on 127.0.0.1:5080
 │                                            (C:\wabitex-apps\wabi-insight\web\api)
 │ Encryption key ring: C:\wabitex-apps\wabi-insight\keys
 │ GitHub Actions runner (service) ← receives the deployments you start on GitHub
 └────────────┬──────────────────────────────────────┬───────────────────────────┘
              │ SQL over TLS (port 1433)             │ HTTPS (443)
              ▼                                      ▼
   Azure SQL Database "WabiInsightDB"     Azure Storage account → private container "wabi-insight-files"
   (automatic backups, point-in-time      (reports, exports, attachments, logos; deleted files kept 14 days)
    restore)                              + container "wabi-insight-backups" (nightly key-ring copies)
```

This setup uses two providers: your VPS provider and Microsoft Azure. That's called **multi-cloud**.

**Time:** about 3–4 hours the first time. **Extra monthly cost** on top of your VPS: about USD 6–20 (see *Costs* at the
end).

## How to read this guide

- **Where a command runs** is shown at the start of each step:
  - **[Azure portal]**: the Azure website, in your browser.
  - **[GitHub]**: the GitHub website.
  - **[VPS PowerShell]**: the administrator PowerShell window on your VPS.
- Most steps have three parts:
  - **Check:** tells you whether the step is already done. If it is, skip **Do**.
  - **Do:** the actions or commands.
  - **Verify:** proves the step worked. **Don't continue until it does.**
- **Copy and paste** commands exactly. In PowerShell, paste with a **right-click**, a whole block at once, then press
  **Enter**. Lines starting with `#` are comments and are ignored.
- **Blocks between `. {` and `}`** ask you for a password or must stop at the first error. Paste the whole block,
  **including those two lines**: nothing runs until the closing `}`, then PowerShell asks its questions one at a time.
  If the prompt shows `>>` after pasting, press **Enter** once more. If such a block stops with red text, nothing after
  the error has run: fix the cause and paste the whole block again.
- Text in `<angle brackets>` is a placeholder. Replace it with your own value, without the brackets.
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
| Azure portal | `https://portal.azure.com`, the website where you create and manage Azure services. |
| Resource group | A folder in Azure that holds related services. Deleting it deletes everything inside. |
| Azure SQL server / database | The *server* is a name and a login gate (`<name>.database.windows.net`). The *database* is where the data lives. |
| Firewall rule | Which IP addresses may connect to the Azure SQL server. |
| Storage account / container / blob | The storage account is a file store; a container is a folder in it; a blob is a file. |
| Connection string | One line of text that tells the application where a service is and how to sign in. It contains a password or key, so treat it as a secret. |
| SAS URL | A link that allows limited access to a storage container (here: upload only) until a date you choose. |
| Key ring | Files that encrypt the secrets the application stores in the database. **If you lose them, those secrets can't be read.** |
| DNS A record | The setting at your domain provider that points `insight.example.com` to your VPS IP address. |
| TLS certificate | What makes the padlock and `https://` work. Here it's free, from Let's Encrypt, and renews itself. |
| CI | Continuous Integration: GitHub builds and tests every change automatically. Already active in this repository. |
| CD | Continuous Delivery: installing a version on your server. Here you start it with one click. |
| Self-hosted runner | A small GitHub agent installed on your VPS as a Windows service. It receives the deployments you start and installs them. |

## Your information sheet

Fill this in as you go. Keep the secret rows in your password manager, not in a text file.

| Item | Example | Secret? |
|---|---|---|
| VPS public IP address | `203.0.113.10` | no |
| VPS Administrator user and password | `Administrator` / … | **yes** |
| Domain for the site | `insight.example.com` | no |
| Azure SQL server name | `wabi-insight-sql-acme.database.windows.net` | no |
| Azure SQL admin login and password | `wabisqladmin` / … | **yes** |
| Database user for the application and its password | `wabiinsight_app` / … | **yes** |
| Storage account name | `wabiinsightacme` | no |
| Storage connection string | `DefaultEndpointsProtocol=https;AccountName=…;AccountKey=…` | **yes** |
| Backups container SAS URL | `https://wabiinsightacme.blob.core.windows.net/wabi-insight-backups?sv=…` | **yes** |
| First Platform Administrator e-mail and password | `support@wabitex.com` / … | **yes** |

---

# Part A — Before you start

## 1. What you need

| You need | Where to get it | Notes |
|---|---|---|
| The VPS: Windows Server 2019 **with Desktop Experience**, 64-bit | Your hosting provider's control panel shows its IP address and the Administrator password | 2 vCPU / 4 GB RAM works; 4 vCPU / 8 GB is comfortable; at least 30 GB free disk |
| A domain name and access to its DNS settings | Your domain registrar (GoDaddy, Namecheap, Cloudflare…) | For `https://` and for the links in e-mails |
| A Microsoft Azure account with a subscription | `https://azure.microsoft.com` → *Try Azure for free* or *Pay as you go* | A credit card is needed even for the free trial |
| A GitHub account with **admin** rights on the repository `mtawfig/wabi-data-harbor` | `https://github.com` | Needed to add the runner and the environment |
| A computer with a browser and a Remote Desktop app | Windows: *Remote Desktop Connection* (`mstsc`) is built in. Mac: install *Windows App* (Microsoft Remote Desktop) from the App Store. | |

The SMTP account (for e-mails) and the AI key are already in the repository's `appsettings.json` (owner decision G).
You don't need to enter them.

## 2. Open the VPS ports in your provider's control panel

Many providers have a firewall in their control panel, outside Windows. Make sure it allows:

| Port | Why | From |
|---|---|---|
| 80 (HTTP) | Certificate validation and the redirect to HTTPS | Anywhere |
| 443 (HTTPS) | The website | Anywhere |
| 3389 (Remote Desktop) | Your access | **Only your own IP address**, if the panel allows it (strongly recommended) |

Outgoing connections must be allowed: to Azure (ports 1433 and 443), GitHub (443), `api.anthropic.com` (443) and your
SMTP server (587 or 465). They usually are by default.

## 3. Point your domain at the VPS (DNS)

**Where:** your domain registrar's website.

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

Then your values. **Edit the lines marked `# CHANGE`** before pasting. Names must be unique worldwide in Azure, so add
your company name. The backup folder is on `D:` if your VPS has a second disk, otherwise on `C:`.

```powershell
$backupRoot = if (Get-PSDrive -Name D -ErrorAction SilentlyContinue) { 'D:\wabi-insight\backup' } else { 'C:\wabi-insight\backup' }
$WI = [ordered]@{
    Domain         = 'insight.example.com'                              # CHANGE: the domain from step 3
    CertEmail      = 'it@example.com'                                   # CHANGE: receives certificate expiry notices
    AdminEmail     = 'support@wabitex.com'                              # CHANGE: first Platform Administrator (a real mailbox)
    SqlServer      = 'wabi-insight-sql-acme.database.windows.net'       # CHANGE after step 8: your Azure SQL server name
    SqlDatabase    = 'WabiInsightDB'
    SqlUser        = 'wabiinsight_app'
    StorageAccount = 'wabiinsightacme'                                  # CHANGE after step 11: your storage account name
    Repo           = 'mtawfig/wabi-data-harbor'
    Root           = 'C:\wabitex-apps\wabi-insight'
    Backup         = $backupRoot
}
New-Item -ItemType Directory -Force -Path $WI.Root | Out-Null
$WI | Export-Clixml (Join-Path $WI.Root 'wabi-insight.vars.xml')
```

You'll change `SqlServer` and `StorageAccount` once you've created them in Azure. Edit the values above, paste the block
again, and the saved copy is updated. In a **new window**, load the values back with:

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
    # Letters and digits from a cryptographic generator, plus '-' and a digit so password rules are always met.
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
```

**Verify:** `$WI` prints your values, and `Get-Command New-WiSecret, Read-WiSecret` finds both functions.

## 6. Check the server and find its public IP address

**Do / Verify:**

```powershell
(Get-CimInstance Win32_OperatingSystem) | Select-Object Caption, Version, OSArchitecture   # Windows Server 2019 …, 10.0.17763, 64-bit
Get-CimInstance Win32_ComputerSystem | Select-Object NumberOfLogicalProcessors, @{n='RAM_GB';e={[math]::Round($_.TotalPhysicalMemory/1GB,1)}}
Get-PSDrive C | Select-Object @{n='Free_GB';e={[math]::Round($_.Free/1GB,1)}}            # at least 30 GB free
Invoke-RestMethod -Uri 'https://api.ipify.org' -UseBasicParsing                          # the VPS public IP: write it on your sheet
(Resolve-DnsName $WI.Domain -Type A | Where-Object Type -eq 'A' | Select-Object -First 1).IPAddress   # must be the same IP (step 3)
```

If the last two lines differ, wait for DNS (step 3) before step 28 (the certificate). You can continue with the other
steps meanwhile, but finish step 28 before the first deployment (step 33): its last check opens the site over HTTPS.

## 7. Two passwords you'll need in Azure

**[VPS PowerShell] Do:** generate them and **store both in your password manager** right away:

```powershell
"Azure SQL admin password:            $(New-WiSecret)"
"Database password for wabiinsight_app: $(New-WiSecret)"
```

They contain only letters, digits and `-`, which is safe inside connection strings. Don't use `;`, `'` or `"` in these
passwords.

---

# Part C — Create the Azure services (in your browser)

You can do Part C on your own computer or in the VPS browser. If you use the VPS, Internet Explorer's *Enhanced
Security Configuration* may block sites: open **Server Manager → Local Server → IE Enhanced Security Configuration →
Off** for Administrators, or install a modern browser.

## 8. Resource group and Azure SQL database

**Where:** [Azure portal] `https://portal.azure.com`. Sign in with your Azure account.

**Check:** in the top search box, type **SQL databases**. Is `WabiInsightDB` already listed?

**Do:**
1. Search **Resource groups** → **+ Create**:
   - **Subscription:** yours;
   - **Resource group:** `rg-wabi-insight`;
   - **Region:** the one closest to your VPS (for example **(Middle East) UAE North** if your VPS is in the UAE).
     *The same region for everything keeps the site fast.*
   - Then **Review + create** → **Create**.
2. Search **SQL databases** → **+ Create**. On the **Basics** tab:
   - **Resource group:** `rg-wabi-insight`.
   - **Database name:** `WabiInsightDB`.
   - **Server:** **Create new**:
     - **Server name:** a unique name, for example `wabi-insight-sql-<yourcompany>`. The full name becomes
       `wabi-insight-sql-<yourcompany>.database.windows.net`; write it on your sheet.
     - **Location:** the same region as the resource group.
     - **Authentication method:** **Use SQL authentication**.
     - **Server admin login:** for example `wabisqladmin`.
     - **Password / Confirm password:** the *Azure SQL admin password* from step 7.
     - Then **OK**.
   - **Want to use SQL elastic pool?** **No**.
   - **Workload environment:** **Production**.
   - **Compute + storage:** **Configure database** → **Service tier:** *Standard (DTU-based)* → **S0 (10 DTUs)**,
     **Data max size** 250 GB is fine → **Apply**. (*Basic* is cheaper, about USD 5/month, but limited to 2 GB.)
   - **Backup storage redundancy:** **Locally-redundant backup storage** (the cheapest; enough here).
3. **Networking** tab:
   - **Connectivity method:** **Public endpoint**.
   - **Allow Azure services and resources to access this server:** **No**.
   - **Add current client IP address:** **Yes** if you're using the computer you'll use later for the portal's query
     editor, otherwise **No**.
   - **Connection policy:** **Default**.
   - **Minimum TLS version:** **TLS 1.2**.
4. **Security** tab: **Enable Microsoft Defender for SQL:** **Not now** (it costs extra; you can enable it later).
5. **Additional settings** tab:
   - **Use existing data:** **None**.
   - **Collation:** leave `SQL_Latin1_General_CP1_CI_AS`.
6. **Review + create** → **Create**. Wait for *Your deployment is complete*, about 3–5 minutes.

**Verify:** **SQL databases → WabiInsightDB** shows **Status: Online**, and its **Server name** ends with
`.database.windows.net`. Put the server name in `$WI.SqlServer` (step 5) and save the values again.

## 9. Let the VPS (and only the VPS) reach the database

**Where:** [Azure portal] → **SQL servers** → your server → **Security → Networking**.

**Do:**
1. Under **Firewall rules** → **+ Add a firewall rule**:
   - **Rule name:** `vps`;
   - **Start IP** and **End IP:** both your **VPS IP** (step 6).
2. Keep **Allow Azure services and resources to access this server** **unticked**.
3. **Save**.

**Verify** ([VPS PowerShell]):

```powershell
Test-NetConnection $WI.SqlServer -Port 1433 -InformationLevel Quiet     # True
```

`False` means the VPS provider blocks outgoing port 1433, or the server name is wrong.

> If your VPS IP address ever changes, update this rule. Otherwise the site stops with "Client with IP address … is not
> allowed to access the server" (see *Troubleshooting*).

## 10. Create the application's own database user

The application shouldn't sign in as the server administrator. You create a **contained database user** that owns only
`WabiInsightDB`; it can also create the tables, which migrations need.

**Where:** [Azure portal] → **SQL databases → WabiInsightDB → Query editor (preview)**.

**Do:**
1. Sign in with **SQL server authentication**, using the admin login and password from step 8.
   - If you see *Client with IP address … is not allowed*, click the **Allowlist IP …** link it offers, then sign in again.
2. Paste this, replacing `<app password>` with the *database password for wabiinsight_app* from step 7, and click
   **Run**:

```sql
CREATE USER [wabiinsight_app] WITH PASSWORD = '<app password>';
ALTER ROLE db_owner ADD MEMBER [wabiinsight_app];
```

**Verify:** run this query; it returns one row, `wabiinsight_app  1`:

```sql
SELECT name, IS_ROLEMEMBER('db_owner', name) AS DbOwner FROM sys.database_principals WHERE name = 'wabiinsight_app';
```

> The firewall rule the query editor added is for your browser's IP. You can delete it later under **Networking**. The
> `vps` rule must stay.

## 11. Storage account for the files

**Where:** [Azure portal] → search **Storage accounts** → **+ Create**.

**Check:** is a storage account for Wabi Insight already listed?

**Do:** on the **Basics** tab:
- **Resource group:** `rg-wabi-insight`.
- **Storage account name:** 3–24 lowercase letters and digits, unique worldwide, for example `wabiinsight<yourcompany>`.
  Write it on your sheet and in `$WI.StorageAccount`.
- **Region:** the same as the database.
- **Primary service:** *Azure Blob Storage or Azure Data Lake Storage Gen 2* (if asked).
- **Performance:** **Standard**.
- **Redundancy:** **Locally-redundant storage (LRS)**, the cheapest. Choose **GRS** if you want a copy in a second
  region.

**Advanced** tab:
- **Require secure transfer for REST API operations:** ✅.
- **Allow enabling anonymous access on individual containers:** ❌ (**off**).
- **Enable storage account key access:** ✅ (the application signs in with the connection string).
- **Minimum TLS version:** **Version 1.2**.
- **Access tier:** **Hot**.

**Networking** tab: **Enable public access from all networks**. To restrict it later to your VPS IP, see the *Security
checklist*.

**Data protection** tab:
- **Enable soft delete for blobs:** ✅ **14** days.
- **Enable soft delete for containers:** ✅ **14** days.
- Leave the rest as it is.

Then **Review + create** → **Create**, and wait for *Your deployment is complete*.

**Verify:** the storage account opens and its **Overview** shows your region.

## 12. Two private containers

**Where:** [Azure portal] → your storage account → **Data storage → Containers**.

**Do:** **+ Container** twice:

| Name | Anonymous access level | Holds |
|---|---|---|
| `wabi-insight-files` | **Private (no anonymous access)** | Every file the application keeps |
| `wabi-insight-backups` | **Private (no anonymous access)** | Nightly copies of the key ring (step 32) |

**Verify:** both containers are listed with access level **Private**.

## 13. Copy the storage connection string

**Where:** [Azure portal] → storage account → **Security + networking → Access keys**.

**Do:** under **key1**, click **Show** next to **Connection string**, then the copy icon. Store it in your password
manager as *Storage connection string*.

**Verify:** it starts with `DefaultEndpointsProtocol=https;AccountName=<your account>;AccountKey=`.

## 14. Create an upload-only link (SAS) for the key-ring backups

**Where:** [Azure portal] → storage account → **Containers → wabi-insight-backups** → **Settings → Shared access tokens**.

**Do:**
- **Signing method:** **Account key** (key1).
- **Permissions:** tick only **Add**, **Create** and **Write**.
- **Start:** now. **Expiry:** two years from today. Add a calendar reminder to renew it (step 37).
- **Allowed protocols:** **HTTPS only**.
- **Generate SAS token and URL**, then copy the **Blob SAS URL**. Store it in your password manager as *Backups SAS URL*.

**Verify:** the URL starts with `https://<account>.blob.core.windows.net/wabi-insight-backups?` and contains `sp=acw`
and `sig=`.

## 15. Longer database backups (optional, recommended)

Azure SQL backs up the database automatically. By default you can restore it to any moment of the last **7 days**
(*point-in-time restore*).

**Where:** [Azure portal] → **SQL servers** → your server → **Data management → Backups → Retention policies**.

**Do:**
1. Tick `WabiInsightDB` → **Configure policies**.
2. **Point-in-time restore:** **14 days** (Standard allows up to 35).
3. Optional **long-term retention**: *Weekly backups* kept **4 weeks**.
4. **Apply**.

**Verify:** the list shows the new retention for `WabiInsightDB`.

---

# Part D — Install the software on the VPS

Run every step in **[VPS PowerShell]**, with the session settings, `$WI` and the helpers from step 5 loaded.

## 16. Windows updates

**Check:**

```powershell
Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 3 HotFixID, InstalledOn   # a recent cumulative update
$pending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
           (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
"Reboot pending: $pending"
```

**Do:**
1. Install all updates: **Start → Settings → Update & Security → Windows Update → Check for updates**, then **Install**.
2. Restart when asked: **Start → Power → Restart**.
3. Reconnect (step 4), open PowerShell as administrator, and paste the session settings, `$WI = Import-Clixml …` and the
   helpers again (step 5).

**Verify:** the **Check** block shows an update from the last month, and `Reboot pending: False`.

## 17. Time zone and clock

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

## 18. Folders

| Folder | Contents |
|---|---|
| `C:\wabitex-apps\wabi-insight\web\app` | The web app (IIS site `WabiInsight`) |
| `C:\wabitex-apps\wabi-insight\web\api` | The API (IIS site `WabiInsightApi`) and `appsettings.Local.json` (this server's settings) |
| `C:\wabitex-apps\wabi-insight\keys` | The key ring. **Back it up (step 32).** |
| `C:\wabitex-apps\wabi-insight\logs`, `releases`, `deploy`, `install`, `tools` | Logs and the deploy log, the last releases (for roll-back), the deploy script, downloaded installers, the certificate tool |
| `D:\wabi-insight\backup` (or `C:\wabi-insight\backup`) | Local copies of the key-ring backups |

The generated and uploaded files are **not** on the VPS: they go to Azure Blob Storage.

**Check:**

```powershell
$appFolders = 'web\app', 'web\api', 'keys', 'logs', 'releases', 'deploy', 'install', 'tools'
$appFolders | ForEach-Object { '{0,-48} {1}' -f (Join-Path $WI.Root $_), (Test-Path (Join-Path $WI.Root $_)) }
'{0,-48} {1}' -f $WI.Backup, (Test-Path $WI.Backup)
```

**Do:** create them, and remove the general `Users` access, because these folders will hold secrets:

```powershell
$appFolders | ForEach-Object { New-Item -ItemType Directory -Force -Path (Join-Path $WI.Root $_) | Out-Null }
New-Item -ItemType Directory -Force -Path $WI.Backup | Out-Null
foreach ($folder in $WI.Root, (Split-Path $WI.Backup -Parent)) {
    icacls $folder /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' 'BUILTIN\Administrators:(OI)(CI)F' | Out-Null
}
```

**Verify:**

```powershell
icacls $WI.Root                              # only NT AUTHORITY\SYSTEM and BUILTIN\Administrators
icacls (Split-Path $WI.Backup -Parent)       # only NT AUTHORITY\SYSTEM and BUILTIN\Administrators
```

## 19. IIS (the web server)

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

## 20. Microsoft Visual C++ runtime (x64)

PDF reports and charts use native libraries that need it.

**Check:** `(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64' -ErrorAction SilentlyContinue).Version`

**Do:**

```powershell
$vc = Get-WiDownload 'https://aka.ms/vs/17/release/vc_redist.x64.exe' 'vc_redist.x64.exe'
$p = Start-Process $vc -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru
"exit code $($p.ExitCode)"      # 0, 3010 (reboot later) or 1638 (a newer version is already installed)
```

**Verify:** the **Check** command prints a version such as `v14.4x.xxxxx`.

## 21. ASP.NET Core 10 Hosting Bundle (runs the API inside IIS)

It installs the .NET 10 runtime and the IIS module for ASP.NET Core. It must come **after** IIS (step 19).

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

## 22. IIS URL Rewrite 2.1

**Check:** `Get-WebGlobalModule -Name RewriteModule -ErrorAction SilentlyContinue`

**Do:**

```powershell
$rw = Get-WiDownload 'https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi' 'rewrite_amd64_en-US.msi'
$p = Start-Process msiexec.exe -ArgumentList '/i', "`"$rw`"", '/qn', '/norestart' -Wait -PassThru; "exit code $($p.ExitCode)"
```

**Verify:** `Get-WebGlobalModule -Name RewriteModule` returns `RewriteModule …\rewrite.dll`.

## 23. Application Request Routing 3.0 (passes `/api` to the API)

**Check:** `Get-WebGlobalModule -Name ApplicationRequestRouting -ErrorAction SilentlyContinue`

**Do:** install it, enable the proxy with a 10-minute timeout (study reports take 1–3 minutes), and allow the rewrite
rule to tell the API that the visitor used HTTPS:

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
Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' | Select-Object enabled, timeout, preserveHostHeader   # True 00:10:00 True
Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/rewrite/allowedServerVariables/add' | Select-Object name   # HTTP_X_FORWARDED_PROTO
```

## 24. Check the connections the server needs

**Verify** (each line must print `True`):

```powershell
Test-NetConnection $WI.SqlServer -Port 1433 -InformationLevel Quiet                                  # Azure SQL
Test-NetConnection "$($WI.StorageAccount).blob.core.windows.net" -Port 443 -InformationLevel Quiet   # Azure Storage
Test-NetConnection github.com -Port 443 -InformationLevel Quiet                                       # GitHub
Test-NetConnection api.anthropic.com -Port 443 -InformationLevel Quiet                                # AI provider
```

`False` on the first line means the server name in `$WI.SqlServer` is wrong, or the VPS provider blocks outgoing port
1433. `False` on the others means the provider blocks outgoing traffic: ask them to allow it. This test can't check the
Azure firewall rule (step 9) or the database password: those are first used by the first deployment (step 33), which
reports *Client with IP address … is not allowed* or *Login failed* if one of them is wrong (see step 42).

---

# Part E — Configure the application and the website

## 25. Application pools and permissions

| Pool | Site | Rights |
|---|---|---|
| `WabiInsightApi` | API | read `web\api`; modify `keys` and `logs` |
| `WabiInsightWeb` | Public site | read `web\app` |

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
# API pool: always running (background e-mails, monitoring and periodic reports), no idle shutdown, one recycle at 03:00
$apiPool = 'IIS:\AppPools\WabiInsightApi'
Set-ItemProperty $apiPool -Name startMode -Value 'AlwaysRunning'
Set-ItemProperty $apiPool -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty $apiPool -Name processModel.loadUserProfile -Value $true
Set-ItemProperty $apiPool -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
Clear-ItemProperty $apiPool -Name recycling.periodicRestart.schedule
New-ItemProperty $apiPool -Name recycling.periodicRestart.schedule -Value @{ value = '03:00:00' } | Out-Null

$root = $WI.Root
icacls "$root\web" /grant 'IIS AppPool\WabiInsightApi:(RX)' 'IIS AppPool\WabiInsightWeb:(RX)' 'IUSR:(RX)' | Out-Null
icacls "$root\web\api" /grant 'IIS AppPool\WabiInsightApi:(OI)(CI)RX' | Out-Null
icacls "$root\web\app" /grant 'IIS AppPool\WabiInsightWeb:(OI)(CI)RX' 'IUSR:(OI)(CI)RX' | Out-Null
icacls "$root\keys" /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' 'BUILTIN\Administrators:(OI)(CI)F' 'IIS AppPool\WabiInsightApi:(OI)(CI)M' | Out-Null
icacls "$root\logs" /grant 'IIS AppPool\WabiInsightApi:(OI)(CI)M' | Out-Null
```

**Verify:**

```powershell
Get-ItemProperty IIS:\AppPools\WabiInsightApi | Select-Object managedRuntimeVersion, startMode, @{n='idleTimeout';e={$_.processModel.idleTimeout}}   # '', AlwaysRunning, 00:00:00
icacls "$($WI.Root)\keys"               # SYSTEM, Administrators and IIS AppPool\WabiInsightApi only
```

## 26. This server's settings: `appsettings.Local.json`

The repository's `appsettings.json` holds the shared settings, including SMTP and the AI key.
`appsettings.Local.json`, next to it on this server, adds what is specific to this server:
- the Azure SQL connection;
- the Azure Storage connection;
- the site's address;
- the key-ring folder.

It overrides `appsettings.json`, and **deployments never overwrite or delete it**.

**Check:** `Test-Path (Join-Path $WI.Root 'web\api\appsettings.Local.json')`

**Do:** PowerShell asks for the two secrets without showing them. Paste them from your password manager:
- the **database password for `wabiinsight_app`** (step 7);
- the **storage connection string** (step 13).

```powershell
. {
$sqlPassword = Read-WiSecret 'Database password for wabiinsight_app'
$storageConnection = Read-WiSecret 'Storage connection string (key1)'
$localSettings = [ordered]@{
    ConnectionStrings = [ordered]@{
        ConfigurationDatabase = "Server=tcp:$($WI.SqlServer),1433;Initial Catalog=$($WI.SqlDatabase);User ID=$($WI.SqlUser);Password=$sqlPassword;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
    }
    Platform       = [ordered]@{ BaseUrl = "https://$($WI.Domain)" }
    Storage        = [ordered]@{
        Provider  = 'AzureBlob'
        RootPath  = (Join-Path $WI.Root 'storage')
        AzureBlob = [ordered]@{ ConnectionString = $storageConnection; ContainerName = 'wabi-insight-files'; ReadFromDiskWhenMissing = $false }
    }
    DataProtection = [ordered]@{ KeyRingPath = (Join-Path $WI.Root 'keys') }
    Logging        = [ordered]@{ File = [ordered]@{ Directory = (Join-Path $WI.Root 'logs') } }
}
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
$localSettings | ConvertTo-Json -Depth 5 | Set-Content -Path $localPath -Encoding UTF8
Remove-Variable sqlPassword, storageConnection
}
```

**Verify:** this shows the file with the secrets hidden:

```powershell
(Get-Content $localPath -Raw) -replace 'Password=[^;]*', 'Password=***' -replace 'AccountKey=[^;"]*', 'AccountKey=***'
(Get-Content $localPath -Raw | ConvertFrom-Json).Storage.Provider          # AzureBlob
```

> Don't add a `Bootstrap` section to this file: step 34 turns bootstrap on for one command only.

## 27. The two IIS sites

The API gets its own site that answers **only on this machine** (`127.0.0.1:5080`). The public site serves the web app
and passes `/api` and `/health` to it, so the browser sees one address.

**Check:**

```powershell
Get-Website | Select-Object Name, State, @{n='Bindings';e={$_.bindings.Collection.bindingInformation -join ', '}}
```

**Do:**

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

Both folders are still empty, so the sites show errors until the first deployment (step 33). That's expected.

## 28. Windows Firewall and the HTTPS certificate

**Check:**

```powershell
Get-NetFirewallRule -DisplayGroup 'World Wide Web Services (HTTP)', 'Secure World Wide Web Services (HTTPS)' -ErrorAction SilentlyContinue |
    Select-Object DisplayName, Enabled, Direction, Action
Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.DnsNameList.Unicode -contains $WI.Domain } | Select-Object Subject, NotAfter
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
   VPS, and port 80 must be open (step 2):

   ```powershell
   $release = Invoke-RestMethod 'https://api.github.com/repos/win-acme/win-acme/releases/latest' -Headers @{ 'User-Agent' = 'wabi-insight-setup' }
   $asset = $release.assets | Where-Object name -match 'x64\.trimmed\.zip$' | Select-Object -First 1
   $zip = Get-WiDownload $asset.browser_download_url $asset.name
   $wacsDir = Join-Path $WI.Root 'tools\win-acme'
   Expand-Archive $zip -DestinationPath $wacsDir -Force
   $siteId = (Get-Website -Name 'WabiInsight').Id
   & (Join-Path $wacsDir 'wacs.exe') --source iis --siteid $siteId --host $WI.Domain --emailaddress $WI.CertEmail --accepttos
   ```

   If the command reports an argument error in your win-acme version, run `& (Join-Path $wacsDir 'wacs.exe')` without
   arguments and choose **N: Create certificate (default settings)** → the site **WabiInsight**.

**Verify:**

```powershell
Get-WebBinding -Name 'WabiInsight' | Select-Object protocol, bindingInformation, sslFlags        # http *:80:<domain> and https *:443:<domain>
Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.DnsNameList.Unicode -contains $WI.Domain } | Select-Object Subject, NotAfter
Get-ScheduledTask | Where-Object TaskName -like 'win-acme*' | Select-Object TaskName, State    # the renewal task, Ready
```

---

# Part F — CI/CD: deploy from GitHub with one click

## 29. How CI and CD work in this repository

| Part | File | When it runs | What it does |
|---|---|---|---|
| **CI**, already active | `.github/workflows/ci.yml` | **Automatically** on every pull request and every merge to `main` | Builds the API, runs the unit and SQL Server integration tests, checks dependencies for known vulnerabilities, lints, tests and builds the web app, and builds the Docker images. A red ✗ on a pull request means don't merge it yet. |
| **CD** | `.github/workflows/deploy-windows.yml` (**Deploy (Windows)**) | **Only when you click Run workflow** | GitHub builds the parts you choose. The runner on your VPS installs them with `C:\wabitex-apps\wabi-insight\deploy\Deploy-WabiInsight.ps1`. |

```
 You: GitHub → Actions → Deploy (Windows) → Run workflow
   │   component: frontend+backend | backend | frontend    migrate database: on/off    run tests: on/off    ref: (optional)
   ▼
 GitHub's computers: build and test → package the API and the web app
   ▼
 The runner service on YOUR VPS (it only makes outgoing connections to GitHub; no port is opened)
   └─ Deploy-WabiInsight.ps1:
        API     : stage → keep the previous version → take the API offline → install → [key-ring backup + database
                  migrations] → back online → wait until healthy → check the Azure file storage
                  (anything fails → the previous version is put back automatically)
        web app : stage → keep the previous version → install (index.html last)
        both    : final check through IIS: https://<domain>/ answers 200 and /health/ready is Healthy
```

The same workflow serves Windows servers with a local SQL Server and this Azure setup. The deploy script sees from
`appsettings.Local.json` that the database is Azure SQL. Before migrations, it records the moment you'd restore to
(point-in-time restore) instead of making a local `.bak` file. **No server secret is stored in GitHub.**

## 30. A GitHub environment for the server

**Where:** [GitHub] → the repository → **Settings → Environments**.

**Check:** is `production-windows` listed?

**Do:**
1. **New environment** → name `production-windows` → **Configure environment**.
2. **Deployment branches and tags:** *Selected branches and tags* → **Add deployment branch or tag rule** → `main`.
3. Optional: **Required reviewers**: add yourself, to approve each deployment before it touches the server.
4. **Environment variables → Add variable:** `WI_PUBLIC_URL` = `https://<your domain>`.

On GitHub Free, private repositories don't get branch rules and reviewers; the workflow still runs without them.

**Verify:** `production-windows` is listed with the variable.

## 31. Install the runner on the VPS (as a Windows service)

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
```

On **[GitHub] Settings → Actions → Runners**, the runner shows **Idle**, with the labels `self-hosted`, `Windows`, `X64`
and `wabi-insight`.

## 32. Nightly key-ring backup to Azure

The database and the files are backed up by Azure. The **key ring** is on the VPS. This scheduled task zips it every
night and uploads it to the `wabi-insight-backups` container with the upload-only link from step 14. The deploy script
also runs this task before every database migration.

**Check:**

```powershell
Get-ScheduledTask -TaskName 'Wabi Insight backup' -ErrorAction SilentlyContinue | Select-Object TaskName, State
Test-Path (Join-Path $WI.Backup 'backup.ps1'), (Join-Path $WI.Backup 'backup-target.txt')
```

**Do:** paste the **Backups SAS URL** when asked:

```powershell
. {
$sasUrl = Read-WiSecret 'Backups SAS URL (step 14)'
Set-Content -Path (Join-Path $WI.Backup 'backup-target.txt') -Value $sasUrl.Trim() -Encoding ASCII
Remove-Variable sasUrl
$script = @'
# Nightly backup of the Wabi Insight key ring to Azure Blob Storage. Runs as SYSTEM from the "Wabi Insight backup" task.
param([string]$Keys = '__KEYS__')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
if (-not (Get-ChildItem $Keys -Force)) { exit 0 }      # nothing to copy yet: the key ring stays empty until the API first starts (step 33)
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmm')
$zip = Join-Path $PSScriptRoot "keys-$stamp.zip"
Compress-Archive -Path $Keys -DestinationPath $zip -Force
$target = (Get-Content (Join-Path $PSScriptRoot 'backup-target.txt') -Raw).Trim()
$parts = $target.Split('?', 2)
$uri = $parts[0].TrimEnd('/') + "/keys/keys-$stamp.zip?" + $parts[1]
Invoke-RestMethod -Method Put -Uri $uri -InFile $zip -ContentType 'application/zip' -Headers @{ 'x-ms-blob-type' = 'BlockBlob' } -UseBasicParsing | Out-Null
Get-ChildItem $PSScriptRoot -Filter 'keys-*.zip' | Where-Object LastWriteTime -lt (Get-Date).AddDays(-30) | Remove-Item -Force
exit 0
'@
Set-Content -Path (Join-Path $WI.Backup 'backup.ps1') -Value $script.Replace('__KEYS__', (Join-Path $WI.Root 'keys')) -Encoding UTF8
$action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$($WI.Backup)\backup.ps1`""
$trigger   = New-ScheduledTaskTrigger -Daily -At '02:30'
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName 'Wabi Insight backup' -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
}
```

The key ring is still empty at this point; it gets its first key when the API starts (step 33). Test the task after
that.

**Verify** (after step 33):

```powershell
Start-ScheduledTask -TaskName 'Wabi Insight backup'; Start-Sleep -Seconds 20
(Get-ScheduledTaskInfo -TaskName 'Wabi Insight backup').LastTaskResult             # 0
Get-ChildItem $WI.Backup -Filter 'keys-*.zip' | Select-Object -Last 1 Name, Length
```

**[Azure portal]** → storage account → **Containers → wabi-insight-backups → keys**: the same `keys-<date>.zip` is
listed.

## 33. Install the deploy script, then the first deployment

**Where:** first [GitHub], then [VPS PowerShell].

**Do, part 1:** create a short-lived GitHub token, used once to download the script:
1. **[GitHub]** your profile picture → **Settings → Developer settings → Personal access tokens → Fine-grained
   tokens → Generate new token**.
2. Fill in:
   - **Name:** `wabi-insight-deploy-script`;
   - **Expiration:** 7 days;
   - **Repository access:** *Only select repositories* → `mtawfig/wabi-data-harbor`;
   - **Permissions → Repository permissions → Contents:** **Read-only**.
3. **Generate token** and copy it.

**Do, part 2:** **[VPS PowerShell]** paste the token when asked:

```powershell
. {
$deployDir = Join-Path $WI.Root 'deploy'
$ghToken = Read-WiSecret 'GitHub token (Contents: read)'
Invoke-WebRequest -UseBasicParsing -OutFile (Join-Path $deployDir 'Deploy-WabiInsight.ps1') `
    -Uri "https://api.github.com/repos/$($WI.Repo)/contents/deploy/cicd/windows/Deploy-WabiInsight.ps1?ref=main" `
    -Headers @{ Authorization = "Bearer $ghToken"; Accept = 'application/vnd.github.raw'; 'User-Agent' = 'wabi-insight-setup' }
Remove-Variable ghToken
Unblock-File (Join-Path $deployDir 'Deploy-WabiInsight.ps1')
& (Join-Path $deployDir 'Deploy-WabiInsight.ps1') -ScriptVersion          # 4
}
```

Then delete the token: **[GitHub]** *Fine-grained tokens* → the token → **Delete**.

**Do, part 3:** the first deployment:
1. **[GitHub]** **Actions** → **Deploy (Windows)** (left list) → **Run workflow**.
2. **Use workflow from:** `main`.
3. **component:** `frontend+backend`.
4. **migrate_database:** ✅. This creates the tables in the empty Azure database.
5. **run_tests:** ✅.
6. **ref:** empty.
7. **Run workflow**, then click the new run to watch it.

The build takes 3–5 minutes on GitHub; the installation takes 1–3 minutes on your VPS. The log shows each stage
(`==> Backend: …`, `==> Database: Azure SQL … restore point …`, `==> Storage: …`, `==> Frontend: …`) and ends with
`Deployment <release> succeeded.`

**Verify** ([VPS PowerShell]):

```powershell
Get-Content (Join-Path $WI.Root 'logs\deploy.log') -Tail 3                                   # … succeeded release=…
(Invoke-WebRequest 'http://127.0.0.1:5080/health/ready' -UseBasicParsing).Content          # "status":"Healthy" … configuration-database, database-schema, file-storage, key-ring … Healthy
(Invoke-WebRequest "https://$($WI.Domain)/health/ready" -UseBasicParsing).Content          # Healthy, through IIS
Invoke-WebRequest 'http://127.0.0.1:5080/api/auth/csrf' -UseBasicParsing -Headers @{ 'X-Forwarded-Proto' = 'https' } | Out-Null   # first use of the key ring (the header stands for IIS: the API answers this over HTTPS only)
Get-ChildItem (Join-Path $WI.Root 'keys')                                                  # key-<id>.xml: the key ring now exists
```

Now run the **Verify** of step 32 (key-ring backup).

## 34. Create the first Platform Administrator (once)

**[VPS PowerShell] Do:**
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

**Verify:** the output includes `Bootstrap completed. AdministratorId=…`, and `Get-ChildItem Env: | Where-Object Name
-like 'Bootstrap__*'` prints nothing.

## 35. Go-live checks (in your browser)

1. Open `https://<your domain>`. The landing page loads with a padlock, and **العربية** switches the layout to right to
   left.
2. **Sign in** with the administrator e-mail and the first password. You're asked to change the password; do it.
3. **Platform → System Health:** the database and the API are healthy.
4. **Platform → AI providers:** the default Anthropic provider is listed, and **Test** succeeds.
5. Create an **organization** with a real e-mail address for its administrator. The **temporary password e-mail**
   arrives, which proves e-mail works.
6. Sign in as that organization administrator in a private window, change the password, and upload an **organization
   logo** (**Settings**). **[Azure portal]** → storage account → **Containers → wabi-insight-files**: a folder
   `organization-logos` appears. This proves the files go to Azure.
7. Register an application (your organization's database, read-only) and ask a question on **Ask Wabi Insight**. For
   the demo organizations and their databases, see Part H (steps 43–48).

---

# Part G — Running it day to day

## 36. Everyday deployments

**[GitHub] Actions → Deploy (Windows) → Run workflow**, with `main`:

| What changed | component | migrate_database |
|---|---|---|
| Only the web app (screens, texts, translations) | `frontend` | off |
| API code without a new migration | `backend` | off |
| A change that adds a migration (new files under `src/WabiInsight.Infrastructure/Persistence/Migrations/`) | `frontend+backend` | **on** |
| Not sure | `frontend+backend` | on (an empty migration step is harmless) |

`C:\wabitex-apps\wabi-insight\web\api\deploy-info.json` shows which commit is live.

## 37. Regular upkeep

| How often | What | How |
|---|---|---|
| Monthly | Windows updates | Step 16 (restart after) |
| Monthly | .NET 10 security updates for the API | Re-run step 21's **Do** with a fresh download: delete `C:\wabitex-apps\wabi-insight\install\dotnet-hosting-10.0-win.exe` first |
| When the deploy job warns "differs" | The deploy script changed in the repository | Step 33, part 1 and 2 |
| Before the SAS link expires (2 years) | The key-ring backup link | Step 14 again, then paste the new link into `backup-target.txt` (step 32 **Do**, first three lines) |
| Monthly | Check the key-ring backups exist | **[Azure portal]** → `wabi-insight-backups/keys`: recent files |
| Monthly | Azure costs | **[Azure portal]** → **Cost Management → Budgets**: create a monthly budget with an e-mail alert |
| Automatic | TLS certificate | win-acme renews it (scheduled task) |
| Automatic | The runner | It updates itself |

## 38. Rolling back

- **Code only (no migration in between):**
  1. **Run workflow** with **ref** = the commit you want back. It's in `C:\wabitex-apps\wabi-insight\logs\deploy.log`
     (`version=`) or in an earlier run's summary.
  2. Choose the same component and leave **migrate_database** off.
- **Instantly, without GitHub:** the previous version is kept on the server:

```powershell
$r = $WI.Root
Set-Content "$r\web\api\app_offline.htm" 'Updating'
do { Start-Sleep -Seconds 2; try { [IO.File]::Open("$r\web\api\WabiInsight.Api.dll", 'Open', 'ReadWrite', 'None').Dispose(); $free = $true } catch { $free = $false } } until ($free)   # the API has stopped and let go of its files
robocopy "$r\releases\previous-api" "$r\web\api" /MIR /XF appsettings.Local.json app_offline.htm /NFL /NDL /NJH /NJS
Remove-Item "$r\web\api\app_offline.htm"
robocopy "$r\releases\previous-web" "$r\web\app" /MIR /NFL /NDL /NJH /NJS
```

- **Database back to before a migration:** Azure restores into a **new** database; then you point the application at it.
  1. **[Azure portal]** → **SQL databases → WabiInsightDB** → **Restore**.
     - **Point in time:** the moment printed by the deploy log (`restore point …`) or the run summary.
     - **Database name:** for example `WabiInsightDB-restored`.
     - **Review + create**. Wait until it's **Online**.
  2. **[VPS PowerShell]** point the application at the restored database:

```powershell
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
(Get-Content $localPath -Raw).Replace("Initial Catalog=$($WI.SqlDatabase);", 'Initial Catalog=WabiInsightDB-restored;') | Set-Content $localPath -Encoding UTF8
Restart-WebAppPool 'WabiInsightApi'
```

  3. Deploy the older code version (first bullet).
  4. Save the new name: `$WI.SqlDatabase = 'WabiInsightDB-restored'; $WI | Export-Clixml (Join-Path $WI.Root 'wabi-insight.vars.xml')`
     (don't paste the step 5 block again: it would put the old name back).
  5. Once everything works, delete the old database in the portal. The `wabiinsight_app` user and the firewall rule come
     with the restore, so nothing else changes.

## 39. Restoring after a disaster

| Lost | Restore from |
|---|---|
| The database | **[Azure portal]** point-in-time restore (step 38), within the retention you set (step 15) |
| Files (reports, attachments, logos) | Deleted files: **[Azure portal]** → container → **Show deleted blobs** → select → **Undelete** (within 14 days) |
| The key ring (VPS lost or rebuilt) | **[Azure portal]** → `wabi-insight-backups/keys` → download the newest zip → on the new VPS, unpack it into `C:\wabitex-apps\wabi-insight` (it contains the `keys` folder) **before** the first start of the API |
| The whole VPS | A new Windows Server 2019 VPS: steps 4–6 and 16–33. For step 33, restore the key ring as above first, and leave **migrate_database** off. With a new IP address, also update the SQL firewall rule (step 9), the DNS record (step 3) and the provider's port rules (step 2). |

## 40. Security checklist

- **Remote Desktop:** port 3389 open to your own IP only (step 2), and a long Administrator password.
- **Database:**
  - The Azure SQL firewall lists only `vps`, plus your browser's IP while you use the query editor. Delete the browser
    rule afterwards.
  - The application uses `wabiinsight_app`, not the server admin.
- **Storage:**
  - Both containers are **private**.
  - The backups link can only **upload**, not read or delete.
  - Optional: **Storage account → Networking → Enabled from selected virtual networks and IP addresses** → add your VPS
    IP, to accept only the VPS.
- **Secrets on the VPS:**
  - `appsettings.Local.json` and `backup-target.txt` hold secrets. Only Administrators, SYSTEM and the API pool can
    read them (step 18).
  - Never e-mail them or put them in GitHub.
- **Runner:**
  - The runner belongs to this private repository only, and deployments start only when someone with write access
    clicks **Run workflow**.
  - Never use the self-hosted runner for pull-request workflows; CI runs on GitHub's own computers.
- **Updates:** keep Windows and the Hosting Bundle updated (step 37).
- **Demo databases (Part H):** port 1433 stays closed in Windows Firewall and the provider's panel; the applications
  use the read-only `…_reader` logins; change the sample passwords (step 48).

## 41. Costs (approximate, per month)

| Item | Size | About |
|---|---|---|
| Azure SQL Database | S0 (Basic: about USD 5, 2 GB) | USD 15 |
| Point-in-time and long-term backup storage | Small database | USD 0–2 |
| Azure Storage | LRS, a few GB, few operations | under USD 1 |
| Data leaving Azure (file downloads to the VPS) | First 100 GB/month free | usually USD 0 |
| GitHub Actions | about 10 build minutes per deployment | covered by the included minutes |
| Your VPS | Unchanged | — |
| SQL Server Express for the demo databases (Part H, optional) | Free edition, about 1.5 GB RAM and 3 GB disk on the VPS | USD 0 |

## 42. Troubleshooting

| What you see | Likely cause | Fix |
|---|---|---|
| Health check or deploy log: *Client with IP address 'x.x.x.x' is not allowed to access the server* | The VPS IP changed, or the `vps` firewall rule is missing | Step 9 with the IP shown in the message |
| *Login failed for user 'wabiinsight_app'* | Wrong password in `appsettings.Local.json`, or step 10 not done | Repeat step 10's **Verify**, then step 26 with the right password, then `Restart-WebAppPool WabiInsightApi` |
| `Test-NetConnection … -Port 1433` is `False` | The VPS provider blocks outgoing port 1433 | Ask the provider to allow outgoing TCP 1433 to `*.database.windows.net` |
| Deploy log: *Storage check failed* | Wrong or rotated storage key, or a missing container | Step 13 again (copy key1's connection string), step 26, check the container `wabi-insight-files` exists (step 12) |
| `HTTP Error 500.30 / 500.31` | The API couldn't start | **Event Viewer → Windows Logs → Application**, source *IIS AspNetCore Module V2*: the error is there. Usually the connection string or the .NET runtime (step 21). |
| `HTTP Error 500.19` | An IIS module is missing | `Get-WebGlobalModule -Name AspNetCoreModuleV2, RewriteModule, ApplicationRequestRouting`; install the missing one (steps 21–23) |
| `HTTP Error 500.50` on `/api/…` | Step 23's server variable is missing | Step 23 **Do** (last three lines) |
| `502.3` after about 2 minutes on a report | ARR timeout | Step 23: timeout `00:10:00` |
| Signed out right after signing in | The site was opened with `http://` | Use `https://`; the session cookie is HTTPS-only |
| Pages are slow (several seconds) | The VPS and Azure SQL are far apart | Use an Azure region close to the VPS (move the database: point-in-time restore into a server in the new region, or export/import) |
| Deploy waits forever on *Waiting for a runner* | The runner service is stopped | `Get-Service 'actions.runner.*' \| Start-Service` |
| The deploy job warns the script *differs* | The repository has a newer deploy script | Step 33, parts 1 and 2 |
| Key-ring backup task result is not 0 | The SAS link expired or is wrong | Step 14 again, and the first three lines of step 32 **Do** |
| E-mails don't arrive | SMTP settings or outgoing port | **Platform → System Health** and **E-mail** pages; check the SMTP port is allowed out (step 2) |

# Part H — Optional: the demo organizations, with their databases on the VPS

Wabi Insight comes with two fictional demo organizations, each with its own business databases:

| Organization | Databases | Sign-in users |
|---|---|---|
| **Gulf Aviation Services** (`GAS`): flights, HR/payroll, procurement | `FlightOperationsDB`, `HumanResourcesDB`, `ProcurementStoresDB` | 4: [`scripts/sql/samples/README.md`](../scripts/sql/samples/README.md) |
| **Wabi Bank** (`WBK`): personnel, core banking, finance, digital banking, IT help desk | `WabiBankPersonnelDB`, `WabiBankCoreBankingDB`, `WabiBankFinanceDB`, `WabiBankDigitalDB`, `WabiBankITDB` | 8: [`scripts/sql/samples/wabi-bank/README.md`](../scripts/sql/samples/wabi-bank/README.md) |

In this setup:
- the **demo business databases** live in a free **SQL Server Express on your VPS**. Their data never goes to Azure;
- the **demo organizations** (their users, roles, applications, catalogue and permissions) go into the application
  database in **Azure SQL**, next to your real organizations.

```
 Your VPS ─────────────────────────────────────────────────────────────┐
 │ IIS → the API ── asks questions on ──► SQL Server Express (localhost,1433, not reachable from outside)
 │                                          the 8 demo business databases, read through read-only logins
 └────────┬───────────────────────────────────────────────────────────┘
          │ users, roles, applications, catalogue, history (as before)
          ▼
   Azure SQL Database (the application database)
```

**Why the extra copy steps (45–47).** The scripts that add the demo organizations (`04-seed-…` and
`wabi-bank/06-seed-…`) read the demo databases while they write the application database, so all of them must be on the
**same** SQL Server. Azure SQL can't read databases on your VPS. So, once, during a short maintenance window:

1. the application database is copied from Azure to the VPS;
2. the two scripts run there, next to the demo databases;
3. the result goes back to Azure as a **new** database, `WabiInsightDB-demo`, and the application switches to it.

Your existing database isn't changed. If anything fails before the switch, the site simply continues on it.

**When:** any time after the first deployment (step 33) and the first Platform Administrator (step 34). The site is
offline for about 10–20 minutes during steps 46–47. **Cost:** none (SQL Server Express is free). The VPS needs about
**1.5 GB more RAM** and **3 GB more disk**.

## 43. SQL Server Express on the VPS (for the demo databases only)

Run in **[VPS PowerShell]** with the session settings, `$WI` and the helpers from step 5.

**Do, part 1:** add the SQL helper to your session. **Paste it into every new window for Part H**, next to the step 5
helpers:

```powershell
function Invoke-WiSql {
    # Runs SQL on the local SQL Server Express (demo databases) as your Windows administrator account.
    [CmdletBinding()]
    # -Tables shows every result of a script as its own table (the sample scripts return several).
    param([string]$Query, [string]$InputFile, [string]$Database = 'master', [switch]$Tables)
    Import-Module SqlServer -Verbose:$false
    $p = @{ ServerInstance = 'localhost,1433'; Database = $Database; TrustServerCertificate = $true; QueryTimeout = 600; DisableVariables = $true; ErrorAction = 'Stop' }
    if ($InputFile) { $p.InputFile = $InputFile } else { $p.Query = $Query }
    if ($Tables) { Invoke-Sqlcmd @p -OutputAs DataTables | ForEach-Object { $_ | Format-Table -AutoSize | Out-String -Width 250 } }
    else { Invoke-Sqlcmd @p }
}
```

**Check:**

```powershell
Get-Service -Name 'MSSQL$*' -ErrorAction SilentlyContinue | Select-Object Name, Status, StartType
```

If `MSSQL$SQLEXPRESS` is already listed, skip part 2 and do part 3 on it.

**Do, part 2:** download and install SQL Server 2025 Express (5–15 minutes; it shows nothing until it ends).

1. Generate the `sa` password and **store it in your password manager** (it's the emergency administrator of this local
   SQL Server; your Windows administrator account is its everyday administrator):

   ```powershell
   "sa password for the VPS SQL Server Express: $(New-WiSecret)"
   ```

2. Download and install:

   ```powershell
   . {
   $ssei = Get-WiDownload 'https://go.microsoft.com/fwlink/?linkid=2216019' 'SQL2025-SSEI-Expr.exe'
   $media = Join-Path $WI.Root 'install\sql'
   New-Item -ItemType Directory -Force -Path $media | Out-Null
   Start-Process $ssei -ArgumentList '/ACTION=Download', "/MEDIAPATH=$media", '/MEDIATYPE=Core', '/QUIET' -Wait
   $sa = Read-WiSecret 'sa password'
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
   "setup exit code $($p.ExitCode)"      # 0 = success, 3010 = success, restart the VPS first
   Remove-Variable sa
   }
   ```

   - If no `SQLEXPR*.exe` appears in `C:\wabitex-apps\wabi-insight\install\sql`: double-click
     `install\SQL2025-SSEI-Expr.exe`, choose **Download Media → Express Core**, save it to that folder, and run the
     block again from `$sa = …`.
   - **Exit code 3010:** restart the VPS, reconnect, and paste the session settings, `$WI` and the helpers again.
   - **Any other code:** read `C:\Program Files\Microsoft SQL Server\*\Setup Bootstrap\Log\Summary.txt`.

**Do, part 3:** a fixed port 1433, mixed-mode sign-in (the demo applications sign in with SQL logins), and the
PowerShell `SqlServer` module:

```powershell
$instanceId = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').SQLEXPRESS
$sqlKey = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\MSSQLServer"
Set-ItemProperty $sqlKey -Name LoginMode -Value 2
Set-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp" -Name Enabled -Value 1
Set-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp\IPAll" -Name TcpDynamicPorts -Value ''
Set-ItemProperty "$sqlKey\SuperSocketNetLib\Tcp\IPAll" -Name TcpPort -Value '1433'
Restart-Service 'MSSQL$SQLEXPRESS' -Force
Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force | Out-Null
Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
Install-Module -Name SqlServer -Scope AllUsers -AllowClobber -Force
```

**Verify:**

```powershell
Get-Service 'MSSQL$SQLEXPRESS' | Select-Object Status, StartType                       # Running, Automatic
Invoke-WiSql "SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(64)) AS Edition, CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int) AS WindowsAuthOnly"
# Express Edition (64-bit)   0
Get-NetFirewallProfile | Select-Object Name, Enabled                                   # Domain, Private, Public: all True
Get-NetFirewallRule -Enabled True -Direction Inbound -Action Allow -ErrorAction SilentlyContinue | Get-NetFirewallPortFilter |
    Where-Object { $_.LocalPort -contains '1433' }                                       # (nothing): no rule opens port 1433
```

Then prove it from outside. **On your own computer** (not the VPS), in PowerShell:

```powershell
Test-NetConnection 203.0.113.10 -Port 1433 -InformationLevel Quiet     # use your VPS IP address; must print False: the internet can't reach SQL Server
```

If it prints `True`, port 1433 is open: close it in your provider's panel (step 2) and turn Windows Firewall on
(`Set-NetFirewallProfile -All -Enabled True`) before you continue. Only the API, on this same VPS, uses that port, and
the demo logins have well-known sample passwords.

## 44. Create the demo databases

**Check:**

```powershell
Invoke-WiSql "SELECT name FROM sys.databases WHERE name IN (N'FlightOperationsDB', N'HumanResourcesDB', N'ProcurementStoresDB', N'WabiBankPersonnelDB', N'WabiBankCoreBankingDB', N'WabiBankFinanceDB', N'WabiBankDigitalDB', N'WabiBankITDB')"
```

**Do, part 1:** download the scripts. Create a GitHub token exactly as in step 33, part 1 (Contents: read-only, 7 days),
paste it when asked, and delete it on GitHub afterwards:

```powershell
. {
$samples = Join-Path $WI.Root 'install\samples'
New-Item -ItemType Directory -Force -Path (Join-Path $samples 'wabi-bank') | Out-Null
$sampleFiles = '01-create-flightoperationsdb.sql', '02-create-humanresourcesdb.sql', '03-create-procurementstoresdb.sql', '04-seed-wabiinsightdb.sql',
               'wabi-bank\01-create-wabibankpersonneldb.sql', 'wabi-bank\02-create-wabibankcorebankingdb.sql', 'wabi-bank\03-create-wabibankfinancedb.sql',
               'wabi-bank\04-create-wabibankdigitaldb.sql', 'wabi-bank\05-create-wabibankitdb.sql', 'wabi-bank\06-seed-wabiinsightdb.sql', 'wabi-bank\07-check-consistency.sql'
$ghToken = Read-WiSecret 'GitHub token (Contents: read)'
foreach ($f in $sampleFiles) {
    $repoPath = 'scripts/sql/samples/' + $f.Replace('\', '/')
    Invoke-WebRequest -UseBasicParsing -OutFile (Join-Path $samples $f) `
        -Uri "https://api.github.com/repos/$($WI.Repo)/contents/$($repoPath)?ref=main" `
        -Headers @{ Authorization = "Bearer $ghToken"; Accept = 'application/vnd.github.raw'; 'User-Agent' = 'wabi-insight-setup' }
}
Remove-Variable ghToken
# Save as UTF-8 with a byte-order mark, so the Arabic names are read correctly.
foreach ($f in $sampleFiles) {
    $path = Join-Path $samples $f
    [IO.File]::WriteAllText($path, [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8), (New-Object Text.UTF8Encoding $true))
}
Get-ChildItem $samples -Recurse -File | Select-Object Name, Length          # 11 files, none of them 0 bytes
}
```

**Do, part 2:** create the eight databases and their logins (about 5 minutes; the Wabi Bank Core Banking script alone
takes about 2):

```powershell
$createFiles = $sampleFiles | Where-Object { $_ -match '\\?0[1-5]-create-' }
foreach ($f in $createFiles) {
    "== $f"
    Invoke-WiSql -InputFile (Join-Path $samples $f) -Tables
}
```

Each script creates its database, an owner login and a **read-only** login (`…_reader`), and loads the data.

**Verify:**

```powershell
# The eight databases exist (the Check query lists eight rows):
Invoke-WiSql "SELECT name FROM sys.databases WHERE name LIKE N'%DB' AND database_id > 4 ORDER BY name"
# Wabi Bank's cross-system check: the last line must show 0 failed checks.
Invoke-WiSql -InputFile (Join-Path $samples 'wabi-bank\07-check-consistency.sql') -Tables | Select-Object -Last 1
```

## 45. Prepare the copy tools

**Do:**

1. **SqlPackage** (Microsoft's tool that copies a whole database into one `.bacpac` file and back):

   ```powershell
   $zip = Get-WiDownload 'https://aka.ms/sqlpackage-windows' 'sqlpackage-windows.zip'
   Expand-Archive $zip -DestinationPath (Join-Path $WI.Root 'tools\sqlpackage') -Force
   ```

2. Let the local SQL Server accept the application's database user. In Azure, `wabiinsight_app` (step 10) is a
   *contained* user that lives inside the database; the local SQL Server must allow such users to receive the copy:

```powershell
Invoke-WiSql "EXEC sp_configure 'contained database authentication', 1; RECONFIGURE;"
```

**Verify:**

```powershell
Test-Path (Join-Path $WI.Root 'tools\sqlpackage\SqlPackage.exe')                                                       # True
Invoke-WiSql "SELECT value_in_use FROM sys.configurations WHERE name = 'contained database authentication'"            # 1
Invoke-WiSql "SELECT name FROM sys.databases WHERE name = N'WabiInsightDB'"                                              # (nothing)
```

The last line must be empty: the local copy is created under that name in step 46. If a `WabiInsightDB` is listed from
an earlier attempt, remove it first: `Invoke-WiSql "DROP DATABASE [WabiInsightDB]"`.

## 46. Add the demo organizations to the application database (maintenance window)

**Before you start:** tell your users the site will be offline for about 20 minutes, and have the **Azure SQL admin login
and password** (step 8) ready.

**Do:** paste this whole block, from `. {` to `}`. It stops at the first error: in that case the site stays offline on
the **unchanged** database. Read the message, then bring the site back with
`Remove-Item (Join-Path $WI.Root 'web\api\app_offline.htm')` before you try again. Continue with step 47 **only** when
the last line printed is `Copied. Continue with step 47.`

```powershell
. {
$sp = Join-Path $WI.Root 'tools\sqlpackage\SqlPackage.exe'
$work = Join-Path $WI.Backup 'demo-seed'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$adminLogin = Read-Host 'Azure SQL admin login (step 8)'
$adminPassword = Read-WiSecret 'Azure SQL admin password (step 8)'
$azure = "Server=tcp:$($WI.SqlServer),1433;User ID=$adminLogin;Password=$adminPassword;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"
$local = 'Server=localhost,1433;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;'

# 1. The site goes offline, so nothing changes in the database while it is copied.
Set-Content (Join-Path $WI.Root 'web\api\app_offline.htm') 'Wabi Insight is being updated. Please try again in a few minutes.'
Start-Sleep -Seconds 5

# 2. Copy the application database from Azure to the VPS (several minutes).
& $sp /Action:Export /SourceConnectionString:"$($azure)Initial Catalog=$($WI.SqlDatabase);" /TargetFile:"$work\from-azure.bacpac"
if ($LASTEXITCODE -ne 0) { throw 'Export from Azure failed: see the messages above.' }
& $sp /Action:Import /SourceFile:"$work\from-azure.bacpac" /TargetConnectionString:"$($local)Initial Catalog=WabiInsightDB;"
if ($LASTEXITCODE -ne 0) { throw 'Import on the VPS failed: see the messages above.' }

# 3. Add the two demo organizations, next to the demo databases.
foreach ($f in '04-seed-wabiinsightdb.sql', 'wabi-bank\06-seed-wabiinsightdb.sql') {
    "== $f"
    Invoke-WiSql -InputFile (Join-Path $samples $f) -Tables
}

# 4. Copy the result back to Azure as a NEW database (several minutes). Your current database is not touched.
& $sp /Action:Export /SourceConnectionString:"$($local)Initial Catalog=WabiInsightDB;" /TargetFile:"$work\to-azure.bacpac"
if ($LASTEXITCODE -ne 0) { throw 'Export on the VPS failed: see the messages above.' }
& $sp /Action:Import /SourceFile:"$work\to-azure.bacpac" /TargetConnectionString:"$($azure)Initial Catalog=WabiInsightDB-demo;" /p:DatabaseEdition=Standard /p:DatabaseServiceObjective=S0
if ($LASTEXITCODE -ne 0) { throw 'Import into Azure failed: see the messages above.' }
'Copied. Continue with step 47.'
}
```

- `$samples` comes from step 44. In a new window, set it again first:
  `$samples = Join-Path $WI.Root 'install\samples'`.
- If you chose another database size than S0 in step 8, change `S0` in the last import line (and `Standard` to `Basic`
  for Basic).
- The two seed scripts end by listing the users and the applications they created, and print *read-only connection
  strings*: ignore their `Server=` part, step 48 gives the one to use.

**Verify** ([Azure portal] → **SQL databases**): `WabiInsightDB-demo` is listed next to `WabiInsightDB`, status
**Online**.

## 47. Switch the application to the new database

The site is still offline. **Do**, in the same window (it reuses `$adminLogin` and `$adminPassword`):

1. Make sure the application's database user can sign in to the new database. Paste the *database password for
   wabiinsight_app* (step 7) when asked:

```powershell
. {
$appPassword = Read-WiSecret 'Database password for wabiinsight_app (step 7)'
Import-Module SqlServer -Verbose:$false
$userSql = @"
IF DATABASE_PRINCIPAL_ID(N'wabiinsight_app') IS NULL CREATE USER [wabiinsight_app] WITH PASSWORD = '$appPassword';
ELSE ALTER USER [wabiinsight_app] WITH PASSWORD = '$appPassword';
ALTER ROLE db_owner ADD MEMBER [wabiinsight_app];
SELECT name, IS_ROLEMEMBER('db_owner', name) AS DbOwner FROM sys.database_principals WHERE name = 'wabiinsight_app';
"@
Invoke-Sqlcmd -ServerInstance "tcp:$($WI.SqlServer),1433" -Database 'WabiInsightDB-demo' -Username $adminLogin -Password $adminPassword -Query $userSql -ErrorAction Stop
Remove-Variable appPassword, adminPassword
# wabiinsight_app   1
}
```

2. Point the application at it, and bring the site back:

```powershell
. {
$localPath = Join-Path $WI.Root 'web\api\appsettings.Local.json'
$text = (Get-Content $localPath -Raw).Replace("Initial Catalog=$($WI.SqlDatabase);", 'Initial Catalog=WabiInsightDB-demo;')
if ($text -notmatch 'Initial Catalog=WabiInsightDB-demo;') { throw "The database name was not found in $localPath; nothing was changed. Check 'Initial Catalog=' in that file." }
Set-Content $localPath -Value $text -Encoding UTF8
Restart-WebAppPool 'WabiInsightApi'
Remove-Item (Join-Path $WI.Root 'web\api\app_offline.htm')
$WI.SqlDatabase = 'WabiInsightDB-demo'
$WI | Export-Clixml (Join-Path $WI.Root 'wabi-insight.vars.xml')
}
```

**Verify:**

```powershell
(Invoke-WebRequest "https://$($WI.Domain)/health/ready" -UseBasicParsing).Content          # "status":"Healthy"
```

Then **[Browser]** sign in as your Platform Administrator: **Platform → Organizations** lists your organizations **and**
*Gulf Aviation Services* and *Wabi Bank*.

**Afterwards (once everything works, for example the next day):**
- **[Azure portal]** → **SQL databases → WabiInsightDB** (the old one) → **Delete**. Azure keeps its backups for its
  retention period (**SQL servers → your server → Deleted databases**).
- If you set longer backups in step 15, set them again on `WabiInsightDB-demo` (they belong to each database).
- Remove the local working copy and the copy files, which contain your users' data:

```powershell
Invoke-WiSql "ALTER DATABASE [WabiInsightDB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [WabiInsightDB];"
Remove-Item (Join-Path $WI.Backup 'demo-seed') -Recurse -Force
```

From now on, wherever this guide says `WabiInsightDB` in the Azure portal (for example the roll-back in step 38), use
`WabiInsightDB-demo`.

## 48. Connect the demo applications and try them

The applications' connection strings are encrypted with the API's key ring, so they can't be set by a script: each
organization's administrator enters them once in the app.

**[Browser] Do**, for each organization:

1. Sign in as its administrator with the initial password `Wabi#Sample#2026`, and change the password when asked:
   - Gulf Aviation Services: `faisal.alshamsi@gulfaviation.example`;
   - Wabi Bank: `w1001@wabibank.example`.
2. **Organization Administration → Applications**. For each application:
   - **Replace connection** with the line below for its database, and the reader password from the README table
     ([Gulf Aviation](../scripts/sql/samples/README.md), [Wabi Bank](../scripts/sql/samples/wabi-bank/README.md));
   - **Test connection**: it must report *read-only verified*;
   - **Approve for querying**.

```
Server=localhost,1433;Database=<database>;User Id=<reader login>;Password=<reader password>;Encrypt=True;TrustServerCertificate=True;
```

| Organization | Application database | Reader login |
|---|---|---|
| Gulf Aviation Services | `FlightOperationsDB` | `flightops_reader` |
| Gulf Aviation Services | `HumanResourcesDB` | `hr_reader` |
| Gulf Aviation Services | `ProcurementStoresDB` | `procurement_reader` |
| Wabi Bank | `WabiBankPersonnelDB` | `wbpersonnel_reader` |
| Wabi Bank | `WabiBankCoreBankingDB` | `wbcore_reader` |
| Wabi Bank | `WabiBankFinanceDB` | `wbfinance_reader` |
| Wabi Bank | `WabiBankDigitalDB` | `wbdigital_reader` |
| Wabi Bank | `WabiBankITDB` | `wbit_reader` |

`localhost` is right: the API runs on this VPS, next to SQL Server Express.

**Verify:** sign in as a Management user (`khalid.alsuwaidi@gulfaviation.example` or `w1002@wabibank.example`, initial
password as above), and ask on **Ask Wabi Insight**, for example *"Total deposits by branch"* on Core Banking. The
READMEs list more questions for each user.

**Then:**
- **Change the sample passwords** before anyone else uses the server: the users' (each changes it at first sign-in) and
  the SQL logins' (`Invoke-WiSql "ALTER LOGIN [<login>] WITH PASSWORD = '<new password>'"`). After changing a reader
  password, **Replace connection** again for that application.
- **Backups:** the demo databases are **not** in any backup. That's fine for demo data: step 44 rebuilds them. If you
  change them yourself, back them up like any SQL Server Express database.

| What you see | Likely cause | Fix |
|---|---|---|
| SqlPackage: *Client with IP address … is not allowed* | The `vps` firewall rule (step 9) is missing or the IP changed | Step 9, then step 46 again |
| SqlPackage import on the VPS: *contained database authentication* | Step 45, part 2 not done | Step 45, then step 46 again |
| SqlPackage import on the VPS: *database already exists* | A copy from an earlier attempt | `Invoke-WiSql "DROP DATABASE [WabiInsightDB]"`, then step 46 again |
| SqlPackage import into Azure: *database already exists* | `WabiInsightDB-demo` from an earlier attempt | Delete it in the portal (it isn't in use yet), then step 46 again |
| Seed script: *is not migrated to the current version* | The copied database is older than the code | Run **Deploy (Windows)** with `migrate_database` on, then step 46 again |
| *Test connection* fails with *Login failed* | Wrong reader password, or mixed-mode sign-in is off | Check the README password; step 43, part 3 |
| *Test connection* can't reach the server | SQL Server Express is stopped | `Start-Service 'MSSQL$SQLEXPRESS'` |

---

## Appendix: moving a server that already runs Wabi Insight with SQL Server Express

If your VPS already runs Wabi Insight with a local SQL Server Express ([DEPLOYMENT_WINDOWS_SERVER_2019.md](DEPLOYMENT_WINDOWS_SERVER_2019.md)),
follow Part C to create the Azure services, but **skip creating tables**: you copy your existing database instead.

1. **Copy the database to Azure.**
   - On the VPS, install SqlPackage: download the *Windows .NET 8* zip from `https://aka.ms/sqlpackage-windows` and
     unpack it to `C:\wabitex-apps\wabi-insight\tools\sqlpackage`.
   - Take the site offline: `Set-Content C:\wabitex-apps\wabi-insight\web\api\app_offline.htm 'Moving'`.
   - Then export and import. The database must not exist in Azure yet, so delete the empty `WabiInsightDB` created in
     step 8 first, or import under another name and use that name:

```powershell
. {
$sp = 'C:\wabitex-apps\wabi-insight\tools\sqlpackage\SqlPackage.exe'
$bacpac = Join-Path $WI.Backup 'WabiInsightDB.bacpac'
$localPassword = Read-WiSecret 'Password of the local wabiinsightdbowner login'
& $sp /Action:Export /SourceConnectionString:"Server=localhost,1433;Database=WabiInsightDB;User Id=wabiinsightdbowner;Password=$localPassword;Encrypt=True;TrustServerCertificate=True;" /TargetFile:$bacpac
$adminLogin = Read-Host 'Azure SQL admin login'
$adminPassword = Read-WiSecret 'Azure SQL admin password'
& $sp /Action:Import /SourceFile:$bacpac /TargetConnectionString:"Server=tcp:$($WI.SqlServer),1433;Initial Catalog=$($WI.SqlDatabase);User ID=$adminLogin;Password=$adminPassword;Encrypt=True;TrustServerCertificate=False;" /p:DatabaseEdition=Standard /p:DatabaseServiceObjective=S0
Remove-Variable localPassword, adminPassword
}
```

2. **Create the application user** in the imported database (step 10).
3. **Switch the settings:**
   - Step 26 rewrites `appsettings.Local.json` with Azure SQL and Azure Storage. Keep `DataProtection.KeyRingPath`
     pointing at your **existing** key ring.
   - Then remove the offline file: `Remove-Item C:\wabitex-apps\wabi-insight\web\api\app_offline.htm`.
4. **Copy the existing files to Azure:** with the new settings in place, run once:

```powershell
Push-Location (Join-Path $WI.Root 'web\api'); $env:ASPNETCORE_ENVIRONMENT = 'Production'
$settings = Get-Content 'appsettings.Local.json' -Raw | ConvertFrom-Json
$settings.Storage.RootPath = 'D:\wabi-insight\files'                    # where the files are today
$settings | ConvertTo-Json -Depth 5 | Set-Content 'appsettings.Local.json' -Encoding UTF8
dotnet .\WabiInsight.Api.dll storage-copy                               # copies every file into the container (safe to repeat)
Pop-Location
Restart-WebAppPool 'WabiInsightApi'
```

5. **Check:**
   - Open an older analysis in **My History** and download its report.
   - When everything works, stop and disable the local SQL Server
     (`Stop-Service 'MSSQL$SQLEXPRESS'; Set-Service 'MSSQL$SQLEXPRESS' -StartupType Disabled`) and the old backup task.
   - Then set up step 32, which replaces the old nightly task of the same name.
