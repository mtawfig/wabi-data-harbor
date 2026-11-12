# Wabi Insight on a Linux VPS with Docker — the complete beginner's guide (deployment, CI/CD, backups, moving to Azure)

This guide assumes **little or no Linux knowledge**. It takes you from a fresh **Ubuntu 24.04 LTS** VPS to a live Wabi
Insight at `https://<your domain>` that you open from your PC's browser, with one-click deployments from GitHub, nightly
encrypted backups, and a tested way to move the **same containers** to another server or to an Azure virtual machine
without setting everything up again.

Everything runs in **Docker containers**, started and managed by one command, `wabi-docker`:

```
 Users' browsers (your PC, phones…)
      │ HTTPS (443) — and HTTP (80), which only redirects to HTTPS
      ▼
 ┌──────────── Your VPS: Ubuntu 24.04 LTS + Docker ───────────────────────────────────────────┐
 │  caddy      HTTPS certificate (Let's Encrypt, renewed by itself)                            │
 │     ├── /api, /health ──▶ api        the Wabi Insight API (.NET 10)                         │
 │     └── everything else ─▶ web       the web app (Angular, served by nginx)                 │
 │                               │                                                             │
 │                               ▼                                                             │
 │                           sqlserver  SQL Server 2022 Express (free): the platform database  │
 │                                      WabiInsightDB and the sample organizations' databases  │
 │  Volumes (Docker's storage on the disk): databases, encryption key ring, certificates      │
 │  /opt/wabi-insight: settings (.env, api.env), backups, the stack files                     │
 │  GitHub Actions runner (service) ← receives the deployments you start on GitHub             │
 └────────────────────────────────────────────┬────────────────────────────────────────────────┘
                                              │ outgoing only
                                              ▼
     GitHub (code, images)   Azure Blob Storage (files, backup copies)   AI provider   your SMTP server
```

Only the website (ports 80 and 443) and SSH (port 22, for you) are open to the internet. SQL Server, the API and the
web container are reachable **only** inside Docker's private network on the server.

**Time:** about 3 hours the first time (most of it waiting for downloads and builds).
**Monthly cost:** the VPS (for example about €29 for 4 cores / 8 GB / 80 GB), plus a few euros for Azure Blob Storage.
All the software is free (see *Costs* at the end).

**Other guides:** [DEPLOYMENT_LINUX.md](DEPLOYMENT_LINUX.md) installs the same application on Ubuntu **without** Docker
(nginx + systemd); [DEPLOYMENT_WINDOWS_SERVER_2019.md](DEPLOYMENT_WINDOWS_SERVER_2019.md) on Windows with IIS;
[DEPLOYMENT_AZURE.md](DEPLOYMENT_AZURE.md) on Azure App Service. Use **one** of them for a server, not several.

---

## How to read this guide

- **Where a command runs** is shown at the start of each step:
  - **[Your computer]**: a terminal on your own Windows or Mac computer, or its browser.
  - **[Provider panel]**: your hosting provider's website (for example Melbicom's control panel).
  - **[Domain registrar]**: the website where you manage your domain's DNS.
  - **[Server terminal]**: a terminal connected to the server **as root** (step 9). Its prompt ends with `#`.
  - **[GitHub]**: the GitHub website, signed in with an account that can open the repository `mtawfig/wabi-data-harbor`.
  - **[Azure portal]**: <https://portal.azure.com>.
- Each step is written the same way:
  - **Why:** what the step is for.
  - **Check:** tells you whether the step is already done. If it is, skip **Do**.
  - **Do:** the actions or commands.
  - **You should see:** what happens when it works.
  - **If not:** the usual causes and what to do.
  - **Verify:** proves the step worked. **Don't continue until it does.**
- **Copy and paste** commands exactly. Copy a whole grey box at once and paste it into the terminal:
  - **Windows** (Windows Terminal or PowerShell): **right-click**, or `Ctrl + V`. If Windows Terminal warns that the text
    has several lines, choose **Paste anyway**.
  - **Mac** (Terminal): `Cmd + V`.
  - The terminal runs the lines one after the other. If the last line just sits there, press **Enter**.
- To **copy text from the terminal**, select it with the mouse, then `Ctrl + C` (Windows Terminal) or `Cmd + C` (Mac).
  **`Ctrl + C` with nothing selected stops the running command**; it doesn't copy.
- When the terminal asks for a password or token, **nothing appears while you type or paste**: no dots, no stars. That's
  normal. Paste it and press **Enter**.
- Lines starting with `#` are comments; the terminal ignores them, and everything after a `#` on a line. In **Verify**
  boxes, the comment after a command shows the **expected output**.
- Text in `<angle brackets>` is a placeholder. Replace it with your own value, **without** the brackets.
- **Never paste a password, key or token into a chat, an e-mail or GitHub.** Keep each one in a password manager
  (Bitwarden, 1Password, KeePass…).

### Linux basics you'll use

| You type | What it does |
|---|---|
| `sudo -i` | Makes you `root`, the all-powerful administrator, until you type `exit`. The prompt changes from `$` to `#`. It asks for **your** password. |
| `exit` | Leaves the root shell. Typed again, it closes the connection. |
| `ls -l <folder>` | Lists the files in a folder. |
| `cat <file>` | Shows a file. |
| `nano <file>` | A simple text editor in the terminal. The mouse doesn't move the cursor: use the arrow keys. **`Ctrl + O` then `Enter` saves; `Ctrl + X` exits.** On a Mac too, use the `Control` key, not `Command`. |
| `cd <folder>` | Goes into a folder. `cd ~` goes back to your home folder. |
| `↑` (arrow up) | Brings back your previous command. |
| `Tab` | Completes a file or command name. |
| `clear` | Clears the screen. |

Linux writes folders with `/` (for example `/opt/wabi-insight`), and names are **case-sensitive**: `WabiInsight` and
`wabiinsight` are different.

### Words you'll meet

| Word | Meaning |
|---|---|
| VPS | A virtual server you rent from a hosting company. Yours runs Ubuntu 24.04 LTS, a free version of Linux. |
| Terminal, SSH | A window where you type commands. SSH opens a terminal on the server from your computer, securely. |
| SSH key | A pair of files made on your computer. The **public** key (`.pub`) goes on the server; the **private** key never leaves your computer. |
| root, sudo | `root` is the Linux administrator. `sudo` runs one command as root; `sudo -i` opens a root terminal. |
| Docker | Runs programs in **containers**: each container has everything its program needs, so it runs the same on any Linux server. |
| Image | The packaged program a container starts from, for example `wabi-insight-api`. Images are built by GitHub and stored in a **registry**. |
| Container | A running copy of an image. Stopping or deleting a container doesn't lose data kept in volumes. |
| Volume | Storage that Docker keeps on the disk for a container: the databases, the key ring, the certificates. Volumes survive updates. |
| Docker Compose | Starts several containers that work together, described in one file (`compose.yaml`). |
| Registry, GHCR | Where images are stored. Here: GitHub's container registry, `ghcr.io/mtawfig/…`. The images are **private**. |
| Version (tag) | The 12-character commit number GitHub builds an image from, for example `1a2b3c4d5e6f`. |
| Caddy | The container that holds the HTTPS certificate and passes each request to the web app or the API. |
| `wabi-docker` | This project's command that installs, starts, updates, backs up and restores everything. You always use it with `sudo`. |
| `.env`, `api.env` | The two settings files in `/opt/wabi-insight`: `.env` for the server stack, `api.env` for the application. Only root can read them. |
| Key ring | Files that encrypt the secrets the application stores in the database. **If you lose them, those secrets can't be read.** They are in every backup. |
| Migration | A change to the database's tables that comes with a new version. `wabi-docker deploy` applies it. |
| Bootstrap | Creating the first Platform Administrator, once. |
| CI / CD | CI: GitHub builds and tests every change automatically. CD: installing a version on your server, here with one click. |
| Self-hosted runner | A small GitHub agent installed on your server as a service. It receives the deployments you start on GitHub. |
| SAS URL | A web address with a built-in, time-limited permission to an Azure Blob container. Used for the backup copies. |

## Your information sheet

Fill this in as you go. Keep the secret rows in your password manager, not in a text file.

