# Wabi Insight on Microsoft Azure — the complete beginner's guide (deployment + CI/CD)

This guide assumes **no previous knowledge** of Azure or GitHub Actions. It takes you from no Azure account at all to a
live Wabi Insight at `https://<your name>.azurewebsites.net` (and, if you want, `https://<your domain>`), with:

- the **web app and the API** in one **Azure App Service** (Microsoft runs the server for you: no Windows or Linux
  machine to install, patch or log in to);
- the **database** in **Azure SQL Database** (Microsoft runs it, with backups included);
- the **generated and uploaded files** (reports, exports, attachments, logos) in a private **Azure Blob Storage**
  container;
- **one-click deployments** from GitHub (**Actions → Deploy (Azure)**). GitHub builds the code, creates or updates the
  Azure services from the template `deploy/azure/main.bicep`, updates the database and installs the new version.
  **No Azure password is stored in GitHub.**

```
 Users' browsers
      │ HTTPS (443)
      ▼
 ┌──────── Azure: resource group "rg-wabi-insight-prod" (one region, for example UAE North) ─────────────┐
 │ App Service "<AZURE_NAME>" (Linux, .NET 10), on the App Service plan "<AZURE_NAME>-plan" (size B1)
 │   https://<AZURE_NAME>.azurewebsites.net        (or your own domain, Part F)
 │   ├─ the API         /home/site/wwwroot          (dotnet WabiInsight.Api.dll)
 │   ├─ the web app     /home/site/web              (served by the API itself: Web:RootPath)
 │   └─ the key ring    /home/data/wabi-insight/keys (persistent storage: survives restarts and deployments)
 │        │ SQL over TLS (port 1433)                        │ HTTPS (443)
 │        ▼                                                 ▼
 │ Azure SQL server "<AZURE_NAME>-sql"              Storage account "<storage name>" (step 7)
 │   database "WabiInsightDB" (size S0)               private container "wabi-insight-files"
 │   automatic backups, point-in-time restore         (reports, exports, attachments, logos;
 │                                                     deleted files kept 14 days)
 └────────────────────────────────▲──────────────────────────────────────────────────────────────────────┘
                                  │ OpenID Connect: a short-lived sign-in for each run, no password stored
 GitHub → Actions → "Deploy (Azure)" (runs only when you click Run workflow)
```

**Time:** about 2 hours the first time, including 15–20 minutes for the first run. **Monthly cost:** about
USD 30–35 for the smallest sizes (see *Costs* near the end).

## How to read this guide

- **Where each step runs** is shown at its start:
  - **[Azure portal]**: the Azure website, `https://portal.azure.com`, in your browser.
  - **[Cloud Shell]**: the command window inside the Azure portal (step 5). Nothing to install on your computer.
  - **[GitHub]**: the GitHub website, on the repository `mtawfig/wabi-data-harbor`.
  - **[Domain registrar]**: the website of the company where you bought your domain (only in Part F).
  - **[App Service SSH]**: a command window inside the running App Service, opened from the portal (steps 22 and 31).
  - **[Old server]**: only in the appendix, if you're moving an existing installation.
- Most steps have three parts:
  - **Check:** tells you whether the step is already done. If it is, skip **Do**.
  - **Do:** the actions or commands.
  - **Verify:** proves the step worked. **Don't continue until it does.**
- **Copy and paste** commands exactly.
  - In Cloud Shell, paste with **Ctrl+Shift+V** (Windows, Linux), **Cmd+V** (Mac), or **right-click → Paste**.
  - Paste a whole grey block at once, then press **Enter** if the last line hasn't run yet. Wait until the prompt
    (ending in `$`) comes back before you paste the next block.
  - Lines starting with `#` are comments and are ignored. Text after `#` on a line is a comment too.
- Text in `<angle brackets>` is a placeholder. Replace it with your own value, **without the brackets**.
- **Never paste a password into a chat, an e-mail, an issue, a pull request or a commit.** In GitHub, secrets belong
  in exactly one place: **Settings → Environments → production-azure → Environment secrets** (step 15). Keep every
  password and key in a password manager (Bitwarden, 1Password, KeePass…).

### Words you'll meet

