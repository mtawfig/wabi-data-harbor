# Wabi Insight — Deploy to Microsoft Azure, step by step (GitHub Actions, deployments started by you)

This guide sets up **Actions → Deploy (Azure)**, the third deployment workflow next to *Deploy (Linux)* and
*Deploy (Windows)*. After a one-time setup of about 30 minutes, each deployment is one click with checkboxes for
what to deploy. No server to maintain, no self-hosted runner, and no Azure password stored in GitHub.

## How it works

```
GitHub Actions (GitHub-hosted runner) ──OpenID Connect──▶ Azure resource group
                                                           ├─ App Service (Linux, .NET 10)
                                                           │    runs the API and serves the web app
                                                           │    https://<AZURE_NAME>.azurewebsites.net
                                                           ├─ Azure SQL Database   (configuration database)
                                                           └─ Storage account      (private container for files)
```

- **One address for everything.** The App Service runs the API, which also serves the Angular build from
  `/home/site/web` (`Web:RootPath`, P13.18). The web app and its `/api` share one address, as behind nginx or IIS.
- **Files** (reports, exports, attachments, logos) go to a private Blob Storage container (`Storage:Provider=AzureBlob`,
  P13.14).
- **The encryption key ring** lives on the App Service's persistent storage (`/home/data/wabi-insight/keys`), so it
  survives restarts and deployments.
- **The configuration database** is Azure SQL Database. The SQL firewall lets in Azure services, plus this run's GitHub
  runner while a migration runs; that rule is removed again at the end of the run.
- **GitHub signs in to Azure with OpenID Connect.** A short-lived token is issued for each run, and the identity can
  only change the one resource group.

### Run options

| Checkbox | What happens |
|---|---|
| **Azure resources** | Creates or updates the App Service plan and app, the SQL server and database, and the storage account and container from `deploy/azure/main.bicep`. Tick it the first time, or after the template changes. It also rewrites the app settings from the template. |
| **Database** | Opens the SQL firewall to this run's runner, notes the restore point, runs `dotnet WabiInsight.Api.dll migrate`, then closes the firewall. |
| **API** | Uploads the published API to the App Service, restarts it and waits until `/health/ready` answers. |
| **Web app** | Uploads the Angular build to `/home/site/web` and checks that the site answers with the web app. |
| **Create the first Platform Administrator** | First deployment only: runs `bootstrap` with the bootstrap secrets (step 3). |
| **Run tests** | Unit tests and web lint/tests before anything is deployed (default on). |
| **ref** | Deploys an older commit or tag instead of the selected branch, for example to roll back. |

Jobs run in this order: build → Azure resources → Database → API → Web app. A failed job stops the ones after it.

### Monthly cost (approximate, smallest sizes; check the Azure pricing calculator for your region)

| Resource | Size | About |
|---|---|---|
| App Service plan | B1 (1 core, 1.75 GB) | USD 13–18 |
| Azure SQL Database | S0 (10 DTU, 250 GB); Basic (2 GB) is about USD 5 | USD 15 |
| Storage | Standard LRS, a few GB | under USD 1 |
| GitHub Actions | about 15 runner minutes per full deployment | covered by the free minutes |

## 0. Prerequisites

- An Azure subscription where you are **Owner**, or **Contributor** plus **User Access Administrator**. Step 2 assigns a
  role.
- **Azure Cloud Shell (Bash)**: in the Azure portal, click the `>_` icon at the top. Every command below runs there.
- Admin rights on the GitHub repository, to create the environment in step 3.

## 1. The resource group

**Check:** `az group show --name rg-wabi-insight-prod --query location --output tsv` prints a region, or an error if the
group doesn't exist yet.

**Do:** pick the region close to your users (for example `uaenorth`), then run:

```bash
az group create --name rg-wabi-insight-prod --location uaenorth
for ns in Microsoft.Web Microsoft.Sql Microsoft.Storage; do az provider register --namespace "$ns"; done
```

**Verify:** `az group show --name rg-wabi-insight-prod --output table` shows the group as `Succeeded`.

## 2. The identity GitHub signs in with (OpenID Connect)

**Check:** `az ad app list --display-name wabi-insight-github-deploy --query "[].appId" --output tsv` is empty the first
time.

**Do:** set `REPO` to your GitHub `owner/repository`, then run:

```bash
REPO="mtawfig/wabi-data-harbor"
APP_ID=$(az ad app create --display-name wabi-insight-github-deploy --query appId --output tsv)
az ad sp create --id "$APP_ID" --output none
az ad app federated-credential create --id "$APP_ID" --parameters "{
  \"name\": \"github-production-azure\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:$REPO:environment:production-azure\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"
RG_ID=$(az group show --name rg-wabi-insight-prod --query id --output tsv)
az role assignment create --assignee "$APP_ID" --role Contributor --scope "$RG_ID" --output none
echo "AZURE_CLIENT_ID=$APP_ID"
az account show --query "{AZURE_TENANT_ID:tenantId, AZURE_SUBSCRIPTION_ID:id}" --output table
```

Keep the three printed values for step 3. They are identifiers, not secrets.

**Verify:**

```bash
az ad app federated-credential list --id "$APP_ID" --query "[].subject" --output tsv   # repo:<owner/repo>:environment:production-azure
az role assignment list --assignee "$APP_ID" --scope "$RG_ID" --query "[].roleDefinitionName" --output tsv   # Contributor
```

## 3. The GitHub environment `production-azure`

**Do:** open *Settings → Environments → New environment*, name it `production-azure`, then add the values below.

**Variables:**

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | From step 2 |
| `AZURE_TENANT_ID` | From step 2 |
| `AZURE_SUBSCRIPTION_ID` | From step 2 |
| `AZURE_RESOURCE_GROUP` | `rg-wabi-insight-prod` |
| `AZURE_NAME` | The deployment's name, **globally unique**: 2–40 lowercase letters, digits and hyphens, for example `wabi-insight-prod`. It produces the app `https://wabi-insight-prod.azurewebsites.net`, the SQL server `wabi-insight-prod-sql` and the storage account `wabiinsightprodfiles`. |
| `AZURE_LOCATION` | Optional. Defaults to the resource group's region. |
| `AZURE_APP_SKU` | Optional: `B1` (default), `B2`, `B3`, `S1`, `S2`, `P0v3`, `P1v3`, `P2v3` |
| `AZURE_SQL_SKU` | Optional: `S0` (default), `Basic`, `S1`, `S2` |
| `AZURE_SQL_DATABASE` | Optional. Defaults to `WabiInsightDB`. |
| `AZURE_SQL_ADMIN_LOGIN` | Optional. Defaults to `wabiadmin`. |
| `WI_PUBLIC_URL` | Optional: your own domain once it's set up (step 8). It's used in e-mailed links. |
| `WI_ADMIN_EMAIL` | Optional: e-mail of the first Platform Administrator (defaults to `Bootstrap:AdministratorEmail` in `appsettings.json`) |

**Secrets:**

| Name | Value |
|---|---|
| `AZURE_SQL_ADMIN_PASSWORD` | Password of the SQL administrator, which the application also uses. 16 or more characters mixing upper and lower case, digits and symbols. Avoid `;`, `'` and `"`, because the password goes into a connection string. |
| `WI_BOOTSTRAP_ADMIN_PASSWORD` | First sign-in password of the first Platform Administrator, at least 12 characters (`Security:PasswordMinLength`). It must be changed at first sign-in. |
| `WI_BOOTSTRAP_INITIALIZATION_SECRET` | Any long random string, for example from `openssl rand -base64 32`. |

Optional protection:
- Under *Deployment branches*, choose *Selected branches* and add `main`.
- Leave *Required reviewers* off, or expect to approve each Azure job of a run separately.

**Verify:** the environment page lists 5 to 12 variables and 3 secrets.

## 4. The first deployment

**Do:**
1. Open *Actions → Deploy (Azure) → Run workflow* and choose the `main` branch.
2. Tick **every** box: Azure resources, Database, API, Web app, Create the first Platform Administrator, Run tests.
3. Click *Run workflow*.

The first run takes about 15–20 minutes, mostly because Azure takes a few minutes to create the SQL server.

**Verify:**
1. The run is green, and its summary lists the resources, the restore point and the version.
2. `https://<AZURE_NAME>.azurewebsites.net` shows the landing page.
3. Sign in with `WI_ADMIN_EMAIL` and `WI_BOOTSTRAP_ADMIN_PASSWORD`. You're asked to change the password.
4. In the Azure portal, the storage account's container `wabi-insight-files` holds files once a report has been created.

E-mail and the AI provider work as on any other server: their settings come with `appsettings.json`.