| Item | Example | Secret? |
|---|---|---|
| Server public IP address | `203.0.113.10` | no |
| Provider's first login (user and password) | `root` / … | **yes** |
| Your own user on the server and its password (step 6) | `alex` / … | **yes** (the password) |
| SSH key passphrase (step 4) | … | **yes** |
| Domain for the site | `insight.example.com` | no |
| E-mail for certificate notices | `it@example.com` | no |
| GitHub fine-grained token, to download the files (step 14; deleted after use) | `github_pat_…` | **yes** |
| SQL Server `sa` password (generated in step 16, in `/opt/wabi-insight/.env`) | … | **yes** |
| **Backup passphrase** (generated in step 16, in `/opt/wabi-insight/.env`) | … | **yes — essential** |
| Azure Storage account name and connection string (step 18) | `wabiinsightfiles` / `DefaultEndpointsProtocol=…` | **yes** (the connection string) |
| Backup container SAS URL (step 33) | `https://…/wabi-backups?sv=…` | **yes** |
| Runner registration token (step 24; valid one hour) | … | **yes** |
| First Platform Administrator e-mail and password (step 27) | `support@wabitex.com` / … | **yes** |
| GitHub classic token `read:packages`, to download images by hand (step 44) | `ghp_…` | **yes** |

---

# Part A — Before you start

## 1. What you need

**Why:** to have everything at hand before you start.

- A **VPS with Ubuntu 24.04 LTS** (choose **24.04**, not a newer version: SQL Server's container and this guide are
  tested on 24.04), at least **4 cores, 8 GB of memory and 60 GB of disk**, with a fixed public IPv4 address. Example:
  Melbicom KVM-4-FJR (Fujairah, UAE).
- A **domain** (or subdomain, for example `insight.example.com`) whose DNS you can change.
- A **GitHub account** that can open the repository `mtawfig/wabi-data-harbor`.
- An **Azure account** for the files and the backup copies (step 18). Optional but strongly recommended: without it,
  files and backups stay only on the VPS.
- A **password manager**.
- On your computer: Windows 10/11 or macOS. Nothing to install: SSH is built in.

**Verify:** you have the server's IP address and its first password (or you gave the provider your SSH key, step 4).

## 2. Open the ports in your provider's control panel

**Where:** [Provider panel].

**Why:** many providers have a firewall in their panel, outside the server. It must let the website and your SSH
connection in.

**Do:** make sure it allows these **incoming** connections:

| Port | Why | From |
|---|---|---|
| 22 (SSH) | Your access to the terminal | **Only your own IP address**, if the panel allows it (strongly recommended) |
| 80 (HTTP) | Certificate validation by Let's Encrypt, and the redirect to HTTPS | Anywhere |
| 443 (HTTPS) | The website (TCP; also UDP for HTTP/3 if the panel asks) | Anywhere |

Outgoing connections must be allowed (they usually are by default): HTTPS (443) to GitHub (`github.com`,
`api.github.com`, `ghcr.io`, `pkg-containers.githubusercontent.com`, `*.actions.githubusercontent.com`), Docker
(`download.docker.com`, `registry-1.docker.io`, `mcr.microsoft.com`), Ubuntu's package servers, Let's Encrypt,
`*.blob.core.windows.net` (Azure Storage), the AI provider (`api.anthropic.com`), and your SMTP server (port 587 or 465).

> If your own IP address changes (home internet often does), the SSH rule shuts you out until you update it. Find the
> **web console** in the provider's panel now (called *Console*, *VNC* or *Recovery*): it works even when SSH doesn't.

**Verify:** the panel lists 22, 80 and 443 as allowed (or the provider has no panel firewall at all).

## 3. Point your domain at the server (DNS)

**Where:** [Domain registrar].

**Why:** browsers find your server through its name, and Let's Encrypt only gives a certificate for a name that points
at the server asking for it.

**Do:**
1. Open the DNS settings of your domain.
2. Add a record of type **A**:
   - **Name/Host:** `insight` (for `insight.example.com`; `@` for the bare domain);
   - **Value/Points to:** your server's IP address;
   - **TTL:** 300 (5 minutes) if you can choose: it makes a later move to another server quicker (Part J).
3. Save.

**You should see:** the new record in the list. Changes take from a few minutes to a few hours to work everywhere.

**Verify:** [Your computer], in a terminal (step 4 shows how to open one): `nslookup insight.example.com` shows your
server's IP address. **If not:** wait 15 minutes and try again; check the record's name for typos.

---

# Part B — Connect to the server

## 4. Open a terminal on your computer and create your SSH key

**Where:** [Your computer].

**Why:** an SSH key is safer than a password and saves you typing it.

**Do, part 1:** open a terminal (no administrator rights needed):
- **Windows:** click **Start**, type `Terminal` (Windows 11) or `PowerShell` (Windows 10), open it.
- **Mac:** press `Cmd + Space`, type `Terminal`, press **Enter**.

Type `ssh -V` and press **Enter**. It prints a version starting with `OpenSSH`. **If not** (Windows says *'ssh' is not
recognized*): **Settings → Optional features → Add a feature → OpenSSH Client**, then open a new terminal.

**Check:** do you already have a key?

```powershell
Test-Path "$env:USERPROFILE\.ssh\id_ed25519.pub"      # Windows PowerShell: True = you have one, skip part 2
```

```bash
ls ~/.ssh/id_ed25519.pub                              # Mac: the file name = you have one, skip part 2
```

**Do, part 2:** create the key (same command on Windows and Mac):

```text
ssh-keygen -t ed25519 -C "wabi-insight admin"
```

1. *Enter file in which to save the key*: press **Enter** (keep the default).
2. *Enter passphrase*: type a passphrase, **Enter**, then again. Store it in your password manager.

**Verify:** show your **public** key:

```powershell
Get-Content "$env:USERPROFILE\.ssh\id_ed25519.pub"    # Windows: one line, ssh-ed25519 AAAA… wabi-insight admin
```

```bash
cat ~/.ssh/id_ed25519.pub                             # Mac: one line, ssh-ed25519 AAAA… wabi-insight admin
```

The `.pub` file is safe to share. The file **without** `.pub` is your private key: never share it, and keep a backup
of it (for example as an attachment in your password manager).

## 5. Connect to the server for the first time

**Where:** [Your computer].

**Do:**
1. Type (with the provider's user, usually `root`, sometimes `ubuntu`, and your server's IP):

   ```text
   ssh root@<server IP>
   ```

2. The first time, SSH shows the server's fingerprint and asks *Are you sure you want to continue connecting?*. Type
   `yes` and press **Enter**.
3. Paste the provider's password when asked (nothing appears) and press **Enter**.
4. If the provider asks you to change the password now: type the current one, then a new one twice. Store it.
5. If the prompt ends with `$` (not root), type `sudo -i` and the same password.

**You should see:** a prompt like `root@vps123:~#`.

**If not:** *Connection timed out* → port 22 is closed (step 2) or the IP is wrong. *Permission denied* → wrong user
or password; check the provider's welcome e-mail.

**Verify:**

```bash
whoami              # root
lsb_release -ds     # Ubuntu 24.04.x LTS
```

Keep this window open until step 8 is done.

## 6. Create your own administrator user

**Where:** [Server terminal], as root.

**Why:** most attacks try `root`. You'll sign in with your own user and use `sudo` when needed.

**Check:** `getent group sudo` lists users who may use `sudo`. If the provider's user (for example `ubuntu`) is
listed, you can keep using it: skip **Do** and use that name wherever this guide says *your user*.

**Do:** choose a user name (lower-case letters and digits). `adduser` asks for its password twice (store it; it's also
what `sudo` asks for), then for details: press **Enter** for each, and **Y** at the end.

```bash
NEW_ADMIN="alex"              # CHANGE: your user name
adduser "$NEW_ADMIN"
usermod -aG sudo "$NEW_ADMIN"
```

**Verify:**

```bash
id "$NEW_ADMIN"     # … groups=…(alex),27(sudo)…
```

## 7. Sign in with your SSH key

**Where:** [Your computer], in a **second** terminal window (keep the first one open).

**Do:** copy your public key to your user on the server. It asks for that user's password once.

```powershell
# Windows PowerShell (replace both placeholders):
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh <your user>@<server IP> "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

```bash
# Mac:
ssh-copy-id <your user>@<server IP>
```

Then sign in with it:

```text
ssh <your user>@<server IP>
```

**You should see:** it asks only for your **key's passphrase** (not the server password), then shows a prompt ending
with `$`.

**Verify:** `sudo -i` asks for your user's password and the prompt ends with `#`. Type `exit` to leave root.