| Word | Meaning |
|---|---|
| Azure account | Your sign-in to Microsoft Azure: an e-mail address and a password (plus a second factor on your phone). |
| Azure portal | `https://portal.azure.com`, the website where you create and manage Azure services. |
| Subscription | Azure's billing container. Everything you create belongs to one subscription, and its costs go on that subscription's invoice. It has an ID: a long code like `00000000-0000-0000-0000-000000000000`. |
| Tenant (Microsoft Entra ID) | Your organization's directory in Microsoft's cloud: its users and the identities of programs. It's created with your Azure account and has its own ID. |
| Region | The area of Microsoft data centers where your services run, for example `uaenorth` (United Arab Emirates). |
| Resource group | A folder in Azure that holds related services. **Deleting it deletes everything inside.** |
| Resource provider | A part of Azure (web apps, SQL, storage) that must be switched on once per subscription. |
| App Service / App Service plan | App Service runs a web application without a server for you to maintain. The plan is the size of machine you pay for (here **B1**). |
| Azure SQL server / database | The *server* is a name and a login gate (`<name>.database.windows.net`). The *database* is where the data lives. |
| Firewall rule | Which internet addresses may connect to the Azure SQL server. |
| Storage account / container / blob | The storage account is a file store; a container is a folder in it; a blob is a file. |
| Cloud Shell | A command window inside the Azure portal, already signed in as you. Commands that start with `az` are the **Azure CLI**. |
| Bicep template | A file that describes Azure services. Here: `deploy/azure/main.bicep`. Azure creates or updates the services to match it. |
| App registration / service principal | An identity for a program instead of a person. The *app registration* is its definition; the *service principal* is its account in your tenant, which receives rights. |
| OpenID Connect / federated credential | A way for GitHub to sign in to Azure **without a password**. For each run, GitHub gets a short-lived token. The *federated credential* tells Azure which repository and which GitHub environment it trusts. |
| Role assignment | A right given to an identity on a scope. Here: **Contributor** on one resource group (it may create and change everything in that group, but can't give rights to anyone). |
| GitHub Actions / workflow / run / job | GitHub's automation. A *workflow* is a file under `.github/workflows`. Each start is a *run*. A run is made of *jobs* (Build, Azure resources, Database, API, Web app). |
| GitHub environment | A named set of variables and secrets (here `production-azure`) that the deployment jobs use, with optional protection rules. |
| Variable vs secret | A *variable* is plain text: anyone with access to the repository settings can read it. A *secret* is hidden: GitHub never shows it again and masks it in the logs. |
| CI | Continuous Integration: GitHub builds and tests every change automatically. Already active in this repository. |
| CD | Continuous Delivery: installing a version on the live site. Here you start it with one click. |
| Migration | A change to the database's structure that comes with a new version. The `migrate` command applies the pending ones. |
| Key ring | Files that encrypt the secrets the application stores in the database (connection strings, provider keys). **If you lose them, those secrets can't be read.** |
| App settings | The App Service's configuration values, given to the application as environment variables. The template writes them. |
| Point-in-time restore | Azure SQL's automatic backups: you can restore the database as it was at any moment of the last 7 days (the default). |
| Log stream | The App Service page that shows the application's messages live. |
| bacpac | One file that holds a whole database, structure and data. Used to move a database. |
| CNAME / TXT record | DNS settings at your domain registrar. A CNAME points a name to another name; a TXT record proves you own the domain. |
| Managed certificate | A free TLS certificate (the padlock) that App Service creates for your own domain and renews by itself. |

## Your information sheet

Fill this in as you go. Keep the secret rows in your password manager, not in a text file.

| Item | Example | Secret? |
|---|---|---|
| Azure account e-mail and password | `it@example.com` / … | **yes** (the password) |
| Subscription ID | `00000000-0000-0000-0000-000000000000` | no |
| Tenant ID | `11111111-1111-1111-1111-111111111111` | no |
| Region | `uaenorth` | no |
| Resource group | `rg-wabi-insight-prod` | no |
| Deployment name (`AZURE_NAME`) | `wabi-insight-acme` | no |
| Site address | `https://wabi-insight-acme.azurewebsites.net` | no |
| SQL server | `wabi-insight-acme-sql.database.windows.net` | no |
| Storage account | `wabiinsightacmefiles` | no |
| App registration and its client ID (`AZURE_CLIENT_ID`) | `wabi-insight-github-deploy` / `22222222-2222-…` | no |
| SQL admin login and password (`AZURE_SQL_ADMIN_PASSWORD`) | `wabiadmin` / … | **yes** (the password) |
| First Platform Administrator e-mail (`WI_ADMIN_EMAIL`) | `support@example.com` | no |
| First Platform Administrator first password (`WI_BOOTSTRAP_ADMIN_PASSWORD`) | … | **yes** |
| Bootstrap initialization secret (`WI_BOOTSTRAP_INITIALIZATION_SECRET`) | … | **yes** |
| Your own domain (optional) | `insight.example.com` | no |
| Copies of the key ring (step 22) | `key-<id>.xml` | **yes** |

---

# Part A — Before you start

## 1. What you need

| You need | Where to get it | Notes |
|---|---|---|
| A Microsoft Azure account with a subscription where you are **Owner** | `https://azure.microsoft.com` (step 2) | Whoever creates the account is Owner automatically. Otherwise you need **Owner**, or **Contributor** plus **User Access Administrator**, on the subscription. |
| The right to create app registrations in your tenant | Automatic if you created the account yourself | In a company tenant, your IT administrator may have to give you the *Application Developer* role. |
| A GitHub account with **admin** rights on the repository `mtawfig/wabi-data-harbor` | `https://github.com` | Needed to create the environment in step 15. |
| A computer with a modern browser (Edge, Chrome, Firefox, Safari) | — | Everything happens in the browser. You install nothing on your computer. |
| A password manager | Bitwarden, 1Password, KeePass… | For the passwords and secrets of this guide. |
| Optional: a domain name and access to its DNS settings | Your domain registrar (GoDaddy, Namecheap, Cloudflare…) | Only for Part F. Without it, the site uses `https://<AZURE_NAME>.azurewebsites.net`. |

The SMTP account (for e-mails) and the AI key are already in the repository's `appsettings.json` (owner decision G).
**You don't need to enter them.**

## 2. Create your Azure account and subscription

**Where:** [Azure portal], in your browser.

**Check:** sign in at `https://portal.azure.com`. In the search box at the top, type **Subscriptions** and open it. If
a subscription is listed with **Status: Active**, skip to step 3.

**Do:**
1. Open `https://azure.microsoft.com` → **Try Azure for free** (a free account with a starting credit for the first
   30 days) or **Pay as you go**.
2. Sign in with a Microsoft account, or create one. Use a company mailbox that will outlive any one person.
3. Follow the sign-up: your details, a phone check, a credit card (needed even for the free account), the agreement →
   **Sign up**.
4. When Azure asks you to set up **multi-factor authentication**, do it (for example with the Microsoft Authenticator
   app on your phone).
5. Open `https://portal.azure.com`.

> **Free account:** when its credit or its 30 days run out, Azure stops the services unless you upgrade the
> subscription to *pay as you go*. Azure reminds you in the portal and by e-mail. Upgrade before you go live.

**Verify:** **Subscriptions** lists one subscription (often called *Azure subscription 1*) with **Status: Active**.
Write its name on your sheet.

## 3. Check your rights in Azure

**Where:** [Azure portal].

**Do / Verify:**
1. Search **Subscriptions** → click your subscription → **Access control (IAM)** (left menu) → **View my access**.
2. The list shows **Owner** for you.

If it doesn't, ask the subscription's owner to give you **Owner** (or **Contributor** and **User Access
Administrator**) on this subscription before you continue. Step 13 gives the GitHub identity a role, which needs one of
these.

## 4. Find your subscription ID and tenant ID

**Where:** [Azure portal].

**Do:**
1. **Subscriptions** → your subscription → **Overview** → **Subscription ID**. Click the copy icon next to it and put it
   on your sheet.
2. Search **Microsoft Entra ID** → **Overview** → **Tenant ID**. Copy it to your sheet.

They're identifiers, not secrets. GitHub needs both in step 15.

**Verify:** both are long codes of the form `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`. Step 5 prints them again.

---

# Part B — Cloud Shell, names and the resource group

## 5. Open Cloud Shell for the first time

**Where:** [Azure portal] → the **Cloud Shell** icon `>_` in the top bar, to the right of the search box.

**Do:**
1. Click `>_`. A panel opens at the bottom of the page.
2. The first time, Azure asks **Bash** or **PowerShell**: choose **Bash**. Every [Cloud Shell] command in this guide
   is Bash.
3. Azure then asks about storage. Choose **No storage account required**, select your subscription, then **Apply**.
   This is an *ephemeral* session: files you create in Cloud Shell are deleted when it closes. Nothing in this guide
   needs them to stay.
   - If your portal only offers to **create storage**, accept it. It creates a small storage account for Cloud Shell
     (a few cents a month).
4. Wait for the prompt, a line that ends with `$`.

If PowerShell opens instead, click **Switch to Bash** in the panel's toolbar.

Good to know:
- Cloud Shell closes after **20 minutes** without typing. Open it again with `>_` and paste step 8's values again.
- The toolbar's **maximize** icon makes the panel bigger.

**Verify:**

```bash
az account show --query "{subscription:name, subscriptionId:id, tenantId:tenantId, user:user.name}" --output table
```

It shows your subscription, the two IDs from step 4 and your e-mail.

If you have **more than one subscription** and the wrong one is shown, list them and choose the right one:

```bash
az account list --query "[].{name:name, id:id, isDefault:isDefault}" --output table
az account set --subscription "<subscription ID>"
```

## 6. Choose the region

The **region** is where your services run. Choose the one **closest to your users** and to the databases your
organizations will connect to: pages are faster, and the data stays in that country or area. Prices differ a little
between regions.

**[Cloud Shell] Do:** list the regions your subscription can use:

```bash
az account list-locations --query "[?metadata.regionType=='Physical'].{region:name, name:displayName, area:metadata.geographyGroup}" --output table
```

Examples: `uaenorth` (UAE), `qatarcentral` (Qatar), `westeurope` (Netherlands), `northeurope` (Ireland), `eastus`
(USA). Use the **`region`** column: lower case, no spaces.

**Verify:** the region you chose is in the list. Write it on your sheet.

Everything goes into this one region: the template uses the resource group's region (step 10) unless you set
`AZURE_LOCATION` in step 15. If the first run later says the region has no room for the chosen sizes, see
*Troubleshooting* (step 31).

## 7. Choose the deployment name (`AZURE_NAME`)

One name, `AZURE_NAME`, gives every Azure service its name. Several of these names become **web addresses**, so they
must be **unique worldwide**, across all Azure customers.

**Rules** (the workflow checks them, in `deploy/cicd/azure/names.sh`):
- **2 to 40** characters;
- only lower-case letters `a`–`z`, digits `0`–`9` and hyphens `-`;
- it must **start and end** with a letter or a digit.

Add your company name to make it unique, for example `wabi-insight-acme`. These names follow from it:

| Azure service | Name | Example (`AZURE_NAME` = `wabi-insight-acme`) |
|---|---|---|
| App Service (the site) | `AZURE_NAME` | `wabi-insight-acme` → `https://wabi-insight-acme.azurewebsites.net` |
| App Service plan | `AZURE_NAME` + `-plan` | `wabi-insight-acme-plan` |
| Azure SQL server | `AZURE_NAME` + `-sql` | `wabi-insight-acme-sql` → `wabi-insight-acme-sql.database.windows.net` |
| SQL database | `WabiInsightDB` (or `AZURE_SQL_DATABASE`, step 15) | `WabiInsightDB` |
| Storage account | `AZURE_NAME` without its hyphens, cut to 19 characters, + `files` | `wabiinsightacmefiles` |
| Blob container | always `wabi-insight-files` | `wabi-insight-files` |

> **Choose it once.** If you change `AZURE_NAME` after the first run, the next run creates a **second, empty**
> installation next to the first one.

Step 8 checks that the names are still free.

## 8. Set your values in Cloud Shell

**Where:** [Cloud Shell]. **Paste this block again every time you open Cloud Shell** (after a break of 20 minutes, for
example).

**Do:** copy the block into a plain-text editor on your computer (Notepad on Windows; on a Mac, TextEdit after
**Format → Make Plain Text**). Change the two lines marked `# CHANGE`, then copy the block from there into Cloud Shell.
Keep that edited copy: you'll paste it again later.

```bash
# Wabi Insight on Azure: your values. Paste this block every time you open Cloud Shell.
RG="rg-wabi-insight-prod"                  # the resource group (step 10)
LOCATION="uaenorth"                        # CHANGE: your region from step 6
AZURE_NAME="wabi-insight-acme"             # CHANGE: your deployment name from step 7
REPO="mtawfig/wabi-data-harbor"            # the GitHub repository (owner/name)
SQL_SERVER="$AZURE_NAME-sql"
SQL_DATABASE="WabiInsightDB"               # or your AZURE_SQL_DATABASE, if you set one in step 15
STORAGE="$(printf '%s' "$AZURE_NAME" | tr -cd 'a-z0-9' | cut -c1-19)files"
APP_ID="$(az ad app list --display-name wabi-insight-github-deploy --query "[0].appId" --output tsv)"
echo "Resource group: $RG ($LOCATION) · site: https://$AZURE_NAME.azurewebsites.net"
echo "SQL server: $SQL_SERVER · database: $SQL_DATABASE · storage: $STORAGE · identity: ${APP_ID:-not created yet}"
```

`STORAGE` is computed exactly like the workflow does it. `APP_ID` stays empty until step 11.

**Verify:** the two `echo` lines show your values. The first time only, check that the names are still free:

```bash
az storage account check-name --name "$STORAGE" --query "{name:'$STORAGE', available:nameAvailable, reason:reason}" --output table
getent hosts "$AZURE_NAME.azurewebsites.net" || echo "Site name $AZURE_NAME: looks free"
getent hosts "$SQL_SERVER.database.windows.net" || echo "SQL server name $SQL_SERVER: looks free"
```

- The storage line must say `True` under `available`.
- The other two lines must print *looks free*. If one prints an internet address instead, that name is already used
  by someone: choose another `AZURE_NAME` (step 7) and paste the values again.

These checks are a good hint, not a guarantee. The first run (step 17) is the final test.

## 9. Switch on the resource providers

A new subscription may not have web apps, SQL and storage switched on yet. The GitHub identity can't switch them on
(it only has rights on one resource group), so you do it once.

**[Cloud Shell] Check:**

```bash
for ns in Microsoft.Web Microsoft.Sql Microsoft.Storage; do
  printf '%-18s %s\n' "$ns" "$(az provider show --namespace "$ns" --query registrationState --output tsv)"
done
```

**Do:** if any line doesn't say `Registered`:

```bash
for ns in Microsoft.Web Microsoft.Sql Microsoft.Storage; do az provider register --namespace "$ns"; done
```

It takes a few minutes. Azure says the registration is *ongoing*; that's normal.

**Verify:** run the **Check** block again after 2–3 minutes. All three lines say `Registered`.

## 10. Create the resource group

**[Cloud Shell] Check:**

```bash
az group show --name "$RG" --query "{name:name, location:location, state:properties.provisioningState}" --output table
```

The first time it says the resource group *could not be found*. That's expected.

**Do:**

```bash
az group create --name "$RG" --location "$LOCATION" --output table
```

**Verify:** the **Check** command now shows `rg-wabi-insight-prod`, your region and `Succeeded`. In the portal, search
**Resource groups**: `rg-wabi-insight-prod` is listed and still empty.

---

# Part C — The identity GitHub signs in with (OpenID Connect)

GitHub needs to sign in to Azure to create the services and install new versions. Instead of a password, you create an
identity that **trusts GitHub's sign-in tokens, but only for this repository and only for the environment
`production-azure`**, and that may change **only the one resource group**. Three steps: the identity (11), the trust
(12), the right (13).

Run all three in [Cloud Shell], with step 8's values loaded.

## 11. The app registration and its service principal

**Check:**

```bash
az ad app list --display-name wabi-insight-github-deploy --query "[].{name:displayName, appId:appId}" --output table
```

Empty the first time. If it lists `wabi-insight-github-deploy`, the identity exists: skip **Do** (step 8's values
already put its ID in `APP_ID`) and go to **Verify**. Don't create a second one with the same name.

**Do:**

```bash
APP_ID=$(az ad app create --display-name wabi-insight-github-deploy --query appId --output tsv)
echo "Application (client) ID: $APP_ID"
az ad sp create --id "$APP_ID" --output none
```

- The first line creates the **app registration** named `wabi-insight-github-deploy` and keeps its **application
  (client) ID** in `APP_ID`. This ID becomes `AZURE_CLIENT_ID` in GitHub.
- The third line creates its **service principal**: the account in your tenant that receives the role in step 13.

If the first line says *Insufficient privileges to complete the operation*, your account may not create app
registrations. Ask your IT administrator for the *Application Developer* role (step 1).

**Verify:**

```bash
az ad sp show --id "$APP_ID" --query "{name:displayName, appId:appId}" --output table
```

It shows `wabi-insight-github-deploy` and the same ID. In the portal: **Microsoft Entra ID → App registrations → All
applications → wabi-insight-github-deploy**.

## 12. The federated credential for the environment `production-azure`

This tells Azure: "accept sign-in tokens from GitHub Actions, for this repository, for jobs that use the environment
`production-azure`".

**Check:**

```bash
az ad app federated-credential list --id "$APP_ID" --query "[].{name:name, subject:subject}" --output table
```

Empty the first time. If it lists `github-production-azure` with the subject shown under **Verify**, skip **Do**.

**Do:**

```bash
az ad app federated-credential create --id "$APP_ID" --parameters "{
  \"name\": \"github-production-azure\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:$REPO:environment:production-azure\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"
```

What each value means:

| Value | Meaning |
|---|---|
| `name`: `github-production-azure` | A label for this trust. Any name works; this one says what it is. |
| `issuer`: `https://token.actions.githubusercontent.com` | Who issues the tokens: GitHub Actions. Always this value. |
| `subject`: `repo:mtawfig/wabi-data-harbor:environment:production-azure` | **Which runs** are trusted: this repository, jobs in the environment `production-azure`. Every job of *Deploy (Azure)* that touches Azure uses this environment. It must match **exactly**, including upper and lower case. |
| `audiences`: `api://AzureADTokenExchange` | The fixed value Azure expects for this kind of sign-in. |

**Verify:**

```bash
az ad app federated-credential list --id "$APP_ID" --query "[].subject" --output tsv   # repo:mtawfig/wabi-data-harbor:environment:production-azure
```

In the portal: the app registration → **Certificates & secrets** → **Federated credentials** tab.

> If the repository is ever renamed or moved to another owner, the subject no longer matches and the sign-in fails
> (see *Troubleshooting*). Change `REPO` in step 8, delete the old credential in the portal and run **Do** again.

## 13. The Contributor role on the resource group, and the three IDs

**Check:**

```bash
az role assignment list --assignee "$APP_ID" --all --query "[].{role:roleDefinitionName, scope:scope}" --output table
```

Empty the first time.

**Do:**

```bash
RG_ID=$(az group show --name "$RG" --query id --output tsv)
az role assignment create --assignee "$APP_ID" --role Contributor --scope "$RG_ID" --output none
```

- `RG_ID` is the resource group's full Azure address (`/subscriptions/…/resourceGroups/rg-wabi-insight-prod`).
- **Contributor** on that scope lets the identity create and change everything **in this resource group only**. It
  can't touch the rest of your subscription, and it can't give rights to anyone.

If Azure says it *cannot find* the service principal, the identity from step 11 is still being copied inside
Microsoft's systems. Wait a minute and run the second line again.

**Verify:**

```bash
az role assignment list --assignee "$APP_ID" --scope "$RG_ID" --query "[].roleDefinitionName" --output tsv   # Contributor
```

In the portal: **Resource groups → rg-wabi-insight-prod → Access control (IAM) → Role assignments**: **Contributor**
lists `wabi-insight-github-deploy`.

**Then print the three IDs GitHub needs**, and write them on your sheet. They're identifiers, not secrets:

```bash
echo "AZURE_CLIENT_ID=$APP_ID"
az account show --query "{AZURE_TENANT_ID:tenantId, AZURE_SUBSCRIPTION_ID:id}" --output table
```

---

# Part D — Passwords and the GitHub environment

## 14. Generate the passwords and the bootstrap secret

You need three secret values:

| Name in GitHub | What it is | Rules |
|---|---|---|
| `AZURE_SQL_ADMIN_PASSWORD` | The password of the Azure SQL administrator (`wabiadmin`). The template sets it on the SQL server; the application and the migrations sign in with it. | Azure SQL: at least 8 characters, from three of these four groups: upper case, lower case, digits, symbols, and no three characters in a row of the login name. It goes into a connection string, so **no `;`, `'` or `"`**. |
| `WI_BOOTSTRAP_ADMIN_PASSWORD` | The **first** password of the first Platform Administrator. You change it at the first sign-in. | At least 12 characters (`Security:PasswordMinLength`), not the same as the e-mail address. |
| `WI_BOOTSTRAP_INITIALIZATION_SECRET` | A one-time key that allows the first run to create that administrator. | At least 16 characters. |

**[Cloud Shell] Do:** paste this block. It prints three new random values:

```bash
# A password: "Wi-", 24 random letters and digits, then "-7". It has upper case, lower case, digits and a symbol,
# and no character that breaks a connection string.
new_password() {
  local p
  while :; do
    p="Wi-$(openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c 24)-7"
    # Azure SQL refuses a password that contains three letters in a row of the login name "wabiadmin".
    if ! printf '%s' "$p" | grep -qiE 'wab|abi|bia|iad|adm|dmi|min'; then
      printf '%s\n' "$p"
      return
    fi
  done
}
echo "AZURE_SQL_ADMIN_PASSWORD:           $(new_password)"
echo "WI_BOOTSTRAP_ADMIN_PASSWORD:        $(new_password)"
echo "WI_BOOTSTRAP_INITIALIZATION_SECRET: $(openssl rand -hex 32)"
```

**Right away**, copy each value into your password manager under its name: select it with the mouse, then
**right-click → Copy**. Then clear the screen:

```bash
clear
```

Cloud Shell doesn't keep these values: they were only printed, never saved.

**Verify:** your password manager has three new entries, named as in the table, each with a value of 29 characters
(the two passwords) or 64 characters (the secret).

> **Don't change the SQL password by hand in the portal.** The template sets it from the GitHub secret on every run
> with **Azure resources** ticked. To change it, see *Regular upkeep* (step 26).

## 15. The GitHub environment `production-azure`: variables and secrets

The deployment jobs read their settings from a **GitHub environment** named `production-azure`. The identity from
step 12 trusts only jobs in this environment.

**Where:** [GitHub] → the repository `mtawfig/wabi-data-harbor` → **Settings** (the last tab at the top; you need
admin rights to see it) → **Environments** (left menu, under *Code and automation*).

**Check:** is `production-azure` listed? If so, open it and compare its variables and secrets with the tables below.

**Do, part 1: the environment**
1. **New environment** → **Name:** `production-azure` (exactly this, in lower case) → **Configure environment**.
2. Recommended: **Deployment branches and tags** → change *No restriction* to **Selected branches and tags** → **Add
   deployment branch or tag rule** → *Ref type:* **Branch**, *Name pattern:* `main` → **Add rule**. Then only runs
   started from `main` can deploy.
3. Optional: **Required reviewers** → add yourself → **Save protection rules**. Then every job that touches Azure
   waits for your approval. A full run has **four** such jobs (Azure resources, Database, API, Web app), so you
   approve up to four times per run.

If **Environments** is missing, or the protection options can't be used: on **private** repositories, GitHub keeps
these features for paid plans. The workflow needs the environment with its variables and secrets; branch rules and
reviewers are optional.

**Do, part 2: the variables.** Under **Environment variables** → **Add environment variable** → type the **Name** and
the **Value** → **Add variable**. Repeat for each row. Paste values without spaces or a line break at the end.

| Name | Needed? | Value |
|---|---|---|
| `AZURE_CLIENT_ID` | **Required** | From step 13 (`AZURE_CLIENT_ID=…`). |
| `AZURE_TENANT_ID` | **Required** | From step 13 (the same as step 4). |
| `AZURE_SUBSCRIPTION_ID` | **Required** | From step 13 (the same as step 4). |
| `AZURE_RESOURCE_GROUP` | **Required** | `rg-wabi-insight-prod` (step 10). |
| `AZURE_NAME` | **Required** | Your deployment name from step 7, for example `wabi-insight-acme`. |
| `WI_ADMIN_EMAIL` | Recommended | The e-mail of the first Platform Administrator: a real mailbox that you read. Without it, the run uses `Bootstrap:AdministratorEmail` from `appsettings.json`. |
| `AZURE_LOCATION` | Optional | Leave it out: the services go to the resource group's region (step 10). |
| `AZURE_APP_SKU` | Optional | The App Service size. Leave it out for `B1`. Allowed: `B1`, `B2`, `B3`, `S1`, `S2`, `P0v3`, `P1v3`, `P2v3` (bigger costs more). |
| `AZURE_SQL_SKU` | Optional | The database size. Leave it out for `S0`. Allowed: `Basic` (cheaper, at most 2 GB), `S0`, `S1`, `S2`. |
| `AZURE_SQL_DATABASE` | Optional | Leave it out for `WabiInsightDB`. You only set it after a database restore (step 27). |
| `AZURE_SQL_ADMIN_LOGIN` | Optional | Leave it out for `wabiadmin`. **It can't be changed after the first run.** |
| `WI_PUBLIC_URL` | Optional, later | Your own domain, `https://insight.example.com`, once it works (step 24). Without it: `https://<AZURE_NAME>.azurewebsites.net`. It's used in the links of e-mails. |

**Do, part 3: the secrets.** Under **Environment secrets** → **Add environment secret** → **Name**, then paste the
**Value** from your password manager → **Add secret**:

| Name | Needed? | Value |
|---|---|---|
| `AZURE_SQL_ADMIN_PASSWORD` | **Required** | From step 14. |
| `WI_BOOTSTRAP_ADMIN_PASSWORD` | **Required for the first run** | From step 14. |
| `WI_BOOTSTRAP_INITIALIZATION_SECRET` | **Required for the first run** | From step 14. |

> **Variables must be variables, and secrets must be secrets.** The workflow reads the `AZURE_*` IDs and names as
> *variables*; if you add them as secrets, the run doesn't see them.

**Verify:** the `production-azure` page lists **5 to 12 variables** (at least the five required ones) and **3
secrets**. A secret shows only its name and when it was updated; GitHub never shows its value again. To change one,
use its pencil icon and paste a new value.

---

# Part E — CI/CD: the first deployment

## 16. How CI and CD work in this repository

| Part | File | When it runs | What it does |
|---|---|---|---|
| **CI**, already active | `.github/workflows/ci.yml` | **Automatically** on every pull request and every merge to `main` | Builds the API, runs the unit and SQL Server integration tests, checks dependencies for known vulnerabilities, lints, tests and builds the web app, and builds the Docker images. A red ✗ on a pull request means don't merge it yet. |
| **CD** | `.github/workflows/deploy-azure.yml` (**Deploy (Azure)**) | **Only when you click Run workflow** | Builds the parts you choose (`.github/workflows/build-release.yml`), signs in to Azure with OpenID Connect, and deploys what you ticked. Nothing runs on a schedule or by itself. |

```
 You: GitHub → Actions → Deploy (Azure) → Run workflow → tick the boxes
   ▼
 Build            on GitHub's computers: the API and/or the web app, tested unless you untick Run tests
   ▼
 Azure resources  sign in (OpenID Connect) → create or update everything from deploy/azure/main.bicep,
                  including the App Service's settings
   ▼
 Database         open the SQL firewall to this run's computer only → note the restore point →
                  migrate → [create the first Platform Administrator] → close the firewall again (always)
   ▼
 API              upload to the App Service → restart → wait until /health/ready answers 200
   ▼
 Web app          upload to /home/site/web (the API serves it) → check the site answers with the web app
```

Only the ticked parts run, always in this order. **A failed job stops the jobs after it.** Migrations run on GitHub's
computer, not on the App Service, through a firewall opening that exists only during that job.

The checkboxes, as GitHub shows them in the **Run workflow** form:

| Checkbox (GitHub shows this text) | Default | What happens |
|---|---|---|
| **Azure resources:** *create or update the App Service, SQL database and storage…* | off | Creates or updates the App Service plan and app, the SQL server and database, and the storage account and container from `deploy/azure/main.bicep`. Tick it the first time, after the template changed, or after you changed an `AZURE_*` or `WI_PUBLIC_URL` variable or the SQL password. **It rewrites all the App Service's settings from the template.** |
| **Database:** *apply migrations…* | off | Opens the SQL firewall to this run's computer, notes the restore point, runs `dotnet WabiInsight.Api.dll migrate`, then closes the firewall. |
| **API** | on | Uploads the published API to the App Service, restarts it and waits until `/health/ready` answers. |
| **Web app** | on | Uploads the Angular build to `/home/site/web` and checks that the site answers with the web app. |
| **Create the first Platform Administrator…** | off | **First deployment only:** runs `bootstrap` with the two bootstrap secrets. |
| **Run unit tests and web lint/tests before deploying** | on | Tests before anything is deployed. |
| *Optional commit SHA or tag to deploy…* (**ref**, a text box) | empty | Deploys an older commit or tag instead of the selected branch, for example to roll back (step 27). |

## 17. The first deployment

**Where:** [GitHub].

**Check:** open `https://<AZURE_NAME>.azurewebsites.net`. If it already shows the Wabi Insight landing page, the
first deployment is done: go to step 18.

> **Moving an existing installation** (its database, key ring and files)? Don't do this step: follow the
> [appendix](#appendix-moving-an-existing-installation-to-azure) instead, then come back to step 18.

**Do:**
1. Open the repository → **Actions** (tab at the top).
2. In the left list, click **Deploy (Azure)**.
3. On the right, click **Run workflow** (a grey button with ▾).
4. **Use workflow from:** **Branch: main**.
5. Tick **every** box:
   - **Azure resources** ✅
   - **Database** ✅
   - **API** ✅
   - **Web app** ✅
   - **Create the first Platform Administrator** ✅
   - **Run unit tests and web lint/tests** ✅
6. Leave the **ref** box empty.
7. Click the green **Run workflow**. After a few seconds, a run named *Deploy to Azure · resources · database · API ·
   web app (main) by @you* appears at the top of the list. Click it to watch.

If you turned on **Required reviewers** (step 15), each Azure job stops at *Waiting for review*: click **Review
deployments** → tick **production-azure** → **Approve and deploy**.

The first run takes **15–20 minutes**, mostly because Azure takes several minutes to create the SQL server. The run page
shows the jobs as boxes joined by lines: *Build / Resolve version*, *Build / Backend (.NET 10)*, *Build / Frontend
(Angular 22)*, then **Azure resources**, **Database**, **API**, **Web app**. Click a box to see its log; click a step
to open it. What to look for:

| Job → step | The log shows |
|---|---|
| any job → **Resource names** | `App Service: <AZURE_NAME> · SQL server: <AZURE_NAME>-sql · database: WabiInsightDB · storage: <storage name>` |
| Azure resources → **Create or update the resources** | After several minutes, the template's results: `appUrl`, `sqlServerFqdn`, `storageAccount`. |
| Database → **Open the SQL firewall to this runner** | `Opened <AZURE_NAME>-sql to <address> for this run.` |
| Database → **Apply migrations** | `Applying … migration(s): …`, then `Migrations applied.` |
| Database → **Create the first Platform Administrator** | `Bootstrap completed. AdministratorId=… The administrator must change the password at first sign-in.` |
| API → **Wait until the API is ready** | Some `Not ready yet (HTTP …); waiting…` lines while the app starts, then `Ready after … check(s).` |
| Web app → **Check the site** | `The web app answers at https://<AZURE_NAME>.azurewebsites.net (check …).` |

**Verify:**
1. Every box has a **green ✓**.
2. Scroll down the run page to the **summary**. It has four short tables:
   - **Azure resources:** the resource group and region, the App Service with its size and address, the SQL server
     and database with its size, the storage account;
   - **Database: success:** *Migrations applied: true*, *First administrator: true* and the **Restore point (UTC)**;
   - **API: success:** the App Service and the **Version** (the short commit code, then the full one in brackets);
   - **Web app: success:** the address and the version.
3. The API and Web app boxes show the site's address as a link.

If a box is red, open it, read the red line, and look it up in *Troubleshooting* (step 31). After fixing the cause,
use **Re-run jobs → Re-run failed jobs** at the top right of the run page.

> **Never tick *Create the first Platform Administrator* again.** It works once. On later runs it fails with
> `AlreadyCompleted`, and the API and Web app jobs after it are skipped.

## 18. Check what Azure created

**Where:** [Azure portal], then [Cloud Shell].

**Do / Verify in the portal:** **Resource groups → rg-wabi-insight-prod → Overview** lists these five services:

| Name | Type |
|---|---|
| `<AZURE_NAME>-plan` | App Service plan |
| `<AZURE_NAME>` | App Service |
| `<AZURE_NAME>-sql` | SQL server |
| `WabiInsightDB (<AZURE_NAME>-sql/WabiInsightDB)` | SQL database |
| `<storage name>` | Storage account |

**Do / Verify in Cloud Shell** (with step 8's values loaded):

```bash
az webapp show --resource-group "$RG" --name "$AZURE_NAME" --query "{state:state, address:defaultHostName, httpsOnly:httpsOnly}" --output table
curl -s "https://$AZURE_NAME.azurewebsites.net/health/ready"; echo
az webapp config appsettings list --resource-group "$RG" --name "$AZURE_NAME" --query "[].name" --output tsv
az sql server firewall-rule list --resource-group "$RG" --server "$SQL_SERVER" --query "[].name" --output tsv
```

| Line | Expected |
|---|---|
| `az webapp show` | `Running`, `<AZURE_NAME>.azurewebsites.net`, `True` |
| `curl …/health/ready` | `"status":"Healthy"`, with the checks `api` and `configuration-database` both `Healthy` |
| app settings | 11 names: `ASPNETCORE_ENVIRONMENT`, `ConnectionStrings__ConfigurationDatabase`, `DataProtection__KeyRingPath`, `Storage__Provider`, `Storage__RootPath`, `Storage__AzureBlob__ConnectionString`, `Storage__AzureBlob__ContainerName`, `Web__RootPath`, `Platform__BaseUrl`, `ReverseProxy__TrustAllProxies`, `WEBSITES_ENABLE_APP_SERVICE_STORAGE` (the command shows names only, never the values) |
| SQL firewall rules | only `AllowAllWindowsAzureIps` (Azure services, such as your App Service). The run's own rule `github-runner-…` is gone. |

The same settings are in the portal: **App Services → `<AZURE_NAME>` → Settings → Environment variables → App
settings**. Values stay hidden until you click **Show value**.

> **Don't add or change App Service settings in the portal.** The next run with **Azure resources** ticked rewrites
> the whole list from the template, and your change disappears. A setting that must stay belongs in
> `deploy/azure/main.bicep` (ask a developer).

## 19. Sign in as the first Platform Administrator

**Where:** your browser.

**Do:**
1. Open `https://<AZURE_NAME>.azurewebsites.net`. The landing page loads, with a padlock in the address bar.
2. Click **Sign in**.
3. **E-mail:** `WI_ADMIN_EMAIL` from step 15 (or, if you didn't set it, `Bootstrap:AdministratorEmail` from
   `appsettings.json`). **Password:** `WI_BOOTSTRAP_ADMIN_PASSWORD` from your password manager.
4. You're asked to **change the password**. Choose a new one of at least 12 characters, and save it in your password
   manager as *Wabi Insight Platform Administrator*.

**Verify:** after the change, you see the platform pages (**Platform Overview**, **Organizations**, **System
Health**…).

From now on, the two bootstrap secrets aren't used. You may delete them from the environment (**Settings →
Environments → production-azure**, the bin icon next to each); the first run is the only one that needs them.

## 20. Go-live checks

1. On the landing page, the **Arabic** button (globe icon) switches the page to Arabic, right to left. Switch back.
2. **System Health:** the database and the API are healthy.
3. **AI Providers:** the default provider is listed, and **Test** succeeds.
4. **Organizations:** create an organization, with a real e-mail address for its administrator. The **temporary
   password e-mail** arrives in that mailbox, which proves e-mail works. The temporary password is only ever e-mailed;
   no screen shows it.
5. Open a **private browser window**, sign in as that organization administrator, and change the password.
   Then **Organization Administration → Organization Settings → Upload logo**, and choose a small image.
6. **[Azure portal]** → **Storage accounts** → `<storage name>` → **Data storage → Containers** →
   `wabi-insight-files`. A folder `organization-logos` is there, which proves the files go to Azure Blob Storage. The
   container's **Anonymous access level** column says **Private**.
7. **Organization Administration → Applications:** register an application (your organization's database,
   read-only), click **Test connection**, then ask a question on **Ask Wabi Insight**.

   Your organization's database must accept connections from the App Service. The App Service's outgoing addresses
   are in **App Services → `<AZURE_NAME>` → Settings → Properties → Outbound IP addresses**, or in Cloud Shell:

   ```bash
   az webapp show --resource-group "$RG" --name "$AZURE_NAME" --query possibleOutboundIpAddresses --output tsv
   ```

   Allow **all** of these addresses in that database's firewall: Azure may use any of them.

## 21. Longer database backups (optional, recommended)

Azure SQL backs up the database automatically. By default you can restore it to any moment of the last **7 days**
(point-in-time restore). The run summary's restore point relies on it.

**Where:** [Azure portal] → **SQL servers** → `<AZURE_NAME>-sql` → **Data management → Backups → Retention policies**.

**Do:**
1. Tick `WabiInsightDB` → **Configure policies**.
2. **Point-in-time restore:** **14 days** (S0 allows up to 35; *Basic* allows at most 7).
3. Optional **long-term retention:** *Weekly backups* kept **4 weeks**. These copies are kept even beyond the
   point-in-time window.
4. **Apply**.

The template doesn't manage these settings, so later runs keep them.

**Verify:** the list shows the new retention for `WabiInsightDB`.

## 22. Keep a copy of the key ring

The key ring lives on the App Service's persistent storage (`/home/data/wabi-insight/keys`). It survives restarts,
deployments and Azure's maintenance. It's **lost if the App Service or the resource group is deleted**. Without it,
the connection strings and provider keys stored in the database can't be read, and every organization would have to
enter them again. So keep your own copy.

The application creates a new key about every 90 days (`DataProtection:KeyLifetimeDays`) and keeps the old ones.
Copy the new files each time (step 26 reminds you).

**Where:** [Azure portal] → **App Services** → `<AZURE_NAME>` → **Development Tools → SSH** → **Go →**. A command
window opens in a new browser tab, inside the App Service.

**[App Service SSH] Do:**

```bash
ls -l /home/data/wabi-insight/keys
cat /home/data/wabi-insight/keys/key-*.xml
```

The first line lists files named `key-<id>.xml`. The second prints them. For **each** file, copy its text, from
`<?xml` to `</key>`, into a **secure note** in your password manager named like the file (`key-<id>.xml`). Close the
tab when you're done.

**Verify:** your password manager has one note per file listed by `ls`, with the same names.

**To put the key ring back** (after the App Service was recreated, step 28): run the workflow with **only Azure
resources** ticked, then do this in [Cloud Shell] **before** you deploy the API. For each saved key, type the `cat` line with the
note's name instead of `key-<id>.xml`, press **Enter**, paste the note's text, press **Enter**, then **Ctrl+D**:

```bash
mkdir -p ~/keyring
cat > ~/keyring/"key-<id>.xml"      # the note's name; then paste the text, Enter, Ctrl+D. Repeat for each key.
```

Then upload them and clean up:

```bash
python3 -m zipfile -c ~/wabi-keys.zip ~/keyring/key-*.xml
az webapp deploy --resource-group "$RG" --name "$AZURE_NAME" --src-path ~/wabi-keys.zip --type zip \
  --target-path /home/data/wabi-insight/keys --clean false --restart true
rm -r ~/keyring ~/wabi-keys.zip
```

This uses the same upload method the workflow uses for the web app. After the next API deployment, the **Do** above
lists the same file names again.

---

# Part F — Your own domain (optional)

Without this part, the site works at `https://<AZURE_NAME>.azurewebsites.net`. With it, users open
`https://insight.example.com`. Use a **sub-domain** (like `insight.`), which is the simplest case. Both addresses keep
working.

## 23. Point your domain at the App Service, with a free certificate

**Where:** [Azure portal], and your [Domain registrar] in a second browser tab.

**Check:** **App Services** → `<AZURE_NAME>` → **Settings → Custom domains**. Is your domain listed with **Status:
Secured**? Then skip to step 24.

**Do:**
1. **[Azure portal]** **Custom domains** → **+ Add custom domain**:
   - **Domain provider:** **All other domain services**;
   - **TLS/SSL certificate:** **App Service Managed Certificate** (free; it renews itself);
   - **TLS/SSL type:** **SNI SSL**;
   - **Domain:** `insight.example.com`;
   - **Hostname record type:** **CNAME**.
2. The pane now shows **two DNS records** to create. Keep it open.
3. **[Domain registrar]** open your domain's DNS settings and add both records:

   | Type | Name / Host | Value / Points to |
   |---|---|---|
   | **CNAME** | `insight` | `<AZURE_NAME>.azurewebsites.net` |
   | **TXT** | `asuid.insight` | the long *domain verification ID* shown in the portal |

   - If `insight` already has an **A** record (for example an old server), delete it first: a name can't have both.
   - On **Cloudflare**, set the CNAME to **DNS only** (grey cloud) so Azure can check it and issue the certificate.
4. Wait a few minutes. **[Cloud Shell]** check that the records are visible (replace the domain):

   ```bash
   dig +short CNAME insight.example.com          # <AZURE_NAME>.azurewebsites.net.
   dig +short TXT asuid.insight.example.com      # "the verification ID"
   ```

5. **[Azure portal]** back in the pane: **Validate**. Both lines get a green tick. Then **Add**.
6. Azure adds the domain and creates the certificate. That takes a few minutes.

**Verify:**
- **Custom domains** lists `insight.example.com` with **Status: Secured**.
- `https://insight.example.com` opens the site with a padlock.

## 24. Tell the application its address (`WI_PUBLIC_URL`)

E-mailed links (temporary passwords, notifications, shared reports) use the application's address,
`Platform:BaseUrl`. The template sets it from `WI_PUBLIC_URL`.

**Do:**
1. **[GitHub]** **Settings → Environments → production-azure → Environment variables**: add (or edit with the
   pencil) `WI_PUBLIC_URL` = `https://insight.example.com` (with `https://`, without a `/` at the end).
2. **Actions → Deploy (Azure) → Run workflow**, branch `main`. Tick **only Azure resources**: untick **API** and **Web
   app** (they're ticked by default). **Run workflow**. Changing the settings restarts the App Service by itself.

**Verify:**
- The run is green.
- In Cloud Shell: `az webapp config appsettings list --resource-group "$RG" --name "$AZURE_NAME" --query "[?name=='Platform__BaseUrl'].value" --output tsv`
  prints `https://insight.example.com`.
- The next temporary-password e-mail links to your domain.

---

# Part G — Running it day to day

## 25. Everyday deployments

**[GitHub] Actions → Deploy (Azure) → Run workflow**, branch `main`, ref empty:

| What changed | Azure resources | Database | API | Web app |
|---|---|---|---|---|
| Only the web app (screens, texts, translations under `src/web`) | | | | ✅ |
| Only the API, without a new migration (including `appsettings.json`: SMTP, AI) | | | ✅ | |
| API and web app, without a new migration (the default boxes) | | | ✅ | ✅ |
| A change that adds a migration (new files under `src/WabiInsight.Infrastructure/Persistence/Migrations/`) | | ✅ | ✅ | ✅ |
| `deploy/azure/main.bicep`, an `AZURE_*` or `WI_PUBLIC_URL` variable, or the SQL password | ✅ | | ✅ if the code changed too | ✅ if the code changed too |
| Not sure | ✅ only if something under `deploy/azure` changed | ✅ | ✅ | ✅ |

- **Create the first Platform Administrator:** always **off** after the first run.
- **Run tests:** keep it **on**. Untick it only for an urgent fix that already passed CI.
- An empty migration step is harmless: it prints `Configuration database is up to date.`
- **What's live?** The latest green run's summary shows the **Version**. The repository's home page lists
  **production-azure** under *Deployments*, with the history.
- While the API restarts, the site is unavailable for about a minute.

## 26. Regular upkeep

| How often | What | How |
|---|---|---|
| Automatic | The App Service's operating system and .NET 10 runtime updates | Azure installs them. |
| Automatic | Database backups | Azure SQL (step 21 for longer retention). |
| Automatic | The certificate of your own domain | App Service renews the managed certificate. |
| Every 3 months | Copy new key-ring files | Step 22: compare `ls` with your password manager and copy the new `key-<id>.xml` files. |
| Monthly | Azure costs | **[Azure portal]** search **Budgets** (Cost Management) → **+ Add** → a monthly amount and an e-mail alert at, for example, 80 %. |
| Monthly | No leftover firewall openings | **SQL servers → `<AZURE_NAME>-sql` → Security → Networking**: only *AllowAllWindowsAzureIps* (and your own IP while you use the query editor). Delete any old `github-runner-…` rule. |
| When someone leaves | Who can deploy and who can sign in to Azure | **[GitHub] Settings → Collaborators** (or *Collaborators and teams*), and **[Azure portal] Subscriptions → Access control (IAM)**: remove them. |
| When you want to change the SQL password | Rotate `AZURE_SQL_ADMIN_PASSWORD` | Step 14 for a new value → update the secret (step 15) → run with **only Azure resources** ticked. The template sets the new password on the server and in the App Service settings, and the App Service restarts. |

## 27. Rolling back

There's no instant switch back on the App Service: every deployment replaces the previous files. A roll-back is a new
run of an older version, about 10 minutes (untick **Run tests** to save a few).

- **Code only (no migration in between):**
  1. Find the **full commit code** that worked: the **API** table of that earlier run's summary shows it in
     brackets after **Version**, or **[GitHub] Code → Commits** → the copy icon next to the commit. A tag works too.
  2. **Run workflow** → branch `main` → tick **API** and **Web app** (untick **Database**) → **ref:** the full commit
     code → **Run workflow**.
  3. Only commits made **after the Azure deployment was added to the repository (P13.18)** can be deployed this way:
     older commits don't contain the Azure scripts.
- **Database back to before a migration:** migrations aren't reversed. Azure restores into a **new** database, and you
  point the application at it.
  1. **[Azure portal]** → **SQL databases** → `WabiInsightDB` → **Overview** → **Restore** (top toolbar):
     - **Restore point (UTC):** the **Restore point (UTC)** from the summary of the run that applied the migration;
     - **Database name:** for example `WabiInsightDB-restored`;
     - **Review + create** → **Create**. Wait until the new database's **Status** is **Online**.
     Everything saved after the restore point is not in the restored database.
  2. **[GitHub]** in the environment, set the variable `AZURE_SQL_DATABASE` = `WabiInsightDB-restored` (step 15).
  3. **Run workflow** → tick **Azure resources**, **API** and **Web app** (not **Database**) → **ref:** the older
     commit → **Run workflow**. The template points the App Service at the restored database.
  4. Set `SQL_DATABASE="WabiInsightDB-restored"` in step 8's values, and repeat step 21 for the new database.
  5. Once everything works, delete the old database: **SQL databases → WabiInsightDB → Delete**. The template no
     longer manages it, and it still costs money.

## 28. Restoring after a disaster

| Lost | Restore from |
|---|---|
| Data in the database (a mistake, a bad migration) | **[Azure portal]** point-in-time restore (step 27), within the retention you set (step 21). |
| The database was deleted | **[Azure portal]** → **SQL servers** → `<AZURE_NAME>-sql` → **Data management → Deleted databases** → select it → restore under the same name, within the retention. |
| Files (reports, attachments, logos) | Deleted files: **[Azure portal]** → storage account → **Containers → wabi-insight-files** → **Show deleted blobs** → select → **Undelete** (within 14 days). |
| The whole `wabi-insight-files` container | Storage account → **Containers** → **Show deleted containers** → select → **Undelete** (within 14 days). |
| The App Service (deleted) | Run with **only Azure resources** ticked (it recreates it), put the key ring back (step 22), then run **API** and **Web app**. The database and the files are separate and unaffected. |
| The key ring | Your copies from step 22, put back as step 22 describes, **before** the API runs again. |
| The whole resource group (deleted) | Everything in it is gone: the services, the files, the database with its point-in-time backups, and the role from step 13. Only **long-term retention** backups (step 21), if you set them up, outlive a deleted server; Azure's documentation on long-term backup retention explains how to restore one. Then steps 10 and 13 again, a first run, and the key ring (step 22). **Never delete the resource group.** |

## 29. Security checklist

- **Azure and GitHub accounts:** multi-factor authentication on both. Only the people who need it are **Owner** of the
  subscription or **admin** of the repository.
- **No Azure secret in GitHub:**
  - The identity from steps 11–13 has no password. Azure accepts it only for runs of **this repository** in the
    environment **production-azure**, and it can change **only the one resource group**.
  - GitHub keeps three secrets: the SQL password and the two bootstrap values. Delete the bootstrap ones after
    step 19.
  - Deployments start only when someone with write access clicks **Run workflow**. Branch rule `main` and required
    reviewers (step 15) narrow this further.
- **Database:**
  - The SQL firewall lets in Azure services (`AllowAllWindowsAzureIps`, which your App Service needs), plus this run's
    computer for the length of a database step only. That rule is removed even when the job fails.
  - Check now and then for leftover `github-runner-…` rules (step 26), and remove your own IP after using the query
    editor.
  - The application signs in to SQL as the administrator `wabiadmin`. A narrower database user (with `db_datareader`,
    `db_datawriter` and the grants of `scripts/sql/create-runtime-login.sql`, keeping the administrator for
    migrations) would need a change to the template, because every **Azure resources** run rewrites the connection
    string.
- **Secrets in the App Service settings:**
  - The settings hold the SQL and storage connection strings. App Service encrypts them at rest, and they're never
    sent to the browser.
  - For a stricter setup, Key Vault references could replace them (also a template change).
- **Network and storage settings** (from the template):
  - HTTPS only, TLS 1.2, FTP off.
  - The blob container is **private**. Deleted blobs and containers are kept 14 days.
  - Azure SQL keeps point-in-time backups (7 days by default, step 21).
- **`ReverseProxy:TrustAllProxies=true`** is set on the App Service only. There, the platform's front end is the only
  way in, and it adds the real caller to `X-Forwarded-For` (the application reads only that last entry). Never set it
  on a server that can be reached directly.
- **Platform Insights** uses the configuration-database login unless `Insights:ConnectionString` is set. The
  **Platform Insights Configuration** page shows this. It's the same warning as on other servers; see
  [SECURITY.md](SECURITY.md#platform-insights-p71). On Azure, an `Insights__ConnectionString` setting added in the
  portal would also be removed by the next **Azure resources** run.
- **The key ring:** your copies (step 22) are secrets. Keep them only in your password manager.
- More detail: [SECURITY.md, *Azure deployment*](SECURITY.md#azure-deployment-p1318).

## 30. Costs (approximate, per month)

Prices depend on the region. Check the Azure pricing calculator for yours.

| Item | Size | About |
|---|---|---|
| App Service plan | B1 (1 core, 1.75 GB) | USD 13–18 |
| Azure SQL Database | S0 (10 DTU, 250 GB); *Basic* (2 GB) is about USD 5 | USD 15 |
| Point-in-time and long-term backup storage | Small database | USD 0–2 |
| Azure Storage | Standard LRS, a few GB | under USD 1 |
| Your own domain's certificate | App Service Managed Certificate | free |
| GitHub Actions | about 15 build minutes per full deployment | covered by the included minutes |
| **Total** | | **about USD 30–35** |

Bigger sizes (`AZURE_APP_SKU`, `AZURE_SQL_SKU`, step 15) cost more. Set a budget alert (step 26).

## 31. Troubleshooting

First, find **where** it fails: the red box of the run (click it, then the red step), or, for a site that doesn't
start, the **Log stream**: **[Azure portal] → App Services → `<AZURE_NAME>` → Monitoring → Log stream**. If the Log
stream says application logging is off: **Monitoring → App Service logs** → **Application logging: File System** →
**Save**, and open the Log stream again. To look at the files on the App Service, use **[App Service SSH]** (step 22)
and run `ls /home/site/wwwroot /home/site/web /home/data/wabi-insight/keys`.

| What you see | Likely cause | Fix |
|---|---|---|
| **Resource names:** *Set the AZURE_NAME variable of the production-azure environment …* | `AZURE_NAME` is missing, breaks the rules, or was added as a secret | Step 7 for the rules, step 15 to add it as a **variable** |
| **Sign in to Azure:** an error that values such as the client ID or tenant ID are missing | `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` or `AZURE_SUBSCRIPTION_ID` is missing, misspelled, or was added as a secret | Step 15: add them as **variables** of `production-azure` |
| **Sign in to Azure:** *AADSTS70021: No matching federated identity record found for presented assertion* | The federated credential doesn't match this run: a typo in the subject, another environment name, or a renamed repository | The message shows the *Assertion Subject* GitHub sent. Compare it with step 12's **Verify**; fix with step 12 |
| **Sign in to Azure:** *AADSTS700016: Application with identifier '…' was not found in the directory …* | Wrong `AZURE_CLIENT_ID` or `AZURE_TENANT_ID` | Step 13 prints the right values; correct them in step 15 |
| **Sign in to Azure:** no subscriptions found, or the subscription isn't found | The role from step 13 is missing (or was given a minute ago), or `AZURE_SUBSCRIPTION_ID` is wrong | Step 13 **Verify**; wait a few minutes; check the variable |
| *… does not have authorization to perform action 'Microsoft.Resources/deployments/write' over scope …* (or another action) | The role is on another resource group, or `AZURE_RESOURCE_GROUP` names a different group | Step 13 for the group in `AZURE_RESOURCE_GROUP`, or correct the variable |
| *Resource group '…' could not be found.* | A typo in `AZURE_RESOURCE_GROUP`, or the group is in another subscription | Steps 10 and 15 |
| *The subscription is not registered to use namespace 'Microsoft.Web'* (or `Microsoft.Sql`, `Microsoft.Storage`) | The resource providers aren't switched on | Step 9, then **Re-run failed jobs** |
| **Azure resources:** a message that the name already exists or is already taken (the site, the SQL server or the storage account) | Another Azure customer uses that name | Choose another `AZURE_NAME` (steps 7–8), change it in step 15, delete anything the failed run created in the resource group (**Overview** → tick → **Delete**), run again |
| **Azure resources:** a message about **quota** (for example the region has a quota of 0 instances for your subscription) | Your subscription may not create that App Service size in that region (common with free and new subscriptions) | Try another region (step 6, set `AZURE_LOCATION`), or ask for more quota: **[Azure portal] Help + support → Create a support request → Service and subscription limits (quotas)**. Upgrade a free account to pay as you go. |
| **Azure resources:** the region isn't accepting new SQL servers, or SQL provisioning is restricted there | Azure limits new SQL servers in that region for your subscription | Use another region for everything: set `AZURE_LOCATION`, delete what the failed run created, run again |
| **Azure resources:** *Password validation failed* … *not complex enough* | The SQL password doesn't meet Azure SQL's rules | Step 14 for a new one, update `AZURE_SQL_ADMIN_PASSWORD` (step 15), run again |
| *Add the AZURE_SQL_ADMIN_PASSWORD secret to the production-azure environment …* | The secret is missing, or is in another environment | Step 15, part 3 |
| **Database:** *Could not find this runner's public IPv4 address* | A temporary network problem on GitHub's side | **Re-run failed jobs** |
| **Database:** *Login failed for user 'wabiadmin'* | The secret was changed, but no run with **Azure resources** has set it on the server yet | Run with **Azure resources** and **Database** ticked |
| *Client with IP address '…' is not allowed to access the server* | In a run: the new firewall rule wasn't active yet. In the portal's query editor: your own IP isn't allowed | Run: **Re-run failed jobs**. Query editor: click the **Allowlist IP …** link it offers, and delete that rule afterwards (step 26) |
| *Bootstrap failed: SchemaNotCurrent (schema_not_current)* | *Create the first Platform Administrator* was ticked without **Database** on a new database | Run again with **Database** and **Create the first Platform Administrator** ticked |
| *Bootstrap failed: AlreadyCompleted (bootstrap_already_completed)* | The first administrator already exists | Nothing to repair. Run again **without** that box (the API and Web app were skipped) |
| *Bootstrap failed: InvalidConfiguration (…)*, for example `password_too_short`, `initialization_secret_missing`, `administrator_email_invalid` | A bootstrap value breaks a rule (step 14), or `WI_ADMIN_EMAIL` isn't an e-mail address | Fix the secret or the variable (step 15), run again |
| *Add the WI_BOOTSTRAP_ADMIN_PASSWORD and WI_BOOTSTRAP_INITIALIZATION_SECRET secrets …* | They're missing (perhaps deleted after step 19) | Only needed with *Create the first Platform Administrator*; untick it, or add them again |
| **API:** *… /health/ready did not answer 200 within 6 minutes …* | The application can't start, or can't reach the database | **Log stream** (above): the first error explains it. Then run the API again |
| The site shows Azure's *Application Error* page, or the container *didn't respond to HTTP pings* in the Log stream | The application can't start (often: the API was never deployed, or a setting is wrong after a manual change) | **Log stream**; run **Azure resources** and **API** to restore the template's settings |
| **Web app:** *… does not return the web app. Check that the API is deployed …* | The API isn't deployed yet: it's the API that serves the web app's files | Run with **API** and **Web app** ticked |
| The address shows *404* at `/`, but `/health/ready` is `Healthy` | The web app was never deployed | Run with **Web app** ticked |
| Jobs wait with *Waiting for review* | Required reviewers are on (step 15) | **Review deployments** → **Approve and deploy** |
| *Branch "…" is not allowed to deploy to production-azure due to environment protection rules.* | The run was started from another branch | **Use workflow from: main** |
| Jobs don't start, and GitHub mentions billing or a spending limit | The account's Actions minutes or spending limit ran out | **[GitHub]** account or organization **Settings → Billing** |
| Custom domain: **Validate** fails | The DNS records aren't visible yet, or they're wrong | Step 23, point 4 (`dig`). Wait up to an hour for DNS. On Cloudflare: **DNS only** |
| A setting you added in the portal disappeared | A run with **Azure resources** rewrote the App Service's settings from the template | Expected (step 18). Put permanent settings in `deploy/azure/main.bicep` |
| After moving an installation: **Test connection** of an existing application fails, though the database is reachable | The old key ring wasn't copied, so the stored connection strings can't be decrypted | Appendix, part 7 |
| E-mails don't arrive | SMTP settings or the outbox | **System Health** shows the e-mail provider and outbox. The committed SMTP settings use port 587; Azure blocks only port 25. |

---

## Appendix: moving an existing installation to Azure

Use this if Wabi Insight already runs on a server of yours, set up with [DEPLOYMENT_LINUX.md](DEPLOYMENT_LINUX.md) or
[DEPLOYMENT_WINDOWS_SERVER_2019.md](DEPLOYMENT_WINDOWS_SERVER_2019.md): SQL Server on that server and the files on its
disk. You bring three things to Azure: the **database** (as a bacpac file), the **key ring** and the **files**.

If your installation already uses Azure SQL and Azure Blob Storage ([DEPLOYMENT_WINDOWS_AZURE.md, Appendix B](DEPLOYMENT_WINDOWS_AZURE.md#appendix-b--the-database-in-azure-sql-instead-of-the-vps)),
this appendix doesn't cover it.

**Before you start:**
- Do steps 1–15. In step 15 you can leave out the two bootstrap secrets: your administrators come with the database.
- The old server must run a version from **9 November 2026 (P13.14) or later**, which has the `storage-copy` command
  (part 9). If it's older, update it first with its usual deployment.
- Plan about an hour in which nobody uses the old site.

1. **Create the Azure services only.** [GitHub] **Actions → Deploy (Azure) → Run workflow**, branch `main`. Tick
   **only Azure resources**; untick **API** and **Web app**. **Run workflow**.
   - **Verify:** the **Azure resources** job is green; the Build job is skipped.
2. **Delete the new, empty database**, so that yours can take its name. [Cloud Shell], with step 8's values:

   ```bash
   az sql db delete --resource-group "$RG" --server "$SQL_SERVER" --name "$SQL_DATABASE" --yes
   az sql db list --resource-group "$RG" --server "$SQL_SERVER" --query "[].name" --output tsv   # no WabiInsightDB (master is the server's own)
   ```

3. **Stop the old site**, so nothing changes during the move. [Old server]:
   - Linux: `systemctl stop wabi-insight-api`
   - Windows (PowerShell as administrator): `Set-Content C:\wabitex-apps\wabi-insight\web\api\app_offline.htm 'Moving'`
4. **Let the old server reach Azure SQL for the move.** [Cloud Shell] — replace the address with the old server's
   public IP address:

   ```bash
   OLD_IP="203.0.113.10"     # CHANGE: the old server's public IP address
   az sql server firewall-rule create --resource-group "$RG" --server "$SQL_SERVER" --name old-server \
     --start-ip-address "$OLD_IP" --end-ip-address "$OLD_IP" --output table
   ```

5. **Copy the database.** [Old server]. The import asks for the **Azure SQL admin password** (step 14) and uses
   `<AZURE_NAME>-sql` and the size `S0` (use your `AZURE_SQL_SKU` if you set one; `Basic` needs `DatabaseEdition=Basic`).
   - **Linux** (as root; the server has the .NET SDK from its setup). It exports with the connection string in
     `/etc/wabi-insight/api.env`, then imports into Azure:

     ```bash
     dotnet tool install --global microsoft.sqlpackage
     export PATH="$PATH:$HOME/.dotnet/tools"
     export DOTNET_ROLL_FORWARD=Major   # lets the tool run on the server's newer .NET
     AZURE_NAME="wabi-insight-acme"     # CHANGE: your deployment name (step 7)
     LOCAL_DB="$(grep '^ConnectionStrings__ConfigurationDatabase=' /etc/wabi-insight/api.env | cut -d= -f2-)"
     sqlpackage /Action:Export /SourceConnectionString:"$LOCAL_DB" /TargetFile:/root/WabiInsightDB.bacpac
     read -rsp 'Azure SQL admin password: ' AZ_SQL_PASSWORD; echo
     sqlpackage /Action:Import /SourceFile:/root/WabiInsightDB.bacpac \
       /TargetConnectionString:"Server=tcp:$AZURE_NAME-sql.database.windows.net,1433;Initial Catalog=WabiInsightDB;User ID=wabiadmin;Password=$AZ_SQL_PASSWORD;Encrypt=True;TrustServerCertificate=False;" \
       /p:DatabaseEdition=Standard /p:DatabaseServiceObjective=S0
     unset AZ_SQL_PASSWORD LOCAL_DB
     ```

     If the export says the connection string is empty, your `api.env` has no `ConnectionStrings__…` line: the
     server uses the connection string in `appsettings.json`. Use that one for `LOCAL_DB`.

   - **Windows:** install SqlPackage as in the Windows Azure guide's appendix (download the *Windows .NET 8* zip from
     `https://aka.ms/sqlpackage-windows` and unpack it to `C:\wabitex-apps\wabi-insight\tools\sqlpackage`). Then, in
     PowerShell as administrator:

     ```powershell
     . {
     $WI = Import-Clixml C:\wabitex-apps\wabi-insight\wabi-insight.vars.xml
     $azureName = 'wabi-insight-acme'                                        # CHANGE: your deployment name (step 7)
     $sp = 'C:\wabitex-apps\wabi-insight\tools\sqlpackage\SqlPackage.exe'
     $bacpac = Join-Path $WI.Backup 'WabiInsightDB.bacpac'
     $localDb = (Get-Content (Join-Path $WI.Root 'web\api\appsettings.Local.json') -Raw | ConvertFrom-Json).ConnectionStrings.ConfigurationDatabase
     & $sp /Action:Export /SourceConnectionString:"$localDb" /TargetFile:$bacpac
     $secure = Read-Host 'Azure SQL admin password' -AsSecureString
     $azPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
     & $sp /Action:Import /SourceFile:$bacpac /TargetConnectionString:"Server=tcp:$azureName-sql.database.windows.net,1433;Initial Catalog=WabiInsightDB;User ID=wabiadmin;Password=$azPassword;Encrypt=True;TrustServerCertificate=False;" /p:DatabaseEdition=Standard /p:DatabaseServiceObjective=S0
     Remove-Variable azPassword, secure, localDb
     }
     ```

   - **Verify:** the import ends with a success message, and **[Azure portal] → SQL databases** lists
     `WabiInsightDB` as **Online**.
6. **Close the firewall again.** [Cloud Shell]:

   ```bash
   az sql server firewall-rule delete --resource-group "$RG" --server "$SQL_SERVER" --name old-server
   ```

7. **Copy the key ring.** Without it, the stored connection strings and provider keys can't be decrypted.
   - [Old server] put the key files (not their folder) in one zip file:
     - Linux: `python3 -m zipfile -c /root/wabi-keys.zip /var/lib/wabi-insight/keys/key-*.xml`
     - Windows: `Compress-Archive -Path C:\wabitex-apps\wabi-insight\keys\* -DestinationPath C:\wabitex-apps\wabi-insight\wabi-keys.zip`
     - Your key ring folder is `DataProtection:KeyRingPath` (Linux: `DataProtection__KeyRingPath` in
       `/etc/wabi-insight/api.env`; Windows: `appsettings.Local.json`). The paths above are the guides' defaults.
   - Bring `wabi-keys.zip` to your computer (Linux: for example with WinSCP or `scp`; Windows: open the Azure portal
     on the server itself). It's a secret: delete every copy when you're done.
   - [Cloud Shell] toolbar → **Manage files → Upload** → `wabi-keys.zip`. It lands in your home folder. Then:

     ```bash
     python3 -m zipfile -l ~/wabi-keys.zip      # key-<id>.xml files, with no folder in front
     az webapp deploy --resource-group "$RG" --name "$AZURE_NAME" --src-path ~/wabi-keys.zip --type zip \
       --target-path /home/data/wabi-insight/keys --clean false --restart false
     rm ~/wabi-keys.zip
     ```

8. **Deploy the application.** [GitHub] **Run workflow**, branch `main`: tick **Database**, **API**, **Web app** and
   **Run tests**. Leave **Azure resources** and **Create the first Platform Administrator** off.
   - **Verify:** the run is green. **Apply migrations** prints `Configuration database is up to date.` or applies the
     migrations your old version didn't have yet. [App Service SSH]: `ls /home/data/wabi-insight/keys` lists the
     same `key-<id>.xml` names as the old server.
9. **Copy the files** to the blob container with `storage-copy`. It copies every file from the old server's disk
   (`Storage:RootPath`) under the same name, and skips files the container already has, so it's safe to repeat. It
   leaves the disk files in place.
   - First copy the storage **connection string**: **[Azure portal] → Storage accounts → `<storage name>` → Security +
     networking → Access keys → key1 → Connection string → Show** → copy icon. It's a secret: don't save it anywhere.
   - **Linux** (as root). It runs the command the way the Linux guide runs `migrate`, with the server's own settings
     from `/etc/wabi-insight/api.env` (including `Storage__RootPath`), plus the two Azure values for this one run:

     ```bash
     read -rsp 'Storage connection string: ' WI_BLOB; echo
     systemd-run --wait --pipe --quiet --uid=wabi-insight --gid=wabi-insight \
       --property=EnvironmentFile=/etc/wabi-insight/api.env --setenv=ASPNETCORE_ENVIRONMENT=Production \
       --setenv=Storage__Provider=AzureBlob --setenv=Storage__AzureBlob__ConnectionString="$WI_BLOB" \
       --working-directory=/opt/wabi-insight/api /usr/bin/dotnet WabiInsight.Api.dll storage-copy
     unset WI_BLOB
     ```

   - **Windows** (PowerShell as administrator; `Storage:RootPath` comes from `appsettings.Local.json`):

     ```powershell
     . {
     Push-Location C:\wabitex-apps\wabi-insight\web\api
     $env:ASPNETCORE_ENVIRONMENT = 'Production'
     $env:Storage__Provider = 'AzureBlob'
     $secure = Read-Host 'Storage connection string' -AsSecureString
     $env:Storage__AzureBlob__ConnectionString = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
     dotnet .\WabiInsight.Api.dll storage-copy
     Remove-Item Env:Storage__Provider, Env:Storage__AzureBlob__ConnectionString
     Remove-Variable secure
     Pop-Location
     }
     ```

   - **Verify:** it ends with `Storage copy finished: … copied, … already in the container, 0 failed.` Run it again:
     everything is now *already in the container*.
   - If it says *set Storage:Provider to AzureBlob first*, the server's own settings set a `Provider`
     (`Storage__Provider` in `api.env` on Linux, `Provider` under `Storage` in `appsettings.Local.json` on Windows):
     remove that line for the copy.
10. **Check, then switch over:**
    - Sign in at `https://<AZURE_NAME>.azurewebsites.net` with your **existing** account.
    - Open an older analysis in **My History** and download its report: the database and the files arrived.
    - **Organization Administration → Applications → Test connection** on an existing application: the key ring
      arrived.
    - Continue with step 20 (go-live checks), step 21 and step 22 (a copy of the key ring), and Part F to move your
      domain to Azure: delete its old **A** record and add the **CNAME** (step 23).
    - Delete the bacpac and zip files on the old server and on your computer: they hold all the data and the keys.
    - Keep the old server stopped for a while, with its backups, before you retire it.

More about the storage settings and `storage-copy`: [DEPLOYMENT.md, *File storage*](DEPLOYMENT.md#file-storage-server-disk-or-azure-blob-storage).