## 5. Everyday deployments

| You changed | Tick |
|---|---|
| Code, no new migrations | API, Web app |
| Code with new migrations | Database, API, Web app |
| Only the Angular app | Web app |
| `deploy/azure/main.bicep`, or a setting the template sets | Azure resources (then API if the app must restart) |

**Rolling back:**
- Run the workflow with **ref** set to the commit or tag that worked, and tick API and Web app.
- Migrations aren't reversed. To restore the database to the moment before a migration, use the restore point from the
  run summary: *SQL database → Restore → Point in time*. It restores into a new database; point
  `AZURE_SQL_DATABASE` at it and tick Azure resources.

## 6. If a run fails

| Job | Look at | Usual causes |
|---|---|---|
| Azure resources | The step's error message | `AZURE_NAME` already taken worldwide; a region without the chosen size or .NET 10; a missing resource provider (step 1); a password that doesn't meet Azure SQL's rules |
| Database | The migrate output | A wrong `AZURE_SQL_ADMIN_PASSWORD`; the runner's address couldn't be found (the step says so). The firewall rule is removed even when the job fails. |
| API | *App Service → Log stream* in the portal | The app can't reach the database, or a setting is missing. The job prints the last health answer. |
| Web app | `https://<AZURE_NAME>.azurewebsites.net/` | The API isn't deployed yet (it serves the files). Run API, then Web app. |

To look at the files on the server, open *App Service → SSH* and run `ls /home/site/web /home/data/wabi-insight/keys`.

## 7. Moving an existing installation (optional)

Do these steps before the first run that ticks **API**.

1. **Database:**
   - Export the existing configuration database to a `.bacpac` file (SQL Server Management Studio → *Tasks → Export
     Data-tier Application*, or `SqlPackage /Action:Export`).
   - Import it into the SQL server (*SQL server → Import database*) under the name in `AZURE_SQL_DATABASE`.
   - Then run with **Database** ticked, so newer migrations are applied.
2. **Key ring:**
   - Copy the old server's key ring folder (`DataProtection:KeyRingPath`) into `/home/data/wabi-insight/keys`, using
     *App Service → Advanced Tools (Kudu) → Debug console*, drag and drop.
   - Without it, the stored connection strings and provider keys can't be decrypted.
3. **Files:**
   - On the old server, set `Storage:Provider=AzureBlob` and the new account's connection string (*Storage account →
     Access keys*), then run `dotnet WabiInsight.Api.dll storage-copy`. It copies every file to the container under the
     same names (see [DEPLOYMENT.md](DEPLOYMENT.md#file-storage-server-disk-or-azure-blob-storage)).
   - Set the old server back afterwards if it keeps running.

## 8. Your own domain (optional)

1. *App Service → Custom domains → Add custom domain*: follow the DNS records it shows, then add the free App
   Service managed certificate.
2. Set `WI_PUBLIC_URL` to `https://your.domain`, then run with **Azure resources** ticked. This updates
   `Platform:BaseUrl`, so e-mailed links use the domain.

## 9. Security notes

- **No Azure secret is stored in GitHub.** The identity from step 2 can only act on the one resource group, and only
  from runs that use the `production-azure` environment of this repository.
- **SQL firewall:** Azure services are allowed, plus the runner's address for the length of a database step only.
- **Secrets in the app settings:**
  - The App Service settings hold the SQL and storage connection strings. App Service encrypts them at rest, and
    they're never sent to the browser.
  - For a stricter setup, move them to Key Vault references.
  - The application signs in to SQL as the administrator from step 3. To narrow this, create a contained database user
    with `db_datareader`, `db_datawriter` and `scripts/sql/create-runtime-login.sql`'s grants, and point the
    connection string at it, keeping the administrator for migrations.
- **Network and storage settings:**
  - HTTPS only, TLS 1.2, FTP off.
  - The blob container is private; deleted blobs and containers are kept 14 days.
  - Azure SQL keeps point-in-time backups (7 days by default).
- **`ReverseProxy:TrustAllProxies=true`** is set on the App Service only. There, the platform's front end is the only
  way in and it appends the real caller to `X-Forwarded-For` (the application reads only that last entry). Never set it
  on a server that can be reached directly.
- **Platform Insights** uses the configuration database login unless `Insights:ConnectionString` is set. This is the
  same warning as on other servers; see [DEPLOYMENT.md](DEPLOYMENT.md).