## 8. Recommended: switch off password sign-in

**Where:** [Server terminal], as root, **in the window from step 5 or 7**.

**Why:** with keys only, password-guessing attacks on SSH are useless.

**Check:**

```bash
sshd -T | grep -E '^(passwordauthentication|kbdinteractiveauthentication|permitrootlogin) '
# all three say "no" = already done
```

**Do:**

```bash
cat > /etc/ssh/sshd_config.d/10-wabi-insight.conf <<'EOF'
# Wabi Insight guide, step 8: sign in with SSH keys only, and never directly as root.
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
EOF
sshd -t && systemctl reload ssh
```

**Verify:** in a **new** terminal on your computer, `ssh <your user>@<server IP>` still works with your key. **Only
then** close the old windows. **If not:** use the old window (still connected) to delete the file
(`rm /etc/ssh/sshd_config.d/10-wabi-insight.conf && systemctl reload ssh`) and repeat step 7.

## 9. Open a root terminal

**Where:** [Your computer] → [Server terminal].

From now on, every **[Server terminal]** step runs as root:

```text
ssh <your user>@<server IP>
sudo -i
```

**Verify:** `whoami` prints `root`, and the prompt ends with `#`.

---

# Part C — Prepare the server

## 10. Check the server

**Where:** [Server terminal].

**Why:** to confirm the server matches what you ordered before installing anything.

**Do and verify:**

```bash
lsb_release -ds                                 # Ubuntu 24.04.x LTS
nproc                                           # 4 (cores; 2 works, 4 is comfortable)
free -h | awk '/^Mem:/ {print $2}'              # about 7.7Gi (8 GB)
df -h / | awk 'NR==2 {print $4 " free"}'        # 60G free or more
curl -4 -s https://api.ipify.org; echo          # your server's public IP (the one in DNS, step 3)
```

**If not:** a different Ubuntu version → reinstall the server with 24.04 from the provider's panel (it's empty, nothing
is lost). Less than 8 GB → it works with 4 GB, but slowly (and step 13's swap is then required).

## 11. Updates and firewall

**Where:** [Server terminal].

**Why:** install security updates, and let the firewall allow only SSH and the website.

**Do:**

```bash
apt-get update && apt-get -y upgrade
apt-get install -y ca-certificates curl git openssl unattended-upgrades ufw
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp
ufw --force enable
```

**You should see:** many lines while packages install (2–5 minutes). If a purple screen asks which services to restart,
press **Enter** (keep the defaults). The last line says *Firewall is active and enabled on system startup*.

**Verify:**

```bash
ufw status                       # Status: active, with OpenSSH, 80/tcp, 443/tcp, 443/udp ALLOW
systemctl is-enabled unattended-upgrades    # enabled (security updates install themselves)
```

> Docker opens the ports it publishes by itself, around `ufw`. Here only Caddy publishes ports (80 and 443), which are
> meant to be open; SQL Server and the API publish none, so they stay closed whatever `ufw` says.

**If** a reboot is requested (`ls /var/run/reboot-required` shows the file): type `reboot`, wait one minute,
reconnect (step 9).

## 12. Install Docker

**Where:** [Server terminal].

**Why:** Docker runs the containers. These are Docker's official instructions for Ubuntu: they add Docker's package list
and install the engine and the Compose plugin.

**Check:** `docker compose version` prints *Docker Compose version v2…* → already installed: skip **Do**.

**Do:**

```bash
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" > /etc/apt/sources.list.d/docker.list
apt-get update
apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
systemctl enable --now docker
```

**You should see:** the packages install (1–2 minutes), no red *E:* lines.

**If not:** *E: Unable to locate package docker-ce* → the `echo` line failed; run `cat /etc/apt/sources.list.d/docker.list`:
it must contain `noble stable`. Run the box again.

**Verify:**

```bash
docker --version                 # Docker version 2x.x.x
docker compose version           # Docker Compose version v2.x or v5.x
docker run --rm hello-world | grep 'Hello from Docker'      # Hello from Docker!
systemctl is-enabled docker      # enabled (starts by itself after a reboot)
```

## 13. Clock and swap

**Where:** [Server terminal].

**Why:** a correct clock matters for certificates and sign-in; a little swap protects against running out of memory
during updates.

**Do:**

```bash
timedatectl set-ntp true
if ! swapon --show | grep -q .; then
  fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi
```

The server stays on UTC: that's normal. Each organization's own time zone is set in the application.

**Verify:**

```bash
timedatectl | grep -E 'synchronized: yes'     # System clock synchronized: yes
swapon --show                                  # /swapfile … 2G
```

---

# Part D — Wabi Insight's files and settings

## 14. A GitHub token to download the files

**Where:** [GitHub].

**Why:** the repository is private. A **fine-grained token** is a temporary password that can only **read** this one
repository. You delete it after use.

**Do:**
1. Click your profile picture (top right) → **Settings** → **Developer settings** (bottom of the left menu) →
   **Personal access tokens → Fine-grained tokens** → **Generate new token**.
2. **Token name:** `wabi-insight server download`. **Expiration:** 7 days.
3. **Resource owner:** the owner of the repository (`mtawfig`). If it is an organization, it may need to approve the
   token.
4. **Repository access:** *Only select repositories* → `mtawfig/wabi-data-harbor`.
5. **Permissions → Repository permissions → Contents:** *Read-only*. (*Metadata: Read-only* is added by itself.) Nothing
   else.
6. **Generate token**, then **copy it** (starts with `github_pat_`). It's shown only once. Keep it in your password
   manager until step 15 is done.

**Verify:** the token is listed under **Fine-grained tokens**, with access to one repository.

## 15. Download the repository

**Where:** [Server terminal].

**Why:** the stack files, the `wabi-docker` command and the sample databases' scripts come from the repository.

**Check:** `ls /root/wabi-src/deploy/docker/server/wabi-docker.sh` shows the file → already downloaded: to get the
latest version instead, run `git -C /root/wabi-src pull` (it asks for the user and token like below).

**Do:**

```bash
git clone https://github.com/mtawfig/wabi-data-harbor.git /root/wabi-src
```

1. *Username for 'https://github.com'*: type your GitHub user name, **Enter**.
2. *Password for 'https://…@github.com'*: paste the **token** from step 14 (not your GitHub password; nothing appears),
   **Enter**.

**You should see:** *Cloning into '/root/wabi-src'…* and a few progress lines.

**If not:** *Authentication failed* → the token was pasted wrongly, expired, or has no access to this repository: make a
new one (step 14). *Repository not found* → same causes.

**Verify:**

```bash
ls /root/wabi-src/deploy/docker/server/      # Caddyfile  api.env.example  compose.yaml  env.example  wabi-docker.sh
git -C /root/wabi-src log -1 --format='%h %s'  # the latest commit of main
```

## 16. Install `wabi-docker` and the stack files

**Where:** [Server terminal].

**Why:** this copies the `wabi-docker` command to `/usr/local/sbin`, the stack files to `/opt/wabi-insight`, creates
the two settings files with **new random secrets** (the SQL Server password and the backup passphrase), and schedules
the nightly backup.

**Check:** `wabi-docker version` prints a number → already installed. Running **Do** again is safe: it updates the
command and the stack files and **keeps** your settings files.

**Do:**

```bash
bash /root/wabi-src/deploy/docker/server/wabi-docker.sh install /root/wabi-src
```

**You should see:** several `==>` lines, then *Installed: wabi-docker version 1, stack files in /opt/wabi-insight.*

**Verify:**

```bash
wabi-docker version                         # 1
ls -l /opt/wabi-insight                      # Caddyfile, compose.yaml, .env and api.env (-rw------- root root), backups, samples, sql, state
cat /etc/cron.d/wabi-insight-backup | tail -1   # 30 2 * * * root … wabi-docker backup …
```

**Now save the backup passphrase** — this is the most important secret of the installation: without it, **no backup
can ever be restored**, here or on another server.

```bash
grep '^WI_BACKUP_PASSPHRASE=' /opt/wabi-insight/.env
```

Copy the value after `=` into your password manager as *Wabi Insight backup passphrase*. Then `clear` the screen.

## 17. The stack settings: `.env`

**Where:** [Server terminal].

**Why:** `.env` tells the stack your domain and the e-mail for certificate notices.

**Do:** open the file:

```bash
nano /opt/wabi-insight/.env
```

Change only these two lines (use the arrow keys to move; delete the example and type yours):

```text
WI_DOMAIN=insight.example.com        ← your domain from step 3, without https://
WI_ACME_EMAIL=it@example.com         ← your e-mail for certificate notices
```

Save with `Ctrl + O`, `Enter`, and leave with `Ctrl + X`.

What the other lines are (leave them as they are for now):

| Line | Meaning |
|---|---|
| `WI_REGISTRY=ghcr.io/mtawfig` | Where the images come from. |
| `WI_SA_PASSWORD=…` | SQL Server's administrator password, generated. Used only by `wabi-docker`. **Don't change it after step 20.** |
| `WI_MSSQL_EDITION=Express` | The free edition of SQL Server. |
| `WI_BACKUP_PASSPHRASE=…` | Encrypts the backups (saved in step 16). |
| `WI_BACKUP_SAS_URL=` | Filled in step 33, for copies in Azure. |
| `WI_BACKUP_KEEP=7` | How many backups to keep on the server. |
| `WI_CADDY_OPTIONS=` | Empty. Used only while testing a copy on a new server (step 46). |

**Verify:**

```bash
grep -E '^WI_(DOMAIN|ACME_EMAIL)=' /opt/wabi-insight/.env     # your domain and e-mail
```

## 18. Azure Storage for the files (recommended)

**Where:** [Azure portal], then [Server terminal].

**Why:** the reports, exports, attachments and logos then live in Azure Blob Storage in the UAE, not on the VPS: they
survive a lost server, and a move to another server needs no file copy.

**Check:** you already have a storage account for Wabi Insight → skip to **Do, part 3**.

**Do, part 1 — create the storage account:**
1. In the portal, search **Storage accounts** → **+ Create**.
2. **Basics:**
   - **Subscription:** yours. **Resource group:** **Create new** → `wabi-insight`.
   - **Storage account name:** lower-case letters and digits, unique in Azure, for example `wabiinsightfiles`.
   - **Region:** **(Middle East) UAE North**.
   - **Primary service:** *Azure Blob Storage or Azure Data Lake Storage Gen 2*.
   - **Performance:** *Standard*. **Redundancy:** *Locally-redundant storage (LRS)* (or *Zone-redundant*): both keep the
     data in the UAE.
3. **Advanced:** leave *Require secure transfer* **on**, *Minimum TLS version* **1.2**, *Allow enabling anonymous access
   on individual containers* **off**.
4. **Networking:** *Enable public access from all networks* for now (step 18, part 4 limits it to your server).
5. **Data protection:** tick *Enable soft delete for blobs* (7 days): a deleted file can be recovered for a week.
6. **Review + create** → **Create**. Wait for *Your deployment is complete* → **Go to resource**.

**Do, part 2 — two private containers:**
1. In the storage account, left menu **Data storage → Containers** → **+ Container**.
2. Name `wabi-insight-files`, **Anonymous access level:** *Private (no anonymous access)* → **Create**.
3. Again: `wabi-backups`, *Private* → **Create** (used in step 33).

**Do, part 3 — the connection string:** left menu **Security + networking → Access keys** → **key1 → Connection
string → Show** → copy it (starts with `DefaultEndpointsProtocol=https;AccountName=`).

**Do, part 4 — only your server may connect (do it now):** **Security + networking → Networking** → *Public network
access*: **Enabled from selected virtual networks and IP addresses** → **Firewall → Add IP range**: your server's IP
(step 10), and your own computer's IP if you want to browse the containers in the portal → **Save**.

**Do, part 5 — put it in `api.env`:** [Server terminal]:

```bash
nano /opt/wabi-insight/api.env
```

Change `Storage__Provider=FileSystem` to `Storage__Provider=AzureBlob`, remove the `# ` at the start of the next three
`Storage__AzureBlob__…` lines, and replace the example connection string with yours (one line, no spaces, no quotes):

```text
Storage__Provider=AzureBlob
Storage__AzureBlob__ConnectionString=DefaultEndpointsProtocol=https;AccountName=wabiinsightfiles;AccountKey=…;EndpointSuffix=core.windows.net
Storage__AzureBlob__ContainerName=wabi-insight-files
Storage__AzureBlob__ReadFromDiskWhenMissing=false
```

Save (`Ctrl + O`, `Enter`) and leave (`Ctrl + X`).

**Verify:** after the first deployment (step 26) you run `sudo wabi-docker storage-check`; it must end with *Storage
check passed*.

> Without Azure, leave `Storage__Provider=FileSystem`: the files stay in a Docker volume on the VPS and are included in
> every backup (step 32).

## 19. The application's settings: `api.env`

**Where:** [Server terminal].

**Why:** `api.env` holds this server's application settings. Anything in it overrides `appsettings.json` inside the
image.

**Do:** look at it (step 18 already changed the storage lines):

```bash
cat /opt/wabi-insight/api.env
```

| Line | When to change it |
|---|---|
| `ConnectionStrings__ConfigurationDatabase=` | **Leave it empty**: step 20 fills it in. |
| `Storage__…` | Step 18. |
| `Insights__ConnectionString=` | **Leave it empty**: step 29 fills it in. |
| `# Email__…`, `# Ai__Anthropic__ApiKey=` | Only to use your **own** SMTP server or AI key instead of the ones in the repository's `appsettings.json`: remove the `# ` and fill in the value. |

The rules of the file: one setting per line, `Section__Key=value` with **two** underscores, no quotes, no spaces around
`=`. Lines starting with `#` are ignored.

**Verify:** `stat -c '%U %a' /opt/wabi-insight/api.env` prints `root 600` (only root can read it).

## 20. Create the database (`init`)

**Where:** [Server terminal].

**Why:** this starts SQL Server for the first time, creates the platform database `WabiInsightDB` and a dedicated login
`wabi_app` for the application (with a new random password), and writes the connection string into `api.env`. The
application never uses the `sa` administrator.

**Check:** `grep -c '^ConnectionStrings__ConfigurationDatabase=Server=' /opt/wabi-insight/api.env` prints `1` → done.

**Do:**

```bash
wabi-docker init
```

**You should see:** Docker downloads SQL Server the first time (about 2 GB, 1–3 minutes), then:

```text
SQL Server is running: Express Edition (64-bit) 16.0.xxxx.x
==> Creating the database WabiInsightDB and its login wabi_app
Created. The connection string is in /opt/wabi-insight/api.env (the password is never shown).
```

**If not:**
- *set your own WI_DOMAIN* → step 17.
- *SQL Server did not start within 3 minutes* → the log lines printed above say why. *Not enough memory* → check `free -h`
  (it needs at least 2 GB free). Then run `wabi-docker init` again.

**Verify:**

```bash
docker ps --format '{{.Names}}  {{.Status}}'                                 # wabi-insight-sqlserver-1  Up … (healthy)
grep -c '^ConnectionStrings__ConfigurationDatabase=Server=sqlserver' /opt/wabi-insight/api.env   # 1
```

The application isn't running yet: its images arrive with the first deployment from GitHub (Part E).

---

# Part E — CI/CD: deploy from GitHub with one click

## 21. How CI and CD work in this repository

| Part | File | When it runs | What it does |
|---|---|---|---|
| **CI**, already active | `.github/workflows/ci.yml` | **Automatically** on every pull request and every merge to `main` | Builds and tests everything, builds both Docker images and starts the **whole server stack** of this guide with `wabi-docker` (HTTPS, sign-in, backup and restore). A red ✗ on a pull request means don't merge it yet. |
| **CD** | `.github/workflows/deploy-docker.yml` (**Deploy (Docker)**) | **Only when you click Run workflow** | GitHub builds the two images and publishes them, privately, to `ghcr.io/mtawfig`. The runner on your server then runs `sudo wabi-docker deploy <version>`. |

```
 You: GitHub → Actions → Deploy (Docker) → Run workflow  (branch main; run tests: on; ref: empty)
   ▼
 GitHub's computers: tests → build wabi-insight-api and wabi-insight-web → push to ghcr.io/mtawfig/…:<version>
   ▼
 The runner on YOUR server (outgoing connections only; no port is opened)
   └─ sudo wabi-docker deploy <version>
        download the images → back up → migrate the database → start the new version
        → wait until /health/ready is Healthy → check https://<domain>
        (not healthy → the previous version is started again automatically, and the run fails)
```

**No server secret is stored in GitHub.** The server keeps its own settings; GitHub only builds images. The images are
private because they contain the repository's `appsettings.json`.

## 22. A GitHub environment for the server

**Where:** [GitHub] → the repository → **Settings → Environments**.

**Why:** each deployment is recorded there (history, link to the site, optional approval).

**Check:** `production-docker` is listed.

**Do:**
1. **New environment** → name `production-docker` → **Configure environment**.
2. **Deployment branches and tags:** *Selected branches and tags* → **Add deployment branch or tag rule** → `main`.
3. Optional: **Required reviewers**: add yourself, to approve each deployment before it reaches the server.
4. **Environment variables → Add variable:** name `WI_PUBLIC_URL`, value `https://<your domain>`.

Branch rules and required reviewers need GitHub Pro, Team or Enterprise for **private** repositories. On GitHub Free
the workflow still runs, without those rules.

**Verify:** `production-docker` is listed with the variable.

## 23. The runner user

**Where:** [Server terminal].

**Why:** the runner runs as an ordinary user, `github-runner`. Its only `root` right will be `wabi-docker deploy`
(step 25). It is **not** in the `docker` group (that would equal full root).

**Check:** `id github-runner` prints a line → exists: skip **Do**.

**Do:**

```bash
useradd --create-home --shell /bin/bash github-runner
```

**Verify:**

```bash
id github-runner                  # uid=…(github-runner) gid=…(github-runner) groups=…(github-runner)
sudo -l -U github-runner          # User github-runner is not allowed to run sudo on …
```

## 24. Install the runner (as a service)

**Where:** [Server terminal], then [GitHub], then [Server terminal].

**Why:** the runner receives the deployments you start on GitHub.

**Check:**

```bash
ls /opt/actions-runner/config.sh 2>/dev/null
systemctl list-units --all 'actions.runner.*' --no-legend      # a line = installed: skip Do
```

**Do, part 1:** download the current runner and check its fingerprint against GitHub's release notes:

```bash
mkdir -p /opt/actions-runner && cd /opt/actions-runner && pwd      # /opt/actions-runner
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

**If** the check prints `FAILED` instead of `OK`: stop, delete the folder (`cd / && rm -rf /opt/actions-runner`) and
start again.

**Do, part 2:** [GitHub] the repository → **Settings → Actions → Runners → New self-hosted runner** → **Linux**,
architecture **x64**. In the *Configure* box, copy only the long code after `--token`. It's valid for **one hour**.

**Do, part 3:** [Server terminal] register the runner with the label `wabi-insight-docker` and install it as a service
that runs as `github-runner`. Paste the token when asked (nothing appears):

```bash
cd /opt/actions-runner
read -rsp 'Runner registration token: ' RUNNER_TOKEN; echo
runuser -u github-runner -- ./config.sh --unattended --url "https://github.com/mtawfig/wabi-data-harbor" \
  --token "$RUNNER_TOKEN" --name "$(hostname)-wabi-docker" --labels wabi-insight-docker --work _work --replace
./svc.sh install github-runner
./svc.sh start
unset RUNNER_TOKEN
```

**You should see:** *√ Runner successfully added*, *√ Settings Saved*, then the service starts.

**If not:** *Http response code: NotFound* or *token expired* → get a new token (part 2) and run part 3 again.

**Verify:**

```bash
./svc.sh status | grep -E 'Active: active \(running\)'
journalctl -u 'actions.runner.*' -n 5 --no-pager | grep -i 'Listening for Jobs'
```

[GitHub] **Settings → Actions → Runners** shows the runner **Idle**, with the labels `self-hosted`, `Linux`, `X64` and
`wabi-insight-docker`.

## 25. The sudo rule

**Where:** [Server terminal].

**Why:** the runner may run **only** `wabi-docker version` and `wabi-docker deploy <version>` as root, nothing else.
`wabi-docker` belongs to root, so the runner can't change it.

**Check:** `sudo -l -U github-runner | grep wabi-docker` prints the two commands → done.

**Do:**

```bash
cat > /etc/sudoers.d/wabi-docker <<'EOF'
# GitHub Actions runner may run Wabi Insight's deployments as root, and nothing else (docs/DEPLOYMENT_LINUX_DOCKER.md, step 25).
github-runner ALL=(root) NOPASSWD: /usr/local/sbin/wabi-docker version, /usr/local/sbin/wabi-docker deploy *
EOF
chmod 440 /etc/sudoers.d/wabi-docker
visudo -cf /etc/sudoers.d/wabi-docker
```

**Verify:**

```bash
visudo -cf /etc/sudoers.d/wabi-docker                                             # … parsed OK
runuser -u github-runner -- sudo -n /usr/local/sbin/wabi-docker version          # 1
runuser -u github-runner -- sudo -n /usr/bin/id 2>&1 | head -1                     # sudo: a password is required
```

The last line proves the runner has no other root rights.

## 26. The first deployment

**Where:** [GitHub], then [Server terminal], then [Your computer]'s browser.

**Do:**
1. [GitHub] the repository → **Actions** → **Deploy (Docker)** (left list) → **Run workflow**.
2. **Use workflow from:** `main`. **Run the unit tests…:** ticked. **ref:** empty → **Run workflow**.
3. Click the new run to watch it. With required reviewers (step 22), click **Review deployments → Approve**.

**You should see:**
- **Build and publish the images** (on GitHub): 8–15 minutes the first time. Its summary lists the two images, for
  example `ghcr.io/mtawfig/wabi-insight-api:1a2b3c4d5e6f`.
- **Deploy to the Docker server** (on your runner): 2–5 minutes the first time (it downloads about 700 MB). Its log
  shows `==> Downloading version …`, `==> Applying database changes …`, `==> Starting version …`, `==> Checking the site
  through HTTPS` and ends with *Version … is running.*
- Both jobs green ✓.

**If not:**

| What you see | Cause | Fix |
|---|---|---|
| The deploy job waits *Waiting for a runner to pick up this job* | The runner is stopped or has another label | Step 24 **Verify**; `cd /opt/actions-runner && ./svc.sh start` |
| *wabi-docker is not installed* | Step 16 not done | Step 16 |
| *The runner user may not run … with sudo* | Step 25 not done | Step 25 |
| *the images of version … could not be downloaded* | The images job failed, or the package isn't linked to the repository | Check the first job is green. [GitHub] your profile → **Packages** → `wabi-insight-api` → **Package settings → Manage Actions access**: add the repository with **Read** |
| *the migration failed* | Database or `api.env` problem | The message above it says why; `wabi-docker logs sqlserver` |
| *did not report Healthy* | The application couldn't start | The API's last log lines are printed in the job; most often a typo in `api.env` (step 19) |
| *https://… does not answer yet* (a warning, the job is still green) | The certificate is being issued, or DNS/ports | Wait 1–2 minutes, then `wabi-docker status`; check DNS (step 3) and ports 80/443 (steps 2 and 11) |

**Verify:**

```bash
wabi-docker status
```

- *Version installed:* the version of the run.
- All four containers *running* (sqlserver *healthy*).
- *Application health:* `"status":"Healthy"` with `configuration-database`, `database-schema`, `file-storage` and
  `key-ring` all *Healthy*.

Then check the files storage (step 18):

```bash
wabi-docker storage-check        # … Storage check passed: a test file was written, read back identical and deleted.
```

[Your computer] open `https://<your domain>` in your browser: the Wabi Insight landing page appears with a **padlock**,
and `http://<your domain>` switches to `https://` by itself.

[GitHub] **Settings → Environments → production-docker** lists the deployment, and your profile → **Packages** lists
`wabi-insight-api` and `wabi-insight-web`, both **Private**.

---

# Part F — First use, from your PC's browser

## 27. Create the first Platform Administrator (once)

**Where:** [Server terminal].

**Why:** nobody can sign in until there is a first Platform Administrator.

**Do:**

```bash
wabi-docker bootstrap
```

It asks for:
1. **E-mail** of the first Platform Administrator (a real mailbox: password resets go there), for example
   `support@wabitex.com`;
2. **Full name** (Enter keeps *Platform Administrator*);
3. a **first password** (12+ characters, twice; nothing appears). You'll change it at the first sign-in.

**You should see:** *Bootstrap completed. … The administrator must change the password at first sign-in.* and *Done.*

**If not:** *bootstrap_already_completed* → it was done before; sign in with that account, or use **Forgot password?**
on the sign-in page.

## 28. Sign in from your PC and check the platform

**Where:** [Your computer]'s browser.

**Do:**
1. Open `https://<your domain>` → **Sign in**.
2. Enter the e-mail and the first password from step 27 → you are asked to **choose a new password** → do it and store
   it.
3. In the platform menu, open **System Health**.

**You should see:** the platform overview. On **System Health**: the database, the API, the files folder and the key
ring *Healthy*; *e-mail* healthy when the SMTP settings work. *deployment-settings* may be *Degraded* until step 29.

**Verify:** sign out and sign in again with the new password. Then click **Forgot password?**, enter the
administrator's e-mail, and check that the reset e-mail arrives (this proves e-mail works). Close it without
changing anything.

## 29. The Platform Insights login

**Where:** [Server terminal].

**Why:** Platform Insights (organization dashboards) should read the database through a login that can read only the
`insights` views. Without it, *deployment-settings* stays *Degraded*.

**Do:**

```bash
wabi-docker insights-login
```

**You should see:** *Created, saved in api.env, and the API restarted with it.*

**Verify:** after a minute, **System Health** shows *deployment-settings* without the *Insights:ConnectionString*
remark.

## 30. The sample organizations (for testing)

**Where:** [Server terminal], then [Your computer]'s browser.

**Why:** two complete demo organizations with their own databases, users and questions, to test everything before
real customers: **Gulf Aviation Services** (GAS: 3 databases, 4 users) and **Wabi Bank** (WBK: 5 databases, 8 users).
Details: [scripts/sql/samples/README.md](../scripts/sql/samples/README.md) and
[scripts/sql/samples/wabi-bank/README.md](../scripts/sql/samples/wabi-bank/README.md).

**Do, part 1 — load them** (10–25 minutes; leave the terminal open):

```bash
wabi-docker samples
```

`wabi-docker samples gas` or `wabi-docker samples bank` loads only one of them.

**You should see:** one line per script, then a list like:

```text
Organization administrator: faisal.alshamsi@gulfaviation.example
  FLIGHT-OPS  Server=sqlserver;Database=FlightOperationsDB;User Id=flightops_reader;Password=…;Encrypt=True;TrustServerCertificate=True;
  …
```

`wabi-docker sample-connections` shows this list again at any time.

**Do, part 2 — connect each application** (once per application; 3 for GAS, 5 for Wabi Bank):
1. [Your computer] open `https://<your domain>` → **Sign in** as the organization administrator shown in the list
   (GAS: `faisal.alshamsi@gulfaviation.example`; Wabi Bank: the e-mail shown for it), password `Wabi#Sample#2026` →
   choose a new password when asked.
2. **Organization Administration → Applications** → open an application → **Replace connection** → paste its
   connection string from the list (the whole line after the code) → **Save**.
3. **Test connection** → it must say **read-only verified**. Then **Approve**.
4. Repeat for each application of that organization.

**If not:** *Test connection* fails → check the string was pasted completely (it starts with `Server=sqlserver;`).

**Verify:** sign in as another sample user (for example `khalid.alsuwaidi@gulfaviation.example`, the CEO) → **Ask** →
choose *Flight Operations* → ask one of the example questions shown → you get an answer with a table or chart.

## 31. Protect the sample accounts

**Why:** the sample users' first password (`Wabi#Sample#2026`) is published in the repository, and their e-mails are
fictional. While the site is reachable on the internet, anyone could sign in to an account nobody has used yet.

**Do (choose one):**
- **Sign in once to every sample account** (4 for GAS, 8 for Wabi Bank; the lists are in the two READMEs) and set your own
  password: the first sign-in forces a change. Or
- If you don't need Wabi Bank, load only GAS (`wabi-docker samples gas`) and do it for its 4 accounts.

The sample databases' own SQL logins have published passwords too, but SQL Server is **not** reachable from outside the
server in this setup, so they can only be used by the application.

---

# Part G — Backups

## 32. How the backups work

`wabi-docker backup` makes **one encrypted file** in `/opt/wabi-insight/backups`, named
`wabi-backup-<date>-<time>.tar.gz.enc`, containing:
- every database (`WabiInsightDB` and the sample ones), backed up by SQL Server while it runs;
- the SQL logins (so a restore on another server works);
- the **key ring** and the files volume;
- the settings `.env` and `api.env`, and the installed version.

It runs **every night at 02:30** (server time, UTC) by itself, keeps the newest 7 on the server, and — once step 33
is done — copies each one to Azure. `wabi-docker deploy` also makes one before every update.

**Do — the first backup by hand:**

```bash
wabi-docker backup
```

**You should see:** the databases one by one, then *Backup: /opt/wabi-insight/backups/wabi-backup-….tar.gz.enc (…M)*,
and a warning that `WI_BACKUP_SAS_URL` is empty until step 33.

**Verify:** `wabi-docker backups` lists it.

## 33. Copies in Azure Blob Storage

**Where:** [Azure portal], then [Server terminal].

**Why:** a backup that exists only on the VPS is lost with the VPS. The copy in Azure (UAE North) is what Part J uses
to move to another server.

**Do, part 1 — a SAS URL for the container:**
1. Storage account (step 18) → **Data storage → Containers** → `wabi-backups` → **Settings → Shared access tokens**.
2. **Signing method:** *Account key*. **Permissions:** tick **Read, Add, Create, Write, List** (not Delete).
3. **Start:** now. **Expiry:** in 2 years (put a reminder in your calendar to renew it).
4. **Allowed IP addresses:** your server's IP (optional, safer). **Allowed protocols:** *HTTPS only*.
5. **Generate SAS token and URL** → copy the **Blob SAS URL** (starts with
   `https://<account>.blob.core.windows.net/wabi-backups?sp=…`).

**Do, part 2 — put it in `.env`:** [Server terminal]:

```bash
nano /opt/wabi-insight/.env
```

Paste it after `WI_BACKUP_SAS_URL=` (one line, no quotes). Save and leave.

**Do, part 3 — test:**

```bash
wabi-docker backup
wabi-docker backups
```

**You should see:** *Copying it to Azure Blob Storage* without error, and the file listed under **In Azure Blob
Storage**.

**If not:** *the copy to Azure Blob Storage failed* → the SAS URL is incomplete, expired, misses *Write/Create*, or the
storage firewall (step 18, part 4) doesn't list the server's IP.

**Recommended:** storage account → **Data management → Lifecycle management → Add a rule**: *Delete blobs* in container
`wabi-backups` *created more than 30 days ago*. Old copies then disappear by themselves.

## 34. Test a restore (on this server)

**Why:** a backup you never restored is not a backup. Do this once now, while nobody uses the site (it takes the site
offline for 1–3 minutes).

**Do:**

```bash
wabi-docker backups                                   # copy the newest file name
wabi-docker restore <newest file name>
```

**You should see:** *Decrypting …*, *Restoring the SQL logins*, *Restoring the databases* (one line each), *Restoring
the key ring and the files*, *Starting the application*, then *Restored and running*.

**Verify:** [Your computer] sign in at `https://<your domain>` with your administrator; a sample user can still ask a
question (connections still work: the key ring came back with the databases).

---

# Part H — The full test after deployment

**Where:** [Your computer]'s browser, and [Server terminal] for the last checks. Tick each line.

| # | Test | Expected |
|---|---|---|
| 1 | Open `http://<your domain>` | It switches to `https://`, padlock shown |
| 2 | Landing page, then **العربية** | The layout switches to right-to-left |
| 3 | Sign in as the Platform Administrator → **System Health** | Everything *Healthy* (e-mail, database, schema, files, key ring, deployment settings) |
| 4 | **Organizations** | GAS and Wabi Bank listed |
| 5 | **Forgot password?** with a real mailbox | The reset e-mail arrives; the link opens the reset page |
| 6 | As the GAS administrator: **Applications** | 3 applications, each *connected* and approved |
| 7 | As the GAS CEO: **Ask** an example question, in English | Answer with a table or chart |
| 8 | The same in Arabic | Arabic answer |
| 9 | **Export** the answer to PDF, Word and Excel | The three files download and open |
| 10 | **Share** the answer with another GAS user; sign in as that user | It appears under *Shared with me* |
| 11 | As Arjun (GAS, `SELF` scope), ask "Show my net pay for each month of 2025" | Only his own payslips |
| 12 | Wabi Bank: same as 6–7 with its administrator and a manager | Answers |
| 13 | Upload an organization logo (Organization Settings) | It shows; with Azure, a new blob appears in `wabi-insight-files` |
| 14 | Wait a night; next morning `cat /var/log/wabi-insight-backup.log` | A *Backup:* line, copied to Azure |
| 15 | Run **Deploy (Docker)** again (step 36) | Green; the site stays up except a few seconds |
| 16 | `reboot` the server; wait 2 minutes | The site comes back by itself; `wabi-docker status` all running |

Any line that fails: note what you saw, then see *Troubleshooting* at the end.

---

# Part I — Everyday operations

## 35. Where to look

| You want to | Command ([Server terminal]) |
|---|---|
| See everything at once | `wabi-docker status` |
| Follow the API's log live (`Ctrl + C` stops) | `wabi-docker logs api` |
| Other logs | `wabi-docker logs caddy` · `wabi-docker logs web` · `wabi-docker logs sqlserver` |
| Deployment and backup history | `cat /opt/wabi-insight/state/deploy.log` |
| Nightly backup log | `cat /var/log/wabi-insight-backup.log` |
| Restart the application (after changing `api.env`) | `wabi-docker restart` |
| Stop / start everything | `wabi-docker stop` · `wabi-docker start` |
| All commands | `wabi-docker help` |

## 36. Deploy a new version

When a pull request is merged into `main`: [GitHub] **Actions → Deploy (Docker) → Run workflow** (`main`, tests
ticked). That's all. Migrations are applied automatically after a backup. If the new version doesn't become healthy,
the previous one is started again and the run fails (red): the site stays up.

## 37. Roll back to an earlier version

**Fastest** (the previous version is still on the server): [Server terminal]

```bash
cat /opt/wabi-insight/state/previous-tag          # the version before the current one
wabi-docker deploy <that version> --no-pull
```

**Any earlier version:** [GitHub] **Run workflow** with **ref** set to that commit (from the repository's commit list or
`deploy.log`).

A migration is never undone by a roll back. If a version changed the database in a way the older one can't read,
restore the backup made just before that update (`wabi-docker backups`, then `wabi-docker restore <file>`).

## 38. Update `wabi-docker` and the stack files

The deploy job shows a yellow warning *The server has wabi-docker version X, this version of the repository has Y*
when the repository has a newer `wabi-docker`. Then: [GitHub] a new fine-grained token (step 14), and [Server
terminal]:

```bash
git -C /root/wabi-src pull          # user name and the new token
bash /root/wabi-src/deploy/docker/server/wabi-docker.sh install /root/wabi-src
wabi-docker version                 # the new number
```

Your settings and data are kept. Then run the deployment again.

## 39. Server updates and reboots

Security updates install themselves (step 11). Once a month: [Server terminal]

```bash
apt-get update && apt-get -y upgrade
ls /var/run/reboot-required 2>/dev/null && reboot
```

Everything (Docker, the containers, the runner) starts again by itself after a reboot.

SQL Server's and Caddy's images are updated on purpose, not by every deployment: once a quarter, at a quiet time, after
`wabi-docker backup`:

```bash
cd /opt/wabi-insight && docker compose --project-name wabi-insight pull sqlserver caddy && wabi-docker start
```

---

# Part J — Move the same containers to another server or to Azure

The application is the same set of images on any server; everything that makes it *yours* (databases, logins, key
ring, files, settings, version) is in one backup file. Moving means: prepare a new server with Docker, install
`wabi-docker`, restore the latest backup, switch DNS. Nothing is set up again.

**A rehearsal first.** Steps 41–45 build the copy on the new server **without** switching DNS: users keep working on
the old server, and you test the copy. That's the "ready copy" test; do it before the real move.

## 40. What you need

- The **backup passphrase** (step 16) and the **SAS URL** of `wabi-backups` with **Read** and **List** (step 33).
- A **GitHub classic token** with `read:packages` (step 44), to download the private images.
- A fresh **backup** in Azure: [old server] `wabi-docker backup` (and check it's listed by `wabi-docker backups` under
  *In Azure*).

## 41. Create the Azure virtual machine (UAE North)

**Where:** [Azure portal].

**Do:**
1. Search **Virtual machines** → **+ Create → Azure virtual machine**.
2. **Basics:**
   - **Resource group:** `wabi-insight` (step 18). **Name:** `wabi-insight-vm`.
   - **Region:** **(Middle East) UAE North**. **Availability options:** *No infrastructure redundancy required*.
   - **Image:** **Ubuntu Server 24.04 LTS - x64 Gen2**.
   - **Size:** **Standard_B2ms** (2 vCPU, 8 GiB) or **Standard_B4ms** (4 vCPU, 16 GiB) → *See all sizes* if not shown.
   - **Authentication type:** *SSH public key*. **Username:** your user (step 6). **SSH public key source:** *Use
     existing public key* → paste your `id_ed25519.pub` line (step 4).
   - **Public inbound ports:** *Allow selected ports* → **SSH (22), HTTP (80), HTTPS (443)**.
3. **Disks:** **OS disk size:** *64 GiB*. **OS disk type:** *Standard SSD* (or Premium SSD for more speed).
4. **Networking:** keep the new virtual network and a **new public IP**. Click **Create new** under *Public IP* →
   **Assignment: Static** → OK.
5. **Review + create** → **Create**. Wait for *Your deployment is complete* → **Go to resource**.
6. Copy the VM's **Public IP address** (Overview page) into your information sheet.
7. Make SSH reachable only from you: VM → **Networking → Network settings** → the rule for port **22** → **Source**:
   *My IP address* → **Save**.

**Verify:** [Your computer] `ssh <your user>@<VM IP>` signs in with your key (it may ask *Are you sure…*: `yes`).

> The **storage firewall** (step 18, part 4): add the VM's IP too, otherwise the restore can't download the backup and
> the application can't reach the files.

## 42. Prepare the VM

**Where:** [Server terminal] **on the VM** (`ssh <your user>@<VM IP>`, then `sudo -i`).

Do these steps of this guide on the VM, exactly as before: **8** (optional), **10**, **11**, **12**, **13**. (Steps 5–7
are already done by Azure: you signed in with your key.)

**Verify:** `docker run --rm hello-world | grep 'Hello from Docker'` prints *Hello from Docker!*

## 43. Install `wabi-docker` on the VM (no `init`)

**Where:** [GitHub], then [Server terminal] on the VM.

**Do:** a new fine-grained token (step 14), then on the VM steps **15** and **16**. **Don't** edit `.env`, don't run
`init`: the restore brings the settings and the databases.

## 44. Allow the VM to download the images

**Where:** [GitHub], then [Server terminal] on the VM.

**Why:** the images are private. Deployments use the workflow's own token; by hand you need a token with
`read:packages`.

**Do:**
1. [GitHub] profile picture → **Settings → Developer settings → Personal access tokens → Tokens (classic) → Generate new
   token (classic)**. **Note:** `wabi-insight images`. **Expiration:** 30 days. Tick **only** `read:packages` →
   **Generate token** → copy it (starts with `ghp_`).
2. On the VM:

   ```bash
   wabi-docker registry-login
   ```

   It asks for your GitHub user name, then the token (nothing appears).

**You should see:** *Login Succeeded*.

## 45. Restore the latest backup on the VM

**Where:** [Server terminal] on the VM.

**Do:** with the SAS URL of `wabi-backups` (step 33) between the single quotes, and the newest backup's name (from the
old server's `wabi-docker backups`, or the container's list in the Azure portal):

```bash
WI_BACKUP_SAS_URL='<Blob SAS URL of wabi-backups>' wabi-docker restore <newest backup name>
```

It asks for the **backup passphrase** (from your password manager).

**You should see:** *Downloading … from Azure Blob Storage*, *Decrypting…*, *Restoring the settings (.env, api.env)
from the backup*, *Getting version …* (downloads the images), the logins, the databases, the key ring and files, then
*Restored and running*.

**If not:**
- *could not download* → the SAS URL needs **Read**, or the storage firewall doesn't allow the VM's IP (step 41's
  note).
- *could not be decrypted* → wrong passphrase.
- *could not download version* → step 44.

**Verify:**

```bash
wabi-docker status          # four containers running, health Healthy, the same version as the old server
```

## 46. Test the copy before switching

**Where:** [Server terminal] on the VM, then [Your computer].

**Why:** Let's Encrypt gives a certificate only to the server your domain points at, and the domain still points at
the old server. For the test, Caddy on the VM uses its own temporary certificate (your browser warns about it).

**Do, part 1 — temporary certificate:** [VM]

```bash
nano /opt/wabi-insight/.env
```

Find the line `WI_CADDY_OPTIONS=` (add it at the end if it isn't there) and make it `WI_CADDY_OPTIONS=local_certs`. Save
(`Ctrl + O`, `Enter`) and leave (`Ctrl + X`). Then:

```bash
wabi-docker restart
```

**Do, part 2 — test from your computer:** [Your computer] in a terminal:

```text
curl -k --resolve <your domain>:443:<VM IP> https://<your domain>/health/ready
```

**You should see:** `{"status":"Healthy",…}`. (`--resolve` sends this one request to the VM instead of the IP in DNS;
`-k` accepts the temporary certificate.)

**If not:** *Connection refused / timed out* → ports 80/443 on the VM (step 41, *Public inbound ports*; step 11 on the
VM). *SSL* error → part 1 not done, or `wabi-docker restart` not run after it.

**Do, part 3 — in your browser:** add one line to your computer's *hosts* file, so that only your computer opens the
VM for your domain:
- **Windows:** open **Notepad as administrator** (Start → type `Notepad` → right-click → *Run as administrator*) → **File
  → Open** `C:\Windows\System32\drivers\etc\hosts` (choose *All files* at the bottom right) → add a line
  `<VM IP>  <your domain>` → save.
- **Mac:** `sudo nano /etc/hosts` → add the same line → save.

Open `https://<your domain>`: the browser warns about the certificate (expected) → *Advanced* → *Continue*. Sign in, open
**System Health**, ask a question as a sample user. **Then remove the line from the hosts file** and save again.

**This was the rehearsal of a move.** If you only wanted to test the ready copy, stop here: in the Azure portal delete
the VM (VM → **Delete**, tick the OS disk, network interface and public IP) and remove its IP from the storage firewall
(step 18, part 4). Nothing changed for users.

## 47. Switch DNS (the real move)

1. [Old server] tell users, then take a last backup: `wabi-docker backup` (wait for *Copying it to Azure Blob Storage*),
   then `wabi-docker stop`, so nothing changes there any more.
2. [VM] restore that last backup, with the same command as step 45 and the newest name. The VM's databases and files
   are replaced with the latest data; its `.env` is kept.
3. [VM] `nano /opt/wabi-insight/.env` → make the line `WI_CADDY_OPTIONS=` empty again → save and leave.
4. [Domain registrar] change the A record of your domain to the **VM's IP**.
5. [VM] wait until [Your computer] `nslookup <your domain>` shows the VM's IP (5–30 minutes with a TTL of 300), then
   `wabi-docker restart`: Caddy asks Let's Encrypt for the real certificate.

**Verify:** `https://<your domain>` opens with a padlock and no warning (with the hosts-file line removed); `wabi-docker
status` on the VM is healthy; sign in and ask a question.

**If** the browser still warns after 10 minutes: `wabi-docker logs caddy` (`Ctrl + C` to stop): *no such host* or
*timeout* → DNS not switched yet, or port 80 closed on the VM; run `wabi-docker restart` again once it is fixed.

**Going back** (if something is wrong on the VM): point DNS back to the old server's IP, and on the old server run
`wabi-docker start`. Its data is as it was at step 47.1.

## 48. Move the deployments (runner) to the VM

**Where:** [Server terminal] on the old server, then on the VM.

1. Old server: remove the runner (it asks for a **removal** token: [GitHub] **Settings → Actions → Runners** → the
   runner → **Remove** shows the command and token):

   ```bash
   cd /opt/actions-runner && ./svc.sh stop && ./svc.sh uninstall
   runuser -u github-runner -- ./config.sh remove --token <removal token>
   ```

2. VM: steps **23**, **24**, **25**.
3. [GitHub] **Deploy (Docker) → Run workflow** once to prove CD works on the VM.

## 49. Retire the old server

After a week without problems on the VM: take any files you want from the old server, then cancel it at the provider.
Remove its IP from the storage firewall (step 18, part 4).

---

## Troubleshooting

| What you see | Likely cause | What to do |
|---|---|---|
| Browser: *This site can't be reached* | DNS, ports, or the stack is stopped | `nslookup <domain>` (step 3); ports 80/443 (steps 2, 11); `wabi-docker status` |
| Browser: certificate warning on the real domain | Caddy couldn't get the certificate | `wabi-docker logs caddy`: *no such host* or *timeout* → DNS or port 80; then `wabi-docker restart` |
| *502 Bad Gateway* | The API is restarting or failed | `wabi-docker logs api` (the first *fail:* or *Unhandled exception* line says why) |
| Signed out right after signing in | The site was opened with `http://` or another name | Use exactly `https://<your domain>` |
| `wabi-docker: command not found` | Not root, or step 16 not done | `sudo -i` (step 9); step 16 |
| *ERROR: run it as root* | You forgot `sudo` | `sudo wabi-docker …`, or `sudo -i` first |
| `api.env` change has no effect | The API reads it at start | `wabi-docker restart` |
| *Login failed for user 'wabi_app'* in the API log | `api.env`'s connection string was edited, or the database was replaced without the logins | Restore the backup made before; never edit that line by hand |
| *No space left on device* | Disk full (old images, backups) | `df -h /`; `docker image prune -a -f`; `ls -lh /opt/wabi-insight/backups` and lower `WI_BACKUP_KEEP` |
| The nightly backup is missing | Cron or the backup failed | `cat /var/log/wabi-insight-backup.log` |
| A question fails with *source unreachable* | A sample application's connection | Step 30, part 2: *Test connection* |
| E-mails don't arrive | SMTP settings, or the provider blocks port 587/465 | **System Health** → e-mail; ask the provider to open outgoing SMTP |

## Command reference

| Command | What it does |
|---|---|
| `wabi-docker install <folder>` | Installs or updates the command and the stack files from a downloaded repository (keeps settings and data) |
| `wabi-docker init` | Starts SQL Server and creates the database and the application's login (once) |
| `wabi-docker deploy <version> [--no-pull]` | Installs a version: download, backup, migrate, start, check, roll back on failure |
| `wabi-docker bootstrap` | Creates the first Platform Administrator (once) |
| `wabi-docker samples [gas\|bank\|all]` | Loads the sample organizations |
| `wabi-docker sample-connections` | Shows the sample applications' connection strings again |
| `wabi-docker insights-login` | Creates the read-only Platform Insights login |
| `wabi-docker storage-check` | Tests writing, reading and deleting a file in the files storage |
| `wabi-docker backup [--no-upload]` | Makes an encrypted backup (and copies it to Azure) |
| `wabi-docker backups` | Lists the backups here and in Azure |
| `wabi-docker restore <file or name>` | Restores a backup (here, or on a new server with `WI_BACKUP_SAS_URL='…'` in front) |
| `wabi-docker registry-login` | Signs in to GitHub's registry to download images by hand |
| `wabi-docker status` · `logs [service]` · `start` · `stop` · `restart` · `version` · `help` | Everyday commands |

## Costs (approximate, per month)

| Item | Cost |
|---|---|
| VPS (4 cores, 8 GB, 80 GB), for example Melbicom Fujairah | about €29 with VAT |
| Azure Blob Storage, UAE North: files (10–30 GB) and backup copies | about €2–8 |
| Docker, SQL Server Express, Caddy, Let's Encrypt, the GitHub runner | free |
| Optional, Part J: Azure VM B2ms with a static IP and a 64 GB disk, instead of the VPS | about $70–90 |

Not included: the AI provider's usage, your domain, your e-mail service.

## Security notes

- Only ports 22 (you), 80 and 443 are open. SQL Server and the API are on Docker's private network only.
- Secrets live in `/opt/wabi-insight/.env` and `api.env` (root only, mode 600), never in GitHub. Backups are encrypted
  with your passphrase before they leave the server.
- The application signs in to SQL Server with its own login (`wabi_app`, owner of `WabiInsightDB` only), never `sa`;
  Platform Insights with a read-only login.
- The runner can run only `wabi-docker version` and `wabi-docker deploy` as root; it is not in the `docker` group.
- The images are private on GitHub's registry. Delete tokens you no longer need (steps 14 and 44): GitHub → **Settings →
  Developer settings → Personal access tokens**.
- More: [SECURITY.md](SECURITY.md).
