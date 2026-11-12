# Wabi Insight on an Ubuntu 24.04 LTS server — the complete beginner's guide (deployment + CI/CD)

This guide assumes **no previous knowledge**, not even of Linux. It takes you from a fresh Ubuntu 24.04 LTS VPS to a
live Wabi Insight at `https://<your domain>`, with:

- **nginx** (the web server) serving the web app and passing `/api` to the API, with a free **HTTPS certificate** from
  Let's Encrypt that renews itself (**certbot**);
- the **API** running as a **systemd service**: it starts by itself, also after the server restarts;
- the **database** in **SQL Server 2025 Express** (free), on the same server;
- the **generated and uploaded files** (reports, exports, attachments, logos) on the server's disk;
- **nightly backups** of the database, the files and the encryption keys;
- **one-click deployments** from GitHub (**Actions → Deploy (Linux)**). GitHub builds the code, and a small agent on
  your server (the *runner*) installs it.

```
 Users' browsers
      │ HTTPS (443)
      ▼
 ┌──────────── Your VPS: Ubuntu 24.04 LTS ─────────────────────────────────────────────────┐
 │ nginx + Let's Encrypt certificate ── /            → the web app (/var/www/wabi-insight)
 │   (renewed by certbot)            ── /api, /health → the API on 127.0.0.1:5080
 │                                                      systemd service "wabi-insight-api"
 │                                                      (/opt/wabi-insight/api, settings in /etc/wabi-insight/api.env)
 │ SQL Server 2025 Express on 127.0.0.1:1433 → database "WabiInsightDB"
 │ Files: /var/lib/wabi-insight/artifacts          Encryption key ring: /var/lib/wabi-insight/keys
 │ Nightly backups (cron, 02:30) → /var/opt/mssql/backup and /var/backups/wabi-insight
 │ GitHub Actions runner (service, user github-runner) ← receives the deployments you start on GitHub
 └────────────────────────────────────────────┬────────────────────────────────────────────┘
                                              │ outgoing only: GitHub, the AI provider, your SMTP server
                                              ▼
```

Nothing but the website (ports 80 and 443) and SSH (port 22, for you) is open to the internet. SQL Server and the API
listen on this machine only, and the runner only makes outgoing connections to GitHub.

**Time:** about 3 hours the first time. **Extra monthly cost** on top of your VPS and your domain: usually **nothing**;
all the software is free (see *Costs* at the end).

Other guides: the Windows Server equivalent is [DEPLOYMENT_WINDOWS_SERVER_2019.md](DEPLOYMENT_WINDOWS_SERVER_2019.md);
Docker Compose and a summary of every setting are in [DEPLOYMENT.md](DEPLOYMENT.md); the pipeline is described in more
detail in [DEVOPS_LINUX.md](DEVOPS_LINUX.md).

## How to read this guide

- **Where a command runs** is shown at the start of each step:
  - **[Your computer]**: a terminal on your own Windows or Mac computer (step 4), or its browser.
  - **[Provider panel]**: your hosting provider's website (its control panel).
  - **[Domain registrar]**: the website where you manage your domain.
  - **[Server terminal]**: a terminal connected to the server, as `root`, with your values loaded (step 9). Its prompt
    ends with `#`.
  - **[GitHub]**: the GitHub website.
- Most steps have three parts:
  - **Check:** tells you whether the step is already done. If it is, skip **Do**.
  - **Do:** the actions or commands.
  - **Verify:** proves the step worked. **Don't continue until it does.**
- **Copy and paste** commands exactly. Copy a whole grey box at once and paste it into the terminal:
  - **Windows** (Windows Terminal or PowerShell): **right-click**, or `Ctrl + V`. If Windows Terminal warns that the
    text has several lines, choose **Paste anyway**.
  - **Mac** (Terminal): `Cmd + V`.
  - The terminal runs the lines one after the other. If the last line just sits there, press **Enter**.
- To **copy text from the terminal**, select it with the mouse, then press `Ctrl + C` (Windows Terminal) or `Cmd + C`
  (Mac). **`Ctrl + C` with nothing selected stops the running command**; it doesn't copy.
- When the terminal asks for a password, **nothing appears while you type or paste**: no dots, no stars. That's normal.
  Paste it and press **Enter**.
- Lines starting with `#` are comments and are ignored, and so is everything after a `#` on a line. In **Verify** boxes,
  the comment after a command shows the **expected output**.
- Text in `<angle brackets>` is a placeholder. Replace it with your own value, without the brackets.
- **Never paste a password into a chat, an e-mail or GitHub.** Keep every password and key in a password manager
  (Bitwarden, 1Password, KeePass…).

### Linux basics you'll use

| You type | What it does |
|---|---|
| `sudo -i` | Makes you `root`, the all-powerful administrator, until you type `exit`. The prompt changes from `$` to `#`. It asks for **your** password. |
| `exit` | Leaves the root shell. Typed again, it closes the connection. |
| `ls -l <folder>` | Lists the files in a folder. |
| `cat <file>` | Shows a file. |
| `nano <file>` | Opens a simple text editor in the terminal. The mouse doesn't move the cursor: use the arrow keys. **`Ctrl + O` then `Enter` saves; `Ctrl + X` exits.** On a Mac too, use the `Control` key, not `Command`. |
| `systemctl status <service>` | Shows whether a service is running. Press `q` to leave. |
| `journalctl -u <service> -n 50 --no-pager` | Shows the last 50 log lines of a service. With `-f` instead of `-n 50 --no-pager`, it follows the log live; stop with `Ctrl + C`. |
| `↑` (arrow up) | Brings back your previous command. |
| `Tab` | Completes a file or command name. |

Linux writes folders with `/` (for example `/etc/wabi-insight`), and names are **case-sensitive**: `WabiInsight` and
`wabiinsight` are different.

### Words you'll meet

| Word | Meaning |
|---|---|
| VPS | A virtual server you rent from a hosting company. Yours runs Ubuntu 24.04 LTS, a free version of Linux. |
| Terminal | A window where you type commands. Your computer has one, and through SSH it shows the server's. |
| SSH | The secure way to open a terminal on the server from your computer. Windows 10/11 and macOS have it built in. |
| SSH key | A pair of files made on your computer. The **public** key (`.pub`) goes on the server; the **private** key never leaves your computer. With it, you sign in without the server password. |
| Host fingerprint | The server's identity card. SSH shows it at the first connection and warns you if it ever changes. |
| root | The Linux administrator account. It can do anything, including breaking the server. |
| sudo | Runs a command as `root`. `sudo -i` opens a root shell. |
| Package / apt | Software for Ubuntu is installed as *packages* with the `apt-get` command, from trusted lists called *repositories*. |
| systemd service | A program that Linux keeps running in the background and starts at boot. `systemctl` starts, stops and checks it; `journalctl` shows its logs. |
| nginx | The web server. It holds the HTTPS certificate, serves the web app and passes `/api` to the API. |
| Kestrel | The web server built into the API. It listens on `127.0.0.1:5080`, which only this machine can reach. |
| certbot / Let's Encrypt | Let's Encrypt gives free HTTPS certificates; certbot asks for them and renews them by itself. |
| TLS certificate | What makes the padlock and `https://` work. |
| DNS A record | The setting at your domain registrar that points `insight.example.com` to your server's IP address. |
| ufw | Ubuntu's firewall: which incoming connections are allowed. |
| Swap | Disk space used as extra memory when RAM runs out. |
| SQL Server Express | Microsoft's free edition of SQL Server. It holds the `WabiInsightDB` database. |
| `sa` | SQL Server's built-in administrator login. The application never uses it. |
| sqlcmd | The command-line tool that runs SQL against SQL Server. |
| Connection string | One line of text that tells the application where the database is and how to sign in. It contains a password, so treat it as a secret. |
| Environment file | `/etc/wabi-insight/api.env`: this server's settings, read by the API service. Only `root` can read it. |
| Key ring | Files that encrypt the secrets the application stores in the database. **If you lose them, those secrets can't be read.** |
| cron | Linux's scheduler. It runs the nightly backup. |
| Migration | A change to the database's tables that comes with a new version of the application. |
| Bootstrap | Creating the first Platform Administrator, once, from the server. |
| CI | Continuous Integration: GitHub builds and tests every change automatically. Already active in this repository. |
| CD | Continuous Delivery: installing a version on your server. Here you start it with one click. |
| Self-hosted runner | A small GitHub agent installed on your server as a service. It receives the deployments you start and installs them. |
| Fine-grained token | A temporary GitHub password with limited rights (here: read the repository's files), which you delete after use. |

## Your information sheet

Fill this in as you go. Keep the secret rows in your password manager, not in a text file.

| Item | Example | Secret? |
|---|---|---|
| Server public IP address | `203.0.113.10` | no |
| Provider's first login: user and password | `root` / … | **yes** |
| Your own user on the server and its password (step 6) | `alex` / … | **yes** (the password) |
| SSH key passphrase (step 4) | … | **yes** |
| Domain for the site | `insight.example.com` | no |
| E-mail for certificate notices | `it@example.com` | no |
| SQL Server `sa` password (step 17) | `Wi-…` | **yes** |
| Database login `wabiinsightdbowner` and its password (step 20) | `wabiinsightdbowner` / `Wi-…` | **yes** |
| First Platform Administrator e-mail and password (step 36) | `support@wabitex.com` / … | **yes** |
| GitHub fine-grained token (steps 23 and 38; deleted after use) | `github_pat_…` | **yes** |
| Runner registration token (step 33; valid one hour) | … | **yes** |
| Optional: Azure Storage connection string (step 40) | `DefaultEndpointsProtocol=https;AccountName=…;AccountKey=…` | **yes** |

---

# Part A — Before you start

## 1. What you need

| You need | Where to get it | Notes |
|---|---|---|
| A VPS with **Ubuntu 24.04 LTS**, 64-bit (x86_64) | Your hosting provider. Choose the *Ubuntu 24.04 LTS* image when you create the server. Its control panel shows the IP address and the first login. | 4 vCPU / 8 GB RAM / 60 GB disk is comfortable. The minimum is 2 vCPU / 4 GB RAM with swap (step 13). |
| A domain name and access to its DNS settings | Your domain registrar (GoDaddy, Namecheap, Cloudflare…) | For `https://` and for the links in e-mails |
| A GitHub account with **admin** rights on the repository `mtawfig/wabi-data-harbor` | `https://github.com` | Needed to add the runner and the environment. The repository is private. |
| A computer (Windows 10/11 or Mac) with a browser | — | SSH is built in (step 4) |
| A password manager | Bitwarden, 1Password, KeePass… | For every secret on your sheet |

The SMTP account (for e-mails) and the AI key are already in the repository's `appsettings.json` (owner decision G).
**You don't need to enter them.** You may override them in this server's settings file (step 24).

### SQL Server Express is enough

Wabi Insight runs on **SQL Server Express 2019, 2022 and 2025**. The full integration suite (120 tests: migrations,
multi-tenant isolation, row scopes, the support desk, the sample-database scripts) passes on each of these Express
versions. The application uses no Standard- or Enterprise-only feature and needs no SQL Server Agent. Express limits
you should know:

| Limit | Express 2019 / 2022 | Express 2025 | What it means here |
|---|---|---|---|
| Database size | 10 GB per database | 50 GB per database | `WabiInsightDB` is about 5 MB after setup and grows with history, audit and usage events, so several years fit easily. The three sample databases are under 50 MB each. Every database has its own limit. |
| Memory | 1,410 MB buffer pool | 1,410 MB buffer pool | Enough for the configuration database. Large source databases are better on Standard. |
| CPU | 1 socket or 4 cores (whichever is lower) | same | Fine for the API's short queries. |
| SQL Server Agent | not included | not included | Backups are scheduled with cron instead (step 26). |
| Backup compression | not supported | not supported | Backups run without `COMPRESSION`. |

This guide installs **SQL Server 2025 Express** (Part D). If your database already runs on another server, skip steps
15–19, run step 20 against that server, and change the connection line in step 24.

## 2. Open the ports in your provider's control panel

**Where:** [Provider panel].

Many providers have a firewall in their control panel, outside the server. Make sure it allows:

| Port | Why | From |
|---|---|---|
| 22 (SSH) | Your access | **Only your own IP address**, if the panel allows it (strongly recommended) |
| 80 (HTTP) | Certificate validation and the redirect to HTTPS | Anywhere |
| 443 (HTTPS) | The website | Anywhere |

Outgoing connections must be allowed. They usually are by default:
- HTTPS (443) to the package servers (`archive.ubuntu.com`, `security.ubuntu.com`, `packages.microsoft.com`), Let's
  Encrypt (`acme-v02.api.letsencrypt.org`), `api.anthropic.com` (the AI provider), and GitHub (`github.com`,
  `api.github.com`, `codeload.github.com`, `objects.githubusercontent.com`, `*.actions.githubusercontent.com`, and
  `*.blob.core.windows.net`, where GitHub keeps the builds);
- your SMTP server (port 587 or 465).

> If your own IP address changes (home internet often does), the SSH rule shuts you out until you update it. Most
> providers also have a **web console** in the control panel (called *Console*, *VNC* or *Recovery*): it works even
> when SSH doesn't. Find it now, so you know where it is.

## 3. Point your domain at the server (DNS)

**Where:** [Domain registrar].

**Do:**
1. Open the DNS settings of your domain.
2. Add a record of type **A**:
   - **Name/Host:** `insight` (for `insight.example.com`; use `@` for the bare domain);
   - **Value/Points to:** your server's IP address;
   - **TTL:** the default.
3. Save.

DNS changes can take from a few minutes to a few hours to work everywhere.

**Verify** ([Your computer], in a terminal, step 4): `nslookup insight.example.com` shows your server's IP address.

---

# Part B — Connect to the server

## 4. Open a terminal on your computer and create your SSH key

**Where:** [Your computer].

**Do, part 1:** open a terminal. You don't need administrator rights.
- **Windows:** click **Start**, type `Terminal` (Windows 11) or `PowerShell` (Windows 10), and open it.
- **Mac:** press `Cmd + Space`, type `Terminal`, press **Enter**.

Type `ssh -V` and press **Enter**. It prints a version that starts with `OpenSSH`. On Windows, if it says *'ssh' is not
recognized*: open **Settings**, search for **Optional features**, add **OpenSSH Client**, then open a new terminal.

**Check:** do you already have a key? Windows (PowerShell):

```powershell
Test-Path "$env:USERPROFILE\.ssh\id_ed25519.pub"      # True = you have one: skip part 2
```

Mac:

```bash
ls ~/.ssh/id_ed25519.pub      # the file name = you have one: skip part 2
```

**Do, part 2:** create the key (the same command on Windows and Mac):

```text
ssh-keygen -t ed25519 -C "wabi-insight admin"
```

1. *Enter file in which to save the key*: press **Enter** (keep the default).
2. *Enter passphrase*: type a passphrase and press **Enter**, then again. It protects the key if your computer is stolen.
   Store it in your password manager. (Nothing appears while you type.)

**Verify:** show your **public** key. Windows (PowerShell):

```powershell
Get-Content "$env:USERPROFILE\.ssh\id_ed25519.pub"     # one line: ssh-ed25519 AAAA… wabi-insight admin
```

Mac:

```bash
cat ~/.ssh/id_ed25519.pub     # one line: ssh-ed25519 AAAA… wabi-insight admin
```

The `.pub` file is safe to share. The file **without** `.pub` (`id_ed25519`) is your **private** key: never share it,
and keep a backup of it (for example in your password manager as an attachment).

> If your provider asks for an SSH key when you create the server, paste this one public-key line there.

## 5. Connect to the server for the first time

**Where:** [Your computer].

**Do:**
1. In the terminal, type (with your provider's user, usually `root`, sometimes `ubuntu`, and your server's IP):

   ```text
   ssh root@<server IP>
   ```

2. The first time, SSH shows the server's fingerprint and asks
   *Are you sure you want to continue connecting (yes/no/[fingerprint])?*. Type `yes` and press **Enter**. (If your
   provider's panel shows the server's SSH fingerprint, compare it first.)
3. When asked for the password, paste the provider's password (nothing appears) and press **Enter**. If you gave the
   provider your SSH key, no password is asked (only your key's passphrase).
4. Some providers ask you to change the password right away: type the current one, then a new one twice. Store it.
5. If the prompt ends with `$` (you're not `root`), type `sudo -i` and the same password.

**Verify:**

```bash
whoami              # root
lsb_release -ds     # Ubuntu 24.04.x LTS
```

**Stay connected:** you continue in this window in step 6, and keep it open until step 8 is done. (Later, to
disconnect, type `exit` until the terminal says *Connection … closed*.)

## 6. Create your own administrator user

**Where:** [Server terminal], as `root` (step 5).

Signing in directly as `root` is what most attacks try. You'll sign in with your own user and borrow `root`'s rights
with `sudo` only when needed.

**Check:** `getent group sudo` lists the users who may use `sudo`. If you signed in with a user that is already listed
(for example `ubuntu`), you can keep using it: skip **Do**, and use that name wherever this guide says *your user*.

**Do:** choose a user name (lower-case letters and digits, starting with a letter). `adduser` asks for the new user's
password twice: choose a long one and store it. This is also the password `sudo` asks for. Then it asks for details
(*Full Name*, *Room Number*…): press **Enter** for each, and **Y** at the end.

```bash
NEW_ADMIN="alex"              # CHANGE: your user name
adduser "$NEW_ADMIN"
usermod -aG sudo "$NEW_ADMIN"
```

**Verify:**

```bash
id "$NEW_ADMIN"     # uid=…(alex) gid=…(alex) groups=…(alex),27(sudo),…
```

## 7. Sign in with your SSH key

**Where:** [Your computer]. **Keep the window from step 5 open**, and open a **second** terminal window.

**Check:** `ssh <your user>@<server IP>` signs you in without asking for the server password? Then skip **Do**.

**Do:** copy your public key to your user on the server. It asks for that user's password once (step 6). Replace both
placeholders first.

Windows (PowerShell):

```powershell
type "$env:USERPROFILE\.ssh\id_ed25519.pub" | ssh <your user>@<server IP> "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

Mac (Terminal):

```text
cat ~/.ssh/id_ed25519.pub | ssh <your user>@<server IP> "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

If this ends with `Permission denied (publickey)`, your provider allows keys only, and you signed in as `root` with your
key in step 5. Then copy `root`'s key to your user instead, in the **[Server terminal]** window from step 5:

```bash
install -d -m 700 -o "$NEW_ADMIN" -g "$NEW_ADMIN" "/home/$NEW_ADMIN/.ssh"
install -m 600 -o "$NEW_ADMIN" -g "$NEW_ADMIN" /root/.ssh/authorized_keys "/home/$NEW_ADMIN/.ssh/authorized_keys"
```

**Verify:** in the second window:

```text
ssh <your user>@<server IP>
```

You're signed in **without the server password** (only your key's passphrase, if you set one). Then:

```bash
sudo -v && echo sudo-ok      # asks for your user's password, then prints: sudo-ok
```

From now on, **always sign in as your user**: `ssh <your user>@<server IP>`, then `sudo -i`.

## 8. Optional, recommended: switch off password sign-in

With password sign-in off, nobody can guess their way in: only your SSH key opens the server.

> **Warning: a mistake here can lock you out of your server.** Do this only after step 7's **Verify** passed. Keep a
> terminal signed in as `root` open the whole time, and test from a **new** window before you close it. If you're ever
> locked out, use the provider's web console (step 2).

**Where:** [Server terminal], in a window that stays open.

**Check:**

```bash
sshd -T | grep -E '^(passwordauthentication|kbdinteractiveauthentication|permitrootlogin) '
# all three say "no" = already done
```

**Do:** the file name starts with `00-`, because SSH reads that folder in alphabetical order and keeps the first value
it finds. So this file wins over the provider's own files (such as `50-cloud-init.conf`).

```bash
cat > /etc/ssh/sshd_config.d/00-key-login-only.conf <<'EOF'
# Wabi Insight guide, step 8: sign in with SSH keys only, and never directly as root.
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
EOF
sshd -t && systemctl restart ssh
```

`sshd -t` checks the settings first; the restart happens only if they're valid. Your open sessions stay connected.

**Verify:**

```bash
sshd -T | grep -E '^(passwordauthentication|kbdinteractiveauthentication|permitrootlogin) '
# passwordauthentication no
# kbdinteractiveauthentication no
# permitrootlogin no
```

Then, on **[Your computer]**, in a **new** window:

```text
ssh <your user>@<server IP>
ssh -o PubkeyAuthentication=no <your user>@<server IP>
```

The first command signs you in with your key (type `exit` to leave). The second must fail with
`Permission denied (publickey).`

**If the first command fails:** in the window that is still open, undo the change, then find the cause:

```bash
rm /etc/ssh/sshd_config.d/00-key-login-only.conf && systemctl restart ssh
```

## 9. Open a root shell and save your values

**Where:** the server: sign in as your user (step 7), then type `sudo -i`. The prompt ends with `#`. Use this window for
every **[Server terminal]** step.

**Check:** `ls -l /root/wabi-insight.vars` (the file exists = done; still run the last line of **Do** in each new
window).

**Do:** paste this as it is. It saves your values and a few helper commands in a file that only `root` can read. It
contains **no password**: the helpers ask for passwords when they need them.

```bash
cat > /root/wabi-insight.vars <<'EOF'
# Wabi Insight: your values and helpers. Load them in every new root session with:  source /root/wabi-insight.vars
export WI_DOMAIN="insight.example.com"        # CHANGE: the site's domain (step 3)
export WI_CERT_EMAIL="it@example.com"         # CHANGE: receives certificate expiry notices
export WI_ADMIN_EMAIL="support@wabitex.com"   # CHANGE: the first Platform Administrator (a real mailbox)
export WI_ADMIN_USER="alex"                   # CHANGE: your own user on this server (step 6)
export WI_REPO="mtawfig/wabi-data-harbor"     # the GitHub repository
export WI_BRANCH="main"                       # the branch you deploy

# A random password: letters, digits and '-' (safe in connection strings and settings files).
wi_secret() { printf 'Wi-%s-%s\n' "$(openssl rand -hex 4)" "$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 20)"; }

# sqlcmd as SQL Server's administrator (sa). Asks for the sa password once per session; it's never saved.
sqlsa() {
    [ -n "${SA_PASSWORD:-}" ] || { read -rsp 'sa password: ' SA_PASSWORD; echo; }
    SQLCMDPASSWORD="$SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S 127.0.0.1,1433 -U sa -C -b "$@"
}

# sqlcmd as the application's login (wabiinsightdbowner), in WabiInsightDB.
sqlapp() {
    [ -n "${WI_DB_PASSWORD:-}" ] || { read -rsp 'Password of wabiinsightdbowner: ' WI_DB_PASSWORD; echo; }
    SQLCMDPASSWORD="$WI_DB_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S 127.0.0.1,1433 -U wabiinsightdbowner -d WabiInsightDB -C -b "$@"
}

# Runs an API command (migrate, storage-check, storage-copy) as the service user, with the service's settings.
wi_api() {
    systemd-run --wait --pipe --quiet --collect --uid=wabi-insight --gid=wabi-insight \
        --property=EnvironmentFile=/etc/wabi-insight/api.env --setenv=ASPNETCORE_ENVIRONMENT=Production \
        --working-directory=/opt/wabi-insight/api /usr/bin/dotnet WabiInsight.Api.dll "$@"
}

# Downloads one file of the repository to /root/wabi-insight-setup/<same path>. Asks for a GitHub token (step 23).
wi_get() {
    [ -n "${GH_TOKEN:-}" ] || { read -rsp 'GitHub token (Contents: read): ' GH_TOKEN; echo; }
    mkdir -p "/root/wabi-insight-setup/$(dirname "$1")"
    curl -fsSL -H "Authorization: Bearer ${GH_TOKEN}" -H "Accept: application/vnd.github.raw" \
        -o "/root/wabi-insight-setup/$1" "https://api.github.com/repos/${WI_REPO}/contents/$1?ref=${WI_BRANCH}" \
        && echo "downloaded $1"
}
EOF
chmod 600 /root/wabi-insight.vars
```

Now change the four lines marked `# CHANGE` to your own values:

```bash
nano /root/wabi-insight.vars
```

Move with the arrow keys, delete with **Backspace**, type your value **between the quotes**. Save with `Ctrl + O`, then
**Enter**; exit with `Ctrl + X`. Then load the file:

```bash
source /root/wabi-insight.vars
```

**In every new window**, after `sudo -i`, run `source /root/wabi-insight.vars` again.

**Verify:**

```bash
echo "$WI_DOMAIN / $WI_CERT_EMAIL / $WI_ADMIN_EMAIL / $WI_ADMIN_USER"     # your values, not the examples
type wi_secret sqlsa sqlapp wi_api wi_get | grep -c 'is a function'    # 5
```

---

# Part C — Prepare the server

## 10. Check the server and its address

**Where:** [Server terminal].

**Do / Verify:**

```bash
lsb_release -ds            # Ubuntu 24.04.x LTS
uname -m                   # x86_64
nproc; free -h; df -h /    # CPUs, memory, free disk (at least 20 GB free)
```

Check that your domain points at this server. Both commands must print the **same** address:

```bash
getent ahostsv4 "$WI_DOMAIN" | awk 'NR==1{print $1}'     # where your domain points (step 3)
curl -4 -s https://ifconfig.me; echo                     # this server's public IP: write it on your sheet
```

If they differ, wait for DNS (step 3) before step 28 (the certificate). You can continue with the other steps meanwhile.

## 11. Update the system and install the base packages

**Where:** [Server terminal].

What the base packages are for:
- `rsync` and `curl`: the deploy script needs them (step 34);
- `curl`, `gnupg` and `ca-certificates`: adding Microsoft's package lists (step 15);
- `python3`: reading GitHub's answers in step 33 (Ubuntu usually has it already);
- `libfontconfig1` and `libfreetype6`: the PDF renderer of the reports (QuestPDF/SkiaSharp).

**Check:**

```bash
apt list --upgradable 2>/dev/null | tail -n +2 | wc -l     # pending updates
dpkg -s curl gnupg ca-certificates rsync python3 libfontconfig1 libfreetype6 2>/dev/null | grep -c '^Status: install ok installed'   # 7 = all present
```

**Do:**

```bash
apt-get update && apt-get -y full-upgrade
apt-get install -y curl gnupg ca-certificates rsync python3 libfontconfig1 libfreetype6
[ -f /var/run/reboot-required ] && echo "REBOOT REQUIRED: run 'reboot', reconnect, sudo -i, source /root/wabi-insight.vars"
```

- If a purple screen asks **which services should be restarted**, press `Tab` until **<Ok>** is highlighted, then
  **Enter** (keep the defaults).
- If it asks about a **modified configuration file**, keep the version currently installed (the default): press
  **Enter**.
- If the last line prints **REBOOT REQUIRED**: type `reboot`. The connection closes. Wait one or two minutes, sign in
  again (step 7), then `sudo -i` and `source /root/wabi-insight.vars`.

**Verify:**

```bash
dpkg -s curl gnupg ca-certificates rsync python3 libfontconfig1 libfreetype6 | grep -c '^Status: install ok installed'   # 7
for t in rsync curl flock; do command -v "$t" >/dev/null && echo "$t: ok"; done                                          # three lines "… ok"
```

## 12. Time zone and clock

The application stores times in UTC. The server clock must be correct for HTTPS, sign-in cookies and support-access
expiry.

**Where:** [Server terminal].

**Check:**

```bash
timedatectl | grep -E 'Time zone|synchronized|NTP service'
```

**Do** (only if the clock isn't synchronized, or you want local time in the logs and backup file names):

```bash
timedatectl set-timezone Asia/Dubai        # optional: any zone name, e.g. Europe/London
timedatectl set-ntp true
```

**Verify:** `timedatectl | grep 'System clock synchronized: yes'` prints one line.

## 13. Swap (servers with less than 8 GB RAM)

**Where:** [Server terminal].

**Check:** `swapon --show` (no output means no swap).

**Do:**

```bash
fallocate -l 4G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

**Verify:** `swapon --show` lists `/swapfile` with size `4G`.

## 14. Firewall

Only SSH, HTTP and HTTPS are open. SQL Server (1433) and the API (5080) stay private.

**Where:** [Server terminal].

**Check:** `ufw status verbose` (`Status: inactive` means not done).

**Do:** SSH is allowed **first**, so you don't lock yourself out:

```bash
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable
```

**Verify:**

```bash
ufw status | grep -E '^(OpenSSH|80/tcp|443/tcp) +ALLOW'      # three lines
ufw status | grep -c 1433                                    # 0: SQL Server not exposed
```

---

# Part D — SQL Server 2025 Express and the configuration database

Skip steps 15–19 if the database runs on another server (see step 1).

## 15. Microsoft's package lists

**Where:** [Server terminal].

**Check:**

```bash
ls /etc/apt/sources.list.d/mssql-server-2025.list /etc/apt/sources.list.d/mssql-release.list 2>&1     # both listed = done
```

**Do:**

```bash
curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg
curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/mssql-server-2025.list -o /etc/apt/sources.list.d/mssql-server-2025.list
curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/prod.list -o /etc/apt/sources.list.d/mssql-release.list
apt-get update
```

If `gpg` asks whether to overwrite `/usr/share/keyrings/microsoft-prod.gpg`, type `y` and **Enter**.

**Verify:**

```bash
apt-cache policy mssql-server | grep Candidate        # 17.0.x
apt-cache policy mssql-tools18 | grep Candidate       # 18.x
```

## 16. Install SQL Server

**Where:** [Server terminal].

**Check:** `dpkg -s mssql-server 2>/dev/null | grep -E '^(Status|Version)'`

**Do:**

```bash
apt-get install -y mssql-server
```

**Verify:** `dpkg -s mssql-server | grep '^Version: 17\.'` prints one line.

## 17. Make it Express and set the `sa` password

**Where:** [Server terminal].

**Check:** this prints `configured` once setup has run:

```bash
systemctl is-active mssql-server && [ -f /var/opt/mssql/mssql.conf ] && echo configured
```

**Do:** this generates a strong `sa` password, shows it once, and configures SQL Server as the free **Express**
edition. **Store the password in your password manager as soon as it appears.** The application never uses it.

```bash
SA_PASSWORD="$(wi_secret)"
echo "sa password (store it now): $SA_PASSWORD"
MSSQL_PID=Express MSSQL_SA_PASSWORD="$SA_PASSWORD" ACCEPT_EULA=Y /opt/mssql/bin/mssql-conf -n setup
systemctl enable --now mssql-server
```

To choose the password yourself instead, replace the first two lines with
`read -rsp 'New sa password: ' SA_PASSWORD; echo`. Use at least 12 characters with upper case, lower case, a digit and a
symbol, and don't use `"`, `'`, `\` or `$`.

**Verify:**

```bash
systemctl is-active mssql-server          # active
systemctl is-enabled mssql-server         # enabled
ss -ltnp | grep ':1433 '                  # sqlservr listening
```

## 18. The `sqlcmd` command-line tool

**Where:** [Server terminal].

**Check:** `ls /opt/mssql-tools18/bin/sqlcmd 2>/dev/null`

**Do:**

```bash
ACCEPT_EULA=Y apt-get install -y mssql-tools18 unixodbc-dev
cat > /etc/profile.d/mssql-tools.sh <<'EOF'
export PATH="$PATH:/opt/mssql-tools18/bin"
EOF
export PATH="$PATH:/opt/mssql-tools18/bin"
```

The helper `sqlsa` (step 9) runs `sqlcmd` as `sa`. `sqlcmd` reads the password from `SQLCMDPASSWORD`, so it never
appears on the command line.

**Verify** the edition and version (if `sqlsa` asks for the `sa` password, paste it from your password manager):

```bash
sqlsa -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(64)), CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(32));"
# Express Edition (64-bit) 17.0.xxxx.x
```

## 19. Listen on this machine only

The API runs on the same server, so SQL Server doesn't need to listen on the network at all.

**Where:** [Server terminal].

**Check:** `grep -A3 '^\[network\]' /var/opt/mssql/mssql.conf` (no `ipaddress` line means all interfaces).

**Do:**

```bash
/opt/mssql/bin/mssql-conf set network.ipaddress 127.0.0.1
systemctl restart mssql-server
```

**Verify:** `ss -ltn | grep ':1433 '` shows `127.0.0.1:1433` only. (Skip this step if other machines must reach the
database. The firewall still blocks 1433 from the internet.)

## 20. The configuration database and its login

The API uses one SQL login, `wabiinsightdbowner`, that owns `WabiInsightDB` (`db_owner` on that database only, no
server-wide rights). Migrations run under it.

**Where:** [Server terminal].

**Check:**

```bash
sqlsa -h -1 -W -Q "SET NOCOUNT ON; SELECT 'db:' + ISNULL(CAST(DB_ID(N'WabiInsightDB') AS varchar(10)), 'missing'), 'login:' + ISNULL(CAST(SUSER_ID(N'wabiinsightdbowner') AS varchar(10)), 'missing');"
```

If both exist already (an earlier installation), keep the existing password and skip to **Verify**.

**Do:** this generates the login's password (letters, digits and `-`, safe for connection strings and the settings
file), shows it once, and creates the database, the login and its user. It only creates what's missing. **Store the
password in your password manager as soon as it appears.**

```bash
WI_DB_PASSWORD="$(wi_secret)"
echo "Password of wabiinsightdbowner (store it now): $WI_DB_PASSWORD"
cat > /tmp/wi-db.sql <<'EOF'
IF DB_ID(N'WabiInsightDB') IS NULL CREATE DATABASE [WabiInsightDB];
GO
IF SUSER_ID(N'wabiinsightdbowner') IS NULL
    CREATE LOGIN [wabiinsightdbowner] WITH PASSWORD = N'$(DbPassword)', DEFAULT_DATABASE = [WabiInsightDB], CHECK_POLICY = ON;
GO
USE [WabiInsightDB];
IF DATABASE_PRINCIPAL_ID(N'wabiinsightdbowner') IS NULL CREATE USER [wabiinsightdbowner] FOR LOGIN [wabiinsightdbowner];
ALTER ROLE db_owner ADD MEMBER [wabiinsightdbowner];
GO
EOF
sqlsa -v DbPassword="$WI_DB_PASSWORD" -i /tmp/wi-db.sql && rm -f /tmp/wi-db.sql
```

**Verify** by signing in as the new login (the helper `sqlapp` from step 9):

```bash
sqlapp -h -1 -W -Q "SET NOCOUNT ON; SELECT DB_NAME(), IS_ROLEMEMBER('db_owner'), IS_SRVROLEMEMBER('sysadmin');"
# WabiInsightDB 1 0
```

---

# Part E — Prepare the application's place on the server

The API and the web app aren't installed in this part: the first deployment (step 35) brings them. Here you prepare
everything they need.

## 21. ASP.NET Core 10 runtime

The API needs the ASP.NET Core 10 **runtime**. The SDK (for building) isn't needed: GitHub builds the code.

**Where:** [Server terminal].

**Check:**

```bash
dotnet --list-runtimes 2>/dev/null | grep 'Microsoft.AspNetCore.App 10\.'
```

**Do:** .NET 10 comes from Ubuntu's own package archive on 24.04:

```bash
apt-get install -y aspnetcore-runtime-10.0
```

If apt reports `Unable to locate package aspnetcore-runtime-10.0` (mirror not updated yet), use Microsoft's install
script instead, and link it to `/usr/bin/dotnet`, which the service expects:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet
ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
```

**Verify:**

```bash
readlink -f /usr/bin/dotnet                                  # /usr/lib/dotnet/dotnet (or /usr/share/dotnet/dotnet with the script)
dotnet --list-runtimes | grep 'Microsoft.AspNetCore.App 10\.'   # Microsoft.AspNetCore.App 10.0.x
```

## 22. Service account and folders

The API runs as its own user, `wabi-insight`, which can't sign in and can write only to its data folder.

| Path | Owner | Contents |
|---|---|---|
| `/opt/wabi-insight/api` | root, group `wabi-insight`, read-only | The installed API. Each deployment replaces it. |
| `/opt/wabi-insight/releases` | root | The last 5 deployments, plus `previous-api` and `previous-web` for rolling back. The first deployment creates it. |
| `/var/www/wabi-insight` | root, readable by everyone | The web app, served by nginx |
| `/var/lib/wabi-insight/artifacts` | `wabi-insight` | Reports, exports, attachments, logos |
| `/var/lib/wabi-insight/keys` | `wabi-insight`, mode 700 | The Data Protection key ring. **Losing it makes every stored connection string and provider key unreadable.** |
| `/etc/wabi-insight/api.env` | root, mode 600 | This server's settings (step 24) |
| `/root/wabi-insight-setup` | root | Files downloaded from the repository (step 23) |
| `/var/opt/mssql/backup`, `/var/backups/wabi-insight` | `mssql`, root | The nightly backups (step 26) |
| `/var/log/wabi-insight-deploy.log` | root | One line per deployment |

**Where:** [Server terminal].

**Check:**

```bash
id wabi-insight 2>/dev/null
ls -ld /opt/wabi-insight /var/lib/wabi-insight/{artifacts,keys} /etc/wabi-insight /var/www/wabi-insight 2>&1
```

**Do:**

```bash
id wabi-insight >/dev/null 2>&1 || useradd --system --home-dir /opt/wabi-insight --no-create-home --shell /usr/sbin/nologin wabi-insight
mkdir -p /opt/wabi-insight/api /var/lib/wabi-insight/artifacts /var/lib/wabi-insight/keys /etc/wabi-insight /var/www/wabi-insight
chown -R wabi-insight:wabi-insight /var/lib/wabi-insight
chmod 750 /var/lib/wabi-insight /var/lib/wabi-insight/artifacts
chmod 700 /var/lib/wabi-insight/keys
chmod 750 /etc/wabi-insight
```

**Verify:**

```bash
id wabi-insight                                           # uid=…(wabi-insight) gid=…(wabi-insight) groups=…(wabi-insight)
stat -c '%U:%G %a %n' /var/lib/wabi-insight/artifacts /var/lib/wabi-insight/keys
# wabi-insight:wabi-insight 750 /var/lib/wabi-insight/artifacts
# wabi-insight:wabi-insight 700 /var/lib/wabi-insight/keys
```

## 23. Download the server files from GitHub

The server needs four files from the repository: the service definition, the nginx site, the deploy script and (for
step 39) a SQL script. The repository is private, so you create a short-lived GitHub token that can only read it.

**Where:** first [GitHub], then [Server terminal].

**Check:**

```bash
ls /root/wabi-insight-setup/deploy/systemd/wabi-insight-api.service /root/wabi-insight-setup/deploy/nginx/wabi-insight.conf \
   /root/wabi-insight-setup/deploy/cicd/linux/wabi-insight-deploy.sh /root/wabi-insight-setup/scripts/sql/create-insights-login.sql 2>&1
```

**Do, part 1:** create the token:
1. **[GitHub]** your profile picture (top right) → **Settings → Developer settings → Personal access tokens →
   Fine-grained tokens → Generate new token**.
2. Fill in:
   - **Token name:** `wabi-insight-server-files`;
   - **Expiration:** 7 days;
   - **Repository access:** *Only select repositories* → `mtawfig/wabi-data-harbor`;
   - **Permissions → Repository permissions → Contents:** **Read-only**.
3. **Generate token**, and copy it (it starts with `github_pat_`). GitHub shows it only once.

**Do, part 2:** **[Server terminal]** download the files. `wi_get` asks for the token: paste it (nothing appears) and
press **Enter**.

```bash
for f in deploy/systemd/wabi-insight-api.service \
         deploy/nginx/wabi-insight.conf \
         deploy/cicd/linux/wabi-insight-deploy.sh \
         scripts/sql/create-insights-login.sql; do
    wi_get "$f" || break
done
unset GH_TOKEN
```

Then delete the token: **[GitHub]** *Fine-grained tokens* → `wabi-insight-server-files` → **Delete**.

**Verify:**

```bash
find /root/wabi-insight-setup -type f | sort                                  # the four files
head -1 /root/wabi-insight-setup/deploy/cicd/linux/wabi-insight-deploy.sh      # #!/bin/bash
bash -n /root/wabi-insight-setup/deploy/cicd/linux/wabi-insight-deploy.sh && echo syntax-ok   # syntax-ok
```

`curl: (22) The requested URL returned error: 401` (or `404`) means the token is wrong, expired, or doesn't include the
repository: create a new one (part 1).

## 24. This server's settings: `/etc/wabi-insight/api.env`

The repository's `appsettings.json` holds the shared settings, including the SMTP account and the AI key (owner
decision G), and a database connection string. This file sets what differs on this server:
- the database on this machine (`127.0.0.1`), signed in as `wabiinsightdbowner`;
- the public address of the site;
- the folders for the files and the key ring.

The API service reads it at every start, and **deployments never change it**. Settings are written as
`Section__Key=value`: two underscores `__` separate the sections.

**Where:** [Server terminal].

**Check:** `ls -l /etc/wabi-insight/api.env 2>/dev/null && grep -c . /etc/wabi-insight/api.env`

**Do:** if `WI_DB_PASSWORD` is no longer set in this window, the first line asks for it (paste it from your password
manager). The file is written only when it doesn't exist yet:

```bash
[ -n "$WI_DB_PASSWORD" ] || { read -rsp 'Password of wabiinsightdbowner: ' WI_DB_PASSWORD; echo; }
[ -f /etc/wabi-insight/api.env ] || cat > /etc/wabi-insight/api.env <<EOF
# /etc/wabi-insight/api.env — read by systemd (EnvironmentFile). Mode 600, root only.
ConnectionStrings__ConfigurationDatabase=Server=127.0.0.1,1433;Database=WabiInsightDB;User Id=wabiinsightdbowner;Password=${WI_DB_PASSWORD};Encrypt=True;TrustServerCertificate=True;
Platform__BaseUrl=https://${WI_DOMAIN}
Storage__RootPath=/var/lib/wabi-insight/artifacts
DataProtection__KeyRingPath=/var/lib/wabi-insight/keys
Bootstrap__Enabled=false
# Optional overrides of appsettings.json, e.g.:
# Email__Password=...
# Ai__Anthropic__ApiKey=...
# Files in Azure Blob Storage instead of the disk (step 40):
# Storage__Provider=AzureBlob
# Storage__AzureBlob__ConnectionString=DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net
EOF
chmod 600 /etc/wabi-insight/api.env && chown root:root /etc/wabi-insight/api.env
```

- If the database is on another server, change the `ConnectionStrings__…` line to that server (`nano
  /etc/wabi-insight/api.env`). To use the committed connection string from `appsettings.json` unchanged, delete the
  line.
- To use your own SMTP account or AI key, add lines such as `Email__Host=…`, `Email__Password=…` or
  `Ai__Anthropic__ApiKey=…`. After any change to this file, run `systemctl restart wabi-insight-api` (once the API is
  installed, step 35).
- **Never create an `appsettings.Local.json` on this server.** It would load after this file and override it.
  Deployments neither ship nor delete it.

**Verify** (passwords hidden):

```bash
stat -c '%U:%G %a' /etc/wabi-insight/api.env                           # root:root 600
sed -E 's/(Password=)[^;]*/\1***/' /etc/wabi-insight/api.env | grep -v '^#'
```

## 25. The API service (systemd unit)

**Where:** [Server terminal].

**Check:**

```bash
systemctl is-enabled wabi-insight-api 2>/dev/null; systemctl is-active wabi-insight-api 2>/dev/null
```

**Do:** install the service definition from the repository (step 23) and enable it, so it starts at every boot:

```bash
install -o root -g root -m 644 /root/wabi-insight-setup/deploy/systemd/wabi-insight-api.service /etc/systemd/system/wabi-insight-api.service
systemd-analyze verify /etc/systemd/system/wabi-insight-api.service
systemctl daemon-reload
systemctl enable wabi-insight-api
```

The service runs `/usr/bin/dotnet /opt/wabi-insight/api/WabiInsight.Api.dll` as `wabi-insight`, reads
`/etc/wabi-insight/api.env`, listens on `127.0.0.1:5080`, and restarts after a crash. It's protected with
`ProtectSystem=strict`: only `/var/lib/wabi-insight` is writable. **It isn't started now**: the API arrives with the
first deployment (step 35), which starts it.

**Verify:**

```bash
systemctl is-enabled wabi-insight-api      # enabled
systemctl is-active wabi-insight-api       # inactive (normal until the first deployment)
```

## 26. Nightly backups (cron)

SQL Server Express has no SQL Server Agent, so cron runs the backups. A restore needs **three things together**: the
database, `/var/lib/wabi-insight/keys` and `/var/lib/wabi-insight/artifacts`.

- The database backups go to `/var/opt/mssql/backup` (SQL Server writes there as the `mssql` user).
- The key ring and the files go to `/var/backups/wabi-insight`.
- Both keep **14 days**. The job runs every night at 02:30 as the database owner login, so `sa` is never stored on disk.
- **The deploy script runs this same backup before every database migration** (step 35). It expects it at exactly
  `/usr/local/sbin/wabi-insight-backup.sh`.

**Where:** [Server terminal].

**Check:** `ls /etc/cron.d/wabi-insight-backup /usr/local/sbin/wabi-insight-backup.sh 2>/dev/null`

**Do:**

```bash
[ -n "$WI_DB_PASSWORD" ] || { read -rsp 'Password of wabiinsightdbowner: ' WI_DB_PASSWORD; echo; }
install -d -o mssql -g mssql -m 750 /var/opt/mssql/backup
install -d -m 700 /var/backups/wabi-insight
(umask 077 && printf 'SQLCMDPASSWORD=%s\n' "$WI_DB_PASSWORD" > /root/.wabi-insight-backup.env)
cat > /usr/local/sbin/wabi-insight-backup.sh <<'EOF'
#!/bin/bash
# Nightly backup of WabiInsightDB + key ring + artifacts; keeps 14 days.
set -euo pipefail
set -a; . /root/.wabi-insight-backup.env; set +a
stamp=$(date +%Y%m%d-%H%M)
/opt/mssql-tools18/bin/sqlcmd -S 127.0.0.1,1433 -U wabiinsightdbowner -C -b -Q \
  "BACKUP DATABASE [WabiInsightDB] TO DISK = N'/var/opt/mssql/backup/WabiInsightDB-${stamp}.bak' WITH INIT, CHECKSUM;"
tar -czf "/var/backups/wabi-insight/files-${stamp}.tar.gz" -C /var/lib wabi-insight
find /var/opt/mssql/backup -name 'WabiInsightDB-*.bak' -mtime +14 -delete
find /var/backups/wabi-insight -name 'files-*.tar.gz' -mtime +14 -delete
EOF
chmod 700 /usr/local/sbin/wabi-insight-backup.sh
echo '30 2 * * * root /usr/local/sbin/wabi-insight-backup.sh >> /var/log/wabi-insight-backup.log 2>&1' > /etc/cron.d/wabi-insight-backup
```

The `( … )` around `umask` keeps the strict file permissions for that one file only.

**Verify:** run it once, then check the backup with `sa` (verifying a backup needs server-level permission):

```bash
/usr/local/sbin/wabi-insight-backup.sh && ls -lh /var/opt/mssql/backup /var/backups/wabi-insight
newest=$(printf '%s\n' /var/opt/mssql/backup/WabiInsightDB-*.bak | tail -1)
sqlsa -Q "RESTORE VERIFYONLY FROM DISK = N'$newest' WITH CHECKSUM;" | tail -1
# The backup set on file 1 is valid.
```

The key ring is still empty now: it gets its first key once the API runs. Step 35 checks that the backups include it.

> These backups are on the same disk as the server. **Copy them off the server regularly** (step 42).

---

# Part F — The website: nginx and HTTPS

## 27. Install nginx and certbot

**Where:** [Server terminal].

**Check:** `nginx -v 2>&1; certbot --version 2>/dev/null`

**Do:**

```bash
apt-get install -y nginx certbot
```

**Verify:** `systemctl is-active nginx` prints `active`, and `nginx -v` shows `1.24` or later.

## 28. Get the HTTPS certificate

DNS (step 3) must point at this server, and port 80 must be open (steps 2 and 14).

**Where:** [Server terminal].

**Check:** `ls "/etc/letsencrypt/live/$WI_DOMAIN/fullchain.pem" 2>/dev/null`

**Do:** a small temporary site answers Let's Encrypt's check on port 80, then certbot requests the certificate:

```bash
mkdir -p /var/www/letsencrypt
cat > /etc/nginx/sites-available/wabi-insight-acme <<EOF
server {
    listen 80;
    server_name ${WI_DOMAIN};
    location /.well-known/acme-challenge/ { root /var/www/letsencrypt; }
    location / { return 404; }
}
EOF
ln -sf /etc/nginx/sites-available/wabi-insight-acme /etc/nginx/sites-enabled/wabi-insight-acme
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx
certbot certonly --webroot -w /var/www/letsencrypt -d "$WI_DOMAIN" -m "$WI_CERT_EMAIL" --agree-tos --non-interactive \
  --deploy-hook "systemctl reload nginx"
```

certbot renews the certificate by itself before it expires, and reloads nginx afterwards (`--deploy-hook`).

If you have a purchased certificate instead, copy the full chain and the key to `/etc/ssl/wabi-insight/` (key mode 600)
and use those paths in step 29.

**Verify:**

```bash
ls -l "/etc/letsencrypt/live/$WI_DOMAIN/"{fullchain,privkey}.pem
openssl x509 -in "/etc/letsencrypt/live/$WI_DOMAIN/fullchain.pem" -noout -subject -enddate    # subject=CN = <your domain>, notAfter=… (about 90 days)
```

## 29. Install the Wabi Insight site

**Where:** [Server terminal].

**Check:** `ls -l /etc/nginx/sites-enabled/ | grep wabi-insight`

**Do:** install the site from the repository (step 23) with your domain filled in, and remove the temporary site:

```bash
sed "s/insight\.example\.com/${WI_DOMAIN}/g" /root/wabi-insight-setup/deploy/nginx/wabi-insight.conf > /etc/nginx/sites-available/wabi-insight
ln -sf /etc/nginx/sites-available/wabi-insight /etc/nginx/sites-enabled/wabi-insight
rm -f /etc/nginx/sites-enabled/wabi-insight-acme
nginx -t && systemctl reload nginx
```

What the site does:
- it redirects HTTP to HTTPS, except `/.well-known/acme-challenge/`, so certificate renewals keep working;
- it serves the web app, with long caching for its hashed files and `no-cache` for `index.html`;
- it passes `/api` and `/health` to the API on `127.0.0.1:5080`, with a 600 s timeout (study reports take 1–3 minutes)
  and a 12 MB upload limit;
- it tells the API the visitor's real address (`X-Forwarded-For`), and sets the security headers.

**Verify:**

```bash
nginx -t                                                                           # syntax is ok / test is successful
curl -s -o /dev/null -w '%{http_code} %{redirect_url}\n' "http://$WI_DOMAIN/"       # 301 https://<your domain>/
curl -sS -o /dev/null -w 'HTTPS works: %{http_code}\n' "https://$WI_DOMAIN/"       # HTTPS works: 403
curl -sI "https://$WI_DOMAIN/health/ready" | grep -iE '^(HTTP|strict-transport-security|x-frame-options)'
# HTTP/2 502
# strict-transport-security: max-age=31536000; includeSubDomains
# x-frame-options: DENY
certbot renew --dry-run 2>&1 | tail -2                                             # "Congratulations, all simulated renewals succeeded"
systemctl list-timers | grep certbot                                               # the renewal timer is scheduled
```

**403** and **502** are expected now: the web app and the API arrive with the first deployment (step 35). A certificate
problem shows a curl error such as `SSL certificate problem` instead of a number.

---

# Part G — CI/CD: deploy from GitHub with one click

## 30. How CI and CD work in this repository

| Part | File | When it runs | What it does |
|---|---|---|---|
| **CI**, already active | `.github/workflows/ci.yml` | **Automatically** on every pull request and every merge to `main` | Builds the API, runs the unit and SQL Server integration tests, checks dependencies for known vulnerabilities, lints, tests and builds the web app, and builds the Docker images. A red ✗ on a pull request means don't merge it yet. |
| **CD** | `.github/workflows/deploy-linux.yml` (**Deploy (Linux)**) | **Only when you click Run workflow** | GitHub builds the parts you choose (`build-release.yml`). The runner on your server installs them with `/usr/local/sbin/wabi-insight-deploy`. |

```
 You: GitHub → Actions → Deploy (Linux) → Run workflow
   │   component: frontend+backend | backend | frontend    migrate database: on/off    run tests: on/off    ref: (optional)
   ▼
 GitHub's computers (build-release.yml)
   ├─ backend : restore, build, unit tests, dotnet publish  ──► artifact wabi-insight-api
   └─ frontend: npm ci, lint, tests, production build       ──► artifact wabi-insight-web
   ▼
 The runner service on YOUR server (it only makes outgoing connections to GitHub; no port is opened)
   └─ sudo /usr/local/sbin/wabi-insight-deploy (root-owned; the runner may run only this as root)
        backend : stage → keep the previous version → stop the service → install → [backup + migrations]
                  → start → wait until /health/ready is Healthy
                  (anything fails → the previous version is put back automatically, and the run fails)
        frontend: stage → keep the previous version → install (index.html last)
        both    : final check through nginx: https://<domain>/ answers 200 and /health/ready is Healthy
```

The options you choose when you click **Run workflow**:

| Option | Values | Meaning |
|---|---|---|
| **Use workflow from** | a branch (normally `main`) | The code that is built. |
| **component** | `frontend+backend` (default), `backend`, `frontend` | What to deploy. |
| **migrate_database** | off (default) / on | Apply the database migrations. Runs only together with the backend, after a fresh database backup (step 26). Ignored, with a warning, for `frontend`. |
| **run_tests** | on (default) / off | Unit tests (API) and lint + tests (web) before packaging. CI has already run the full suite on the pull request. |
| **ref** | empty, or a commit SHA / tag | Deploy that exact version instead of the branch tip, for example to roll back (step 43). |

**No server secret is stored in GitHub.** The server keeps its own settings (`/etc/wabi-insight/api.env`), and the
builds contain only the committed `appsettings.json`. More details: [DEVOPS_LINUX.md](DEVOPS_LINUX.md).

## 31. A GitHub environment for the server

Each deployment is recorded under the repository's **Environments** (history, link, optional approval).

**Where:** [GitHub] → the repository → **Settings → Environments**.

**Check:** is `production-linux` listed?

**Do:**
1. **New environment** → name `production-linux` → **Configure environment**.
2. **Deployment branches and tags:** *Selected branches and tags* → **Add deployment branch or tag rule** → `main`.
   Only code from `main` can then be deployed. (When you roll back with **ref**, the run still starts from `main`.)
3. Optional: **Required reviewers**: add yourself, to approve each deployment before it touches the server.
4. **Environment variables → Add variable:** name `WI_PUBLIC_URL`, value `https://<your domain>`. It's shown as the link
   of each deployment.

Branch rules and required reviewers for **private** repositories need GitHub Pro, Team or Enterprise. On GitHub Free
the workflow still runs, without those rules.

**Verify:** `production-linux` is listed, with the variable (and the branch rule `main`).

## 32. The runner user

The runner runs as an ordinary user, `github-runner`. Its only `root` right will be the deploy script (step 34).

**Where:** [Server terminal].

**Check:** `id github-runner`

**Do:**

```bash
useradd --create-home --shell /bin/bash github-runner
```

**Verify:**

```bash
id github-runner                  # uid=…(github-runner) gid=…(github-runner) groups=…(github-runner)
sudo -l -U github-runner          # User github-runner is not allowed to run sudo on …
```

## 33. Install the runner (as a service)

**Where:** [Server terminal], then [GitHub], then [Server terminal].

**Check:**

```bash
ls /opt/actions-runner/config.sh 2>/dev/null
systemctl list-units --all 'actions.runner.*' --no-legend
```

**Do, part 1:** download the current runner into `/opt/actions-runner`, and check its fingerprint (SHA-256) against
GitHub's release notes:

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

If the check prints `FAILED` instead of `OK`, stop: delete the folder (`rm -rf /opt/actions-runner`) and start again.

**Do, part 2:** get a registration token: **[GitHub]** the repository → **Settings → Actions → Runners → New
self-hosted runner** → **Linux**, architecture **x64**. In the *Configure* box, copy only the long code after
`--token`. It's valid for **one hour**.

**Do, part 3:** **[Server terminal]** register the runner with the label `wabi-insight`, then install it as a service
that runs as `github-runner`. Paste the token when asked:

```bash
cd /opt/actions-runner && pwd      # /opt/actions-runner
read -rsp 'Runner registration token: ' RUNNER_TOKEN; echo
runuser -u github-runner -- ./config.sh --unattended --url "https://github.com/${WI_REPO}" \
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

**[GitHub] Settings → Actions → Runners** shows the runner **Idle**, with the labels `self-hosted`, `Linux`, `X64` and
`wabi-insight`.

## 34. Install the deploy script and its sudo rule

The runner installs releases through `/usr/local/sbin/wabi-insight-deploy`. It belongs to `root`, so the runner can't
change it, and a single sudo rule lets the runner run **only this script** as `root`. You downloaded it in step 23 (it's
`deploy/cicd/linux/wabi-insight-deploy.sh` in the repository).

**Where:** [Server terminal].

**Check:**

```bash
/usr/local/sbin/wabi-insight-deploy --script-version 2>/dev/null       # 1 = installed
sudo -l -U github-runner | grep wabi-insight-deploy
```

**Do:**

```bash
install -o root -g root -m 755 /root/wabi-insight-setup/deploy/cicd/linux/wabi-insight-deploy.sh /usr/local/sbin/wabi-insight-deploy
cat > /etc/sudoers.d/wabi-insight-deploy <<'EOF'
# GitHub Actions runner may run the Wabi Insight deploy script as root, and nothing else (docs/DEVOPS_LINUX.md).
github-runner ALL=(root) NOPASSWD: /usr/local/sbin/wabi-insight-deploy
EOF
chmod 440 /etc/sudoers.d/wabi-insight-deploy
visudo -cf /etc/sudoers.d/wabi-insight-deploy
```

The script reads optional overrides from `/etc/wabi-insight/deploy.conf` (for example `HEALTH_ATTEMPTS=60` to wait
longer for the API). Without that file, it uses the paths of this guide.

**Verify:**

```bash
stat -c '%U:%G %a' /usr/local/sbin/wabi-insight-deploy                                      # root:root 755
visudo -cf /etc/sudoers.d/wabi-insight-deploy                                               # … parsed OK
runuser -u github-runner -- sudo -n /usr/local/sbin/wabi-insight-deploy --script-version     # 1
runuser -u github-runner -- sudo -n /usr/bin/id 2>&1 | head -1                               # sudo: a password is required
```

The last line proves the runner has no other `root` rights.

## 35. The first deployment

**Where:** [GitHub], then [Server terminal].

**Check:** **[GitHub] Actions** lists **Deploy (Linux)** on the left (it appears once the workflow file is on `main`),
and the runner is **Idle** (step 33).

**Do:**
1. **[GitHub] Actions** → **Deploy (Linux)** → **Run workflow**.
2. **Use workflow from:** `main`.
3. **component:** `frontend+backend`.
4. **migrate_database:** ✅. This creates the tables in the empty database.
5. **run_tests:** ✅.
6. **ref:** leave empty.
7. **Run workflow**, then click the new run to watch it. (With *Required reviewers*, click **Review deployments →
   Approve and deploy** when it waits.)

| Job | Runs on | Duration (first run) |
|---|---|---|
| Build / Resolve version | GitHub | about 20 s |
| Build / Backend (.NET 10) | GitHub | 2–4 min |
| Build / Frontend (Angular 22) | GitHub | 2–4 min (at the same time) |
| Deploy to Linux server | your server | 15–60 s |

The deploy log shows each stage (`==> Backend: …`, `==> Database: backup before migration …`,
`==> Database: applying migrations`, `==> Frontend: …`, `==> Site check through nginx: …`) and ends with
`Deployment <release> succeeded.` The run's summary lists the component, the migration, the version and who started it.

**Verify** ([Server terminal]):

```bash
tail -2 /var/log/wabi-insight-deploy.log                       # … started release=… / … succeeded release=…
cat /opt/wabi-insight/api/deploy-info.json; echo               # "version": the commit you deployed
systemctl is-active wabi-insight-api                           # active
ss -ltnp | grep '127.0.0.1:5080'                               # dotnet listening
curl -s http://127.0.0.1:5080/health/ready; echo               # {"status":"Healthy",…"configuration-database"…"Healthy"…}
curl -s "https://$WI_DOMAIN/health/ready"; echo                # "status":"Healthy", through nginx
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' "https://$WI_DOMAIN/login"    # 200 text/html
curl -sI "https://$WI_DOMAIN/login" | grep -ciE '^(content-security-policy|x-frame-options|strict-transport-security):'   # 3: the page has its security headers
ls /opt/wabi-insight/api/appsettings*.json                     # only appsettings.json
stat -c '%U:%G %a' /opt/wabi-insight/api/appsettings.json      # root:wabi-insight 640
journalctl -u wabi-insight-api -n 30 --no-pager                # no errors; "Now listening on: http://127.0.0.1:5080"
```

Then make the API create its key ring, and check that the nightly backup includes it:

```bash
curl -s -o /dev/null http://127.0.0.1:5080/api/auth/csrf && ls /var/lib/wabi-insight/keys     # key-<id>.xml
/usr/local/sbin/wabi-insight-backup.sh
tar -tzf "$(printf '%s\n' /var/backups/wabi-insight/files-*.tar.gz | tail -1)" | grep -c 'wabi-insight/keys/key-'   # 1 or more
```

## 36. Create the first Platform Administrator (once)

**Where:** [Server terminal].

**Check:** `0` means not done yet:

```bash
sqlapp -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.Administrators;"
```

**Do:**
1. Choose the administrator's first password: **at least 12 characters** (a long phrase is fine). Don't use quotes
   (`"` `'`), backslashes (`\`), `$` or spaces: it passes through a settings file. You change it at first sign-in.
2. Run this. The bootstrap settings live in a temporary root-only file for this one command, which is then destroyed.
   The initialization secret is a long random string, used once.

```bash
read -rsp 'First password for the Platform Administrator: ' WI_ADMIN_PASSWORD; echo
(umask 077 && cat > /etc/wabi-insight/bootstrap.env <<EOF
Bootstrap__Enabled=true
Bootstrap__AdministratorEmail=${WI_ADMIN_EMAIL}
Bootstrap__AdministratorFullName=Platform Administrator
Bootstrap__AdministratorPassword=${WI_ADMIN_PASSWORD}
Bootstrap__InitializationSecret=$(openssl rand -hex 32)
EOF
)
systemd-run --wait --pipe --quiet --uid=wabi-insight --gid=wabi-insight \
  --property=EnvironmentFile=/etc/wabi-insight/api.env --property=EnvironmentFile=/etc/wabi-insight/bootstrap.env \
  --setenv=ASPNETCORE_ENVIRONMENT=Production --working-directory=/opt/wabi-insight/api /usr/bin/dotnet WabiInsight.Api.dll bootstrap
shred -u /etc/wabi-insight/bootstrap.env; unset WI_ADMIN_PASSWORD
```

**Verify:** the command printed `Bootstrap completed. AdministratorId=…`, the **Check** query now returns `1`, and the
temporary file is gone:

```bash
ls /etc/wabi-insight/bootstrap.env 2>/dev/null || echo bootstrap-file-removed     # bootstrap-file-removed
```

`Bootstrap failed: …` gives the reason in brackets, for example `password_too_short` (choose a longer password and run
**Do** again) or `bootstrap_already_completed` (an administrator exists already: sign in).

## 37. Go-live checks

**Where:** [Server terminal], then your browser.

**Verify** from the server:

```bash
# Outbound SMTP (the host and port in appsettings.json; its "//" comment lines are removed first)
grep -v '^\s*//' /opt/wabi-insight/api/appsettings.json | python3 -c "import json,sys; e=json.load(sys.stdin)['Email']; print(e['Host'], e['Port'])" | \
  { read -r h p; timeout 5 bash -c "</dev/tcp/$h/$p" && echo "SMTP $h:$p reachable"; }
# AI provider endpoint (401 without a key means reachable)
curl -s -o /dev/null -w 'api.anthropic.com %{http_code}\n' https://api.anthropic.com/v1/models
# Real client addresses reach the API (rate limits and audit use them)
grep -n 'X-Forwarded-For' /etc/nginx/sites-available/wabi-insight
```

If you set `Email__Host` or `Email__Port` in `api.env`, check that host and port instead.

**Verify** in a browser, on your own computer:
1. Open `https://<your domain>`. The landing page loads, the padlock shows a valid certificate, and the **العربية**
   switch flips the layout to right to left.
2. **Sign in** with the administrator e-mail and the first password (step 36). You're asked to change the password; do
   it.
3. **Platform → System Health**: the database and the API are healthy.
4. **Platform → AI Providers**: the default Anthropic provider is listed, and **Test** succeeds.
5. Create an **organization** with a real e-mail address for its administrator. The **temporary password e-mail**
   arrives, which proves e-mail works. (Temporary passwords are only ever e-mailed, never shown on screen.)
6. Sign in as that organization administrator in a private window, and change the password.
7. As that administrator, upload a **Report logo** under **Organization Administration → Organization Settings**. On
   the server, `ls /var/lib/wabi-insight/artifacts` now lists `organization-logos`: the files are stored.
8. Register an application (your organization's database, read-only) or load the samples (step 38), then ask a question
   on **Ask Wabi Insight**.

**Wabi Insight is live.**

---

# Part H — Optional extras

## 38. Sample databases and sample users

To fill the platform with realistic data and users of different roles, the repository has two fictional sample
organizations. Details: [`scripts/sql/samples/README.md`](../scripts/sql/samples/README.md).
- **Gulf Aviation Services** (`GAS`): flight operations, HR/payroll/attendance and procurement/stores, with four users.
- **Wabi Bank** (`WBK`): five banking systems and eight users. Details:
  [`scripts/sql/samples/wabi-bank/README.md`](../scripts/sql/samples/wabi-bank/README.md).

**Where:** [GitHub], then [Server terminal].

**Check:**

```bash
sqlsa -h -1 -W -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE name IN (N'FlightOperationsDB', N'HumanResourcesDB', N'ProcurementStoresDB');"
```

**Do, part 1:** create a GitHub token again, as in step 23 part 1.

**Do, part 2:** download and run the Gulf Aviation scripts (about 2 minutes). `wi_get` asks for the token:

```bash
gas=(01-create-flightoperationsdb.sql 02-create-humanresourcesdb.sql 03-create-procurementstoresdb.sql 04-seed-wabiinsightdb.sql)
for f in "${gas[@]}"; do wi_get "scripts/sql/samples/$f" || break; done
cd /root/wabi-insight-setup/scripts/sql/samples && pwd       # /root/wabi-insight-setup/scripts/sql/samples
for f in "${gas[@]}"; do echo "== $f"; sqlsa -I -i "$f" || break; done
```

Optional, Wabi Bank too (about 3 minutes; `07-check-consistency.sql` must report 0 everywhere):

```bash
wbk=(01-create-wabibankpersonneldb.sql 02-create-wabibankcorebankingdb.sql 03-create-wabibankfinancedb.sql
     04-create-wabibankdigitaldb.sql 05-create-wabibankitdb.sql 07-check-consistency.sql 06-seed-wabiinsightdb.sql)
for f in "${wbk[@]}"; do wi_get "scripts/sql/samples/wabi-bank/$f" || break; done
cd /root/wabi-insight-setup/scripts/sql/samples/wabi-bank && pwd       # /root/wabi-insight-setup/scripts/sql/samples/wabi-bank
for f in "${wbk[@]}"; do echo "== $f"; sqlsa -I -i "$f" || break; done
```

Then `unset GH_TOKEN`, and delete the token on GitHub.

**Verify:** the last script prints the users and the applications. Then, signed in as the sample organization
administrator (`faisal.alshamsi@gulfaviation.example` for Gulf Aviation; `w1001@wabibank.example` for Wabi Bank), open
**Organization Administration → Applications**, and for each application:
1. **Replace connection** with the read-only connection string the seed script printed, with the server address
   `127.0.0.1,1433`:
   `Server=127.0.0.1,1433;Database=…;User Id=…_reader;Password=…;Encrypt=True;TrustServerCertificate=True;`
2. **Test connection**: it must report *read-only verified*.
3. **Approve for querying**.

The sample users and SQL logins have **known passwords** (in the READMEs). **Change them before anyone else uses the
server.**

## 39. Dedicated insights login

Platform Insights can run under a login that can read only the `insights` schema. Without it, the API logs a warning and
uses the configuration login.

**Where:** [Server terminal].

**Check:** `grep -c '^Insights__ConnectionString' /etc/wabi-insight/api.env` (0 means not configured).

**Do:** the script from the repository (step 23) creates the login `wabi_insight_insights`. The generated password is
saved only in `api.env`:

```bash
WI_INSIGHTS_PASSWORD="$(wi_secret)"
(umask 077 && sed -e "s/<replace-with-strong-secret>/${WI_INSIGHTS_PASSWORD}/" \
  /root/wabi-insight-setup/scripts/sql/create-insights-login.sql > /root/insights-login.sql)
sqlsa -i /root/insights-login.sql; shred -u /root/insights-login.sql
echo "Insights__ConnectionString=Server=127.0.0.1,1433;Database=WabiInsightDB;User Id=wabi_insight_insights;Password=${WI_INSIGHTS_PASSWORD};Encrypt=True;TrustServerCertificate=True;" >> /etc/wabi-insight/api.env
unset WI_INSIGHTS_PASSWORD
systemctl restart wabi-insight-api
```

**Verify:**

```bash
journalctl -u wabi-insight-api --since "2 minutes ago" --no-pager | grep -c 'Insights:ConnectionString is not configured'   # 0
```

**Platform → Platform Insights Configuration** shows *Confined to the insights schema*.

## 40. Files in Azure Blob Storage

By default, the files are on this server's disk (`/var/lib/wabi-insight/artifacts`). You can keep them in a private
**Azure Blob Storage** container instead. Background and options:
[DEPLOYMENT.md, *File storage*](DEPLOYMENT.md#file-storage-server-disk-or-azure-blob-storage).

**Where:** the Azure portal, then [Server terminal].

**Do:**
1. Create the storage account and copy its connection string: follow
   [DEPLOYMENT_WINDOWS_AZURE.md steps 8–10](DEPLOYMENT_WINDOWS_AZURE.md#8-the-storage-account-uae-north) (you need
   only the container `wabi-insight-files`). Store the connection string in your password manager.
2. Open the settings file: `nano /etc/wabi-insight/api.env`. On the two lines under *Files in Azure Blob Storage*,
   remove the `# ` at the start; on the second one, replace everything after `ConnectionString=` with your connection
   string. Save (`Ctrl + O`, **Enter**) and exit (`Ctrl + X`).
3. Check the storage, then restart the API:

```bash
wi_api storage-check                       # … Storage check passed: a test file was written, read back identical and deleted.
systemctl restart wabi-insight-api
```

4. Optional, once: copy the files already on the disk to the container (safe to repeat; it skips files already there):

```bash
wi_api storage-copy                        # Storage copy finished: N copied, N already in the container, 0 failed.
```

Until you run `storage-copy`, files saved on the disk before the switch still open from the disk. The disk copies are
left in place; delete them yourself once the copy is verified.

**Verify:** upload a logo again (step 37, item 7). In the Azure portal → the storage account → **Containers →
wabi-insight-files**, a folder `organization-logos` appears.

`storage-check` reports *HTTP 403* for a wrong key or network rules, and *404* for a wrong account. The server needs
outgoing HTTPS to `<account>.blob.core.windows.net`. With Azure, protect the files with Azure's own soft delete; the
nightly backup still saves the key ring.

---

# Part I — Running it day to day

## 41. Everyday deployments

**[GitHub] Actions → Deploy (Linux) → Run workflow**, from `main`:

| What changed | component | migrate_database |
|---|---|---|
| Only the web app (screens, texts, translations) | `frontend` | off |
| API code without a new migration | `backend` | off |
| A change that adds a migration (new files under `src/WabiInsight.Infrastructure/Persistence/Migrations/`) | `backend` or `frontend+backend` | **on** |
| Both, no migration | `frontend+backend` | off |
| Not sure | `frontend+backend` | on (an empty migration step is harmless: "Configuration database is up to date") |

`/opt/wabi-insight/api/deploy-info.json` shows which commit is live (`"version"`). To see whether migrations are
pending, open `https://github.com/mtawfig/wabi-data-harbor/compare/<deployed version>...main` → **Files changed**: any
file under `src/WabiInsight.Infrastructure/Persistence/Migrations/` means **migrate on**.

## 42. Regular upkeep

| How often | What | How |
|---|---|---|
| Monthly | Ubuntu, SQL Server, .NET and nginx updates | Step 11 **Do** (it updates every installed package). Then `systemctl restart wabi-insight-api`, so the API uses the updated .NET. Reboot if asked. |
| Weekly | Copy the backups off the server | Below |
| Monthly | Check that the nightly backups ran | `tail -3 /var/log/wabi-insight-backup.log` and `printf '%s\n' /var/opt/mssql/backup/WabiInsightDB-*.bak \| tail -2` |
| Monthly | Disk space and database size | `df -h /` and `sqlsa -d WabiInsightDB -Q "EXEC sp_spaceused"` (the Express 2025 limit is 50 GB) |
| When the deploy job warns the script *differs* | The deploy script changed in the repository | Step 23 (a new token and the download), then step 34 **Do** |
| Every few months | The service definition or the nginx site changed in the repository | Step 23, then the comparison below |
| Automatic | TLS certificate | certbot renews it (`systemctl list-timers \| grep certbot`) |
| Automatic | The runner | It updates itself |

**Copy the backups to your computer.** [Server terminal]: put the newest backups where your user can read them:

```bash
install -d -m 700 -o "$WI_ADMIN_USER" -g "$WI_ADMIN_USER" "/home/$WI_ADMIN_USER/wabi-backups"
cp "$(printf '%s\n' /var/opt/mssql/backup/WabiInsightDB-*.bak | tail -1)" \
   "$(printf '%s\n' /var/backups/wabi-insight/files-*.tar.gz | tail -1)" "/home/$WI_ADMIN_USER/wabi-backups/"
chown -R "$WI_ADMIN_USER:$WI_ADMIN_USER" "/home/$WI_ADMIN_USER/wabi-backups"
```

[Your computer]: copy them into the current folder of your terminal (your user folder):

```text
scp -r <your user>@<server IP>:wabi-backups .
```

Then [Server terminal]: `rm -rf "/home/$WI_ADMIN_USER/wabi-backups"`. The copies contain the **key ring**: keep them
somewhere safe (an encrypted disk, or a password-protected archive).

**Compare the service definition and the nginx site** with the repository's (after step 23):

```bash
diff /root/wabi-insight-setup/deploy/systemd/wabi-insight-api.service /etc/systemd/system/wabi-insight-api.service && echo "unit: unchanged"
sed "s/insight\.example\.com/${WI_DOMAIN}/g" /root/wabi-insight-setup/deploy/nginx/wabi-insight.conf | diff - /etc/nginx/sites-available/wabi-insight && echo "nginx site: unchanged"
```

If the unit differs, repeat step 25 **Do**, then `systemctl restart wabi-insight-api`. If the site differs, repeat step
29 **Do**.

**The API's own commands**, run with the helper `wi_api` from step 9:

| Command | What it does |
|---|---|
| `wi_api migrate` | Applies pending database migrations (the deployment does it with *migrate_database* on). Prints `Configuration database is up to date.` when there's nothing to do. Take a backup first. |
| `wi_api storage-check` | Writes, reads back and deletes a test file in the configured storage (disk or Azure). |
| `wi_api storage-copy` | Copies the disk files to Azure Blob Storage (step 40 only). |
| `bootstrap` | Creates the first Platform Administrator, once (step 36). |
| `seed-demo` | For developers' computers only: it refuses to run outside the Development environment. Don't use it here. |

## 43. Rolling back

- **Code only (no migration in between):**
  1. **[GitHub] Run workflow** with **ref** = the commit you want back. It's in `/var/log/wabi-insight-deploy.log`
     (`version=`) or in an earlier run's summary.
  2. Choose the same component and leave **migrate_database** off.
- **Instantly, without GitHub:** the version before the last deployment is kept on the server ([Server terminal]):

```bash
rsync -a --delete --exclude appsettings.Local.json /opt/wabi-insight/releases/previous-api/ /opt/wabi-insight/api/ && systemctl restart wabi-insight-api
rsync -a --delete /opt/wabi-insight/releases/previous-web/ /var/www/wabi-insight/
cat /opt/wabi-insight/api/deploy-info.json; echo        # the version now running
```

  The last five deployments are also kept in `/opt/wabi-insight/releases/<date>-<time>-<version>/` (`api/` and `web/`).

- **Database back to before a migration:** the old code may not work with the new tables. Restore the backup that the
  deployment took just before migrating, then put the old code back. **Everything users did since that backup is
  lost.**
  1. [Server terminal] find the backup. Backup file names use the server's time zone; the deploy log uses UTC:

```bash
printf '%s\n' /var/opt/mssql/backup/WabiInsightDB-*.bak       # newest last
tail -4 /var/log/wabi-insight-deploy.log                       # when each deployment ran (UTC)
```

  2. Restore it, with the API stopped:

```bash
BAK="/var/opt/mssql/backup/WabiInsightDB-<yyyymmdd-hhmm>.bak"     # CHANGE: the backup taken just before the deployment
systemctl stop wabi-insight-api
sqlsa -Q "ALTER DATABASE [WabiInsightDB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;"
sqlsa -Q "RESTORE DATABASE [WabiInsightDB] FROM DISK = N'$BAK' WITH REPLACE, CHECKSUM;"
sqlsa -Q "ALTER DATABASE [WabiInsightDB] SET MULTI_USER;"
```

  3. Put the old code back: the *Instantly, without GitHub* commands above (they start the API again), or **Run
     workflow** with **ref** = the old version and **migrate_database** off.
  4. **Verify:** `curl -s "https://$WI_DOMAIN/health/ready"` reports `Healthy`, and you can sign in.

## 44. Restoring after a disaster

| Lost | Restore from |
|---|---|
| The database (damaged, or a bad change) | The newest `WabiInsightDB-*.bak`: step 43, *Database back to before a migration*, item 2; then `systemctl start wabi-insight-api` and item 4 |
| Files (reports, attachments, logos) | The newest `files-*.tar.gz`, with the API stopped: `tar -xzf "<file>" -C /var/lib wabi-insight/artifacts`, then start the API |
| The key ring | The `files-*.tar.gz` of the **same night** as the database backup: `tar -xzf "<file>" -C /var/lib wabi-insight/keys` with the API stopped |
| The whole server | A new Ubuntu 24.04 server and your off-server copies (step 42): see below |

**The whole server:**
1. Steps 4–34 on the new server. Update the DNS record (step 3) if the IP address changed.
2. **[Your computer]** copy your backups to the new server: `scp -r wabi-backups <your user>@<server IP>:`
3. **[Server terminal]** restore the database and the files **before** the first deployment:

```bash
BAK="/home/$WI_ADMIN_USER/wabi-backups/WabiInsightDB-<yyyymmdd-hhmm>.bak"     # CHANGE: your file
FILES="/home/$WI_ADMIN_USER/wabi-backups/files-<yyyymmdd-hhmm>.tar.gz"       # CHANGE: your file
install -o mssql -g mssql -m 640 "$BAK" /var/opt/mssql/backup/
sqlsa -Q "RESTORE DATABASE [WabiInsightDB] FROM DISK = N'/var/opt/mssql/backup/$(basename "$BAK")' WITH REPLACE, CHECKSUM;"
sqlsa -d WabiInsightDB -Q "ALTER USER [wabiinsightdbowner] WITH LOGIN = [wabiinsightdbowner];"
tar -xzf "$FILES" -C /var/lib && chown -R wabi-insight:wabi-insight /var/lib/wabi-insight
```

   The `ALTER USER` line links the restored database's user to the new server's login.
4. Step 35 with **migrate_database** on (harmless when nothing is pending). Skip step 36: the administrators come with
   the database. If you used step 39, run it again.

## 45. Security checklist

- **SSH:**
  - Key sign-in only, and no direct `root` sign-in (step 8).
  - Port 22 open to your own IP only, in the provider's panel (step 2).
  - Your private key is backed up, and protected by a passphrase.
- **Firewall:** only 22, 80 and 443 are open (step 14). SQL Server listens on `127.0.0.1` only (step 19), and the API
  on `127.0.0.1:5080`.
- **Database:**
  - The application uses `wabiinsightdbowner` (owner of `WabiInsightDB` only), never `sa`.
  - The `sa` password is in your password manager, not on the server.
- **Secrets on the server:**
  - `/etc/wabi-insight/api.env` and `/root/.wabi-insight-backup.env` can be read by `root` only.
  - The key ring (`/var/lib/wabi-insight/keys`) can be read by the service user only.
  - Never e-mail these files or put them in GitHub.
- **Runner:**
  - It runs as `github-runner`, whose only `root` right is the deploy script (step 34). It can't read `api.env` or the
    key ring.
  - It belongs to this private repository only, and deployments start only when someone with write access clicks **Run
    workflow**.
  - Never use the self-hosted runner for pull-request workflows; CI runs on GitHub's own computers.
- **GitHub tokens:** delete each fine-grained token after use (steps 23 and 38).
- **Backups:** copied off the server every week (step 42).
- **Updates:** monthly (step 42).
- **Samples:** if you loaded them, their known passwords are changed (step 38).

## 46. Costs (approximate, per month)

| Item | About |
|---|---|
| Your VPS | Depends on your provider and the size (step 1) |
| Your domain | Usually paid yearly at the registrar |
| Ubuntu, nginx, certbot and the Let's Encrypt certificate | Free |
| SQL Server 2025 Express | Free |
| GitHub Actions | About 10 build minutes per deployment on GitHub's computers, plus about a minute on your runner; usually covered by your plan's included minutes |
| Off-server copies of the backups | Free on your computer; a cloud storage plan if you prefer |
| Optional Azure Blob Storage (step 40) | Under USD 1 for a few GB |

## 47. Troubleshooting

| What you see | Likely cause | Fix |
|---|---|---|
| `ssh: connect to host … port 22: Connection timed out` | The provider's firewall blocks port 22 from your current IP, or the IP is wrong | Step 2 with your current IP; meanwhile, the provider's web console |
| `Permission denied (publickey)` | Wrong user name, or your key isn't on the server for that user | `ssh <your user>@<server IP>` with the user from step 6; otherwise the provider's web console, then step 7 |
| `WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED!` | The server was rebuilt (new identity), or someone is in between | Only if you rebuilt it: on your computer, `ssh-keygen -R <server IP>`, then connect again. Otherwise, stop and ask your provider. |
| `… is not in the sudoers file` | Step 6's `usermod -aG sudo` is missing | As `root` (the provider's web console): step 6 **Do**, last line; then sign in again |
| `Unable to locate package aspnetcore-runtime-10.0` | The Ubuntu mirror isn't updated yet | Step 21, the install-script commands |
| certbot: `Timeout during connect` or `unauthorized` | DNS doesn't point at the server yet, or port 80 is closed | Steps 3 and 10 (same IP), steps 2 and 14 (port 80) |
| Deploy waits forever on *Waiting for a runner* | The runner service is stopped | `cd /opt/actions-runner && ./svc.sh status`, then `./svc.sh start` |
| Deploy: *… is not installed. See docs/DEVOPS_LINUX.md step 4* or *may not run … with sudo* | Deploy script or sudo rule missing | Step 34 |
| Deploy warns the script *differs from deploy/cicd/linux/wabi-insight-deploy.sh* | The repository has a newer deploy script | Step 23, then step 34 **Do** |
| Deploy: *'rsync' is not installed (docs/DEPLOYMENT_LINUX.md step 11 …)* | A base package is missing | Step 11 |
| Deploy: */etc/wabi-insight/api.env is missing: complete docs/DEPLOYMENT_LINUX.md step 24 first* | The settings file is missing | Step 24 |
| Deploy: *systemd unit wabi-insight-api is not installed: complete … step 25 first* | The service isn't installed | Step 25 |
| Deploy warns *… not found: migrating without a fresh backup (… step 26)* | The backup script is missing | Step 26 |
| Deploy: *did not report Healthy* or *Migration failed* | The new backend didn't start. The previous backend was **restored automatically** and is running. | Read the job log (it prints the last 40 log lines) and `journalctl -u wabi-insight-api -n 100 --no-pager`. Migrations already applied stay applied; restore the backup taken just before (step 43) if you must go back. |
| Deploy: *The release is installed, but https://… answered '…'* | The release is live, but nginx or the certificate didn't answer correctly | `nginx -t`, `systemctl status nginx`, `certbot certificates` |
| Deploy: *Another deployment is running.* | Two runs at once | Wait for the other run, then run again |
| `502 Bad Gateway` in the browser | The API is stopped or crashing | `systemctl status wabi-insight-api`, `journalctl -u wabi-insight-api -n 100 --no-pager`: read the first exception |
| The service restarts in a loop with `Login failed for user` | Wrong password in `api.env`, or the login has no user in `WabiInsightDB` | Step 20 **Verify**, then fix the line in `api.env` (step 24) and `systemctl restart wabi-insight-api` |
| `/health/ready` reports the database `Unhealthy` | SQL Server is stopped | `systemctl status mssql-server`, `systemctl start mssql-server`, `ss -ltn \| grep 1433`. With step 19 applied, the API must use `127.0.0.1`. |
| `Bootstrap failed: …` | See the reason in brackets | Step 36, last paragraph |
| Signed out right after signing in | The site was opened with `http://` | Use `https://`: the session cookie is HTTPS-only |
| Everyone is rate-limited together (`429`) | The API doesn't see the visitors' addresses | `grep X-Forwarded-For /etc/nginx/sites-available/wabi-insight`: the site must set it (step 29). The API trusts it only from this machine. |
| Uploads fail with `413` | The nginx upload limit | `grep client_max_body_size /etc/nginx/sites-available/wabi-insight`: keep `12m` (ticket attachments are up to 10 MB) |
| PDF reports fail (`DllNotFoundException`, fontconfig) | A font library is missing | `dpkg -s libfontconfig1 libfreetype6`; install both (step 11) and `systemctl restart wabi-insight-api` |
| E-mails don't arrive | SMTP settings or the outgoing port | `sqlsa -d WabiInsightDB -Q "SELECT TOP 10 Status, RecipientEmail, LastError, CreatedAt FROM dbo.EmailMessages ORDER BY CreatedAt DESC"`; the SMTP check in step 37. Failed messages are retried `Email:MaxAttempts` times. |
| Stored connection strings fail after a restore | The key ring doesn't match the database | Restore the key ring from the same night as the database (step 44) |
| The log shows `No XML encryptor configured` and `Failed to determine the https port for redirect` | — | Expected. The key ring is protected by folder permissions (700, service user only), and HTTPS ends at nginx. |
| Settings in `api.env` seem ignored | An `appsettings.Local.json` exists in `/opt/wabi-insight/api` | Delete it: it overrides the settings file (step 24) |
| The database is close to the Express limit | `sqlsa -d WabiInsightDB -Q "EXEC sp_spaceused"` | Archive old usage events, or move to Standard (Express 2025 allows 50 GB) |
| `curl: (22) … error: 401` or `404` from `wi_get` | The GitHub token is wrong, expired or lacks the repository | Step 23, part 1 |

---

## Appendix: Installing without GitHub Actions (build on the server)

Use this only if you can't use GitHub Actions. You build the code on the server, so it needs more memory (keep the swap
of step 13) and more tools. Do **steps 1–29** as written, then these steps instead of steps 30–35, then **steps 36 and
37**. Updates use A5 instead of the **Run workflow** button.

### A1. Build tools: git, .NET 10 SDK and Node.js 22

Angular 22 needs Node `^22.22.3`, `^24.15` or newer; Ubuntu's own `nodejs` package is too old.

**Check:**

```bash
git --version 2>/dev/null; dotnet --list-sdks 2>/dev/null | grep '^10\.'; node --version 2>/dev/null     # node v22.22.3 or later is fine
```

**Do:**

```bash
apt-get install -y git dotnet-sdk-10.0
curl -fsSL https://deb.nodesource.com/setup_22.x -o /tmp/nodesource_setup.sh
bash /tmp/nodesource_setup.sh
apt-get install -y nodejs
```

If step 21 used Microsoft's install script, install the SDK the same way instead of `dotnet-sdk-10.0` (the SDK includes
the runtime): `bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet`.

**Verify:**

```bash
dotnet --list-sdks | grep '^10\.'     # 10.0.x
node --version                        # v22.x (>= v22.22.3)
npm --version
```

### A2. Get the source code

**Check:**

```bash
git -C /opt/wabi-insight/src log -1 --oneline 2>/dev/null
```

**Do:** git asks for a username and a password: give your GitHub user name and, as the password, a fine-grained token
(step 23, part 1). They aren't saved.

```bash
git clone --branch "$WI_BRANCH" "https://github.com/${WI_REPO}.git" /opt/wabi-insight/src
```

**Verify:**

```bash
git -C /opt/wabi-insight/src log -1 --oneline        # the commit you expect
ls /opt/wabi-insight/src/WabiInsight.slnx
```

### A3. Build and install the API and the web app

**Check:** `ls -l /opt/wabi-insight/api/WabiInsight.Api.dll /var/www/wabi-insight/index.html 2>/dev/null` (present
means built before; rebuilding is safe).

**Do:**

```bash
cd /opt/wabi-insight/src && pwd      # /opt/wabi-insight/src
dotnet publish src/WabiInsight.Api/WabiInsight.Api.csproj -c Release -o /opt/wabi-insight/api
chown -R root:wabi-insight /opt/wabi-insight/api
find /opt/wabi-insight/api -type d -exec chmod 750 {} + && find /opt/wabi-insight/api -type f -exec chmod 640 {} +
cd /opt/wabi-insight/src/src/web && npm ci && npm run build -- --configuration production
rsync -a --delete dist/wabi-insight-web/browser/ /var/www/wabi-insight/
chown -R root:root /var/www/wabi-insight && chmod -R a+rX /var/www/wabi-insight
```

`appsettings.json` in the API folder holds the committed configuration (owner decision G). The permissions let only
`root` and the service read it. The publish never includes `appsettings.Local.json` or `appsettings.Development.json`.

**Verify:**

```bash
ls /opt/wabi-insight/api/appsettings*.json                         # only appsettings.json
stat -c '%U:%G %a' /opt/wabi-insight/api/appsettings.json         # root:wabi-insight 640
ls /var/www/wabi-insight/index.html /var/www/wabi-insight/brand >/dev/null && echo web-files-ok   # web-files-ok
grep -o '<base href="[^"]*"' /var/www/wabi-insight/index.html     # <base href="/"
```

### A4. Create the database schema and start the API

**Check:** this lists the applied migrations (`no migrations yet` means none):

```bash
sqlapp -h -1 -W -Q "SET NOCOUNT ON; IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NULL SELECT 'no migrations yet' ELSE SELECT COUNT(*), MAX(MigrationId) FROM dbo.__EFMigrationsHistory;"
```

**Do:**

```bash
wi_api migrate                         # Applying N migration(s): … / Migrations applied.
systemctl start wabi-insight-api
```

**Verify:** `wi_api migrate` again prints `Configuration database is up to date.`, and the **Verify** of step 35 passes
(the deploy log and `deploy-info.json` lines don't apply). Then continue with steps 36 and 37.

### A5. Updating to a new version

```bash
cd /opt/wabi-insight/src && git fetch origin "$WI_BRANCH" && git checkout "$WI_BRANCH" && git pull --ff-only && git log -1 --oneline
/usr/local/sbin/wabi-insight-backup.sh                                     # backup first
systemctl stop wabi-insight-api
dotnet publish src/WabiInsight.Api/WabiInsight.Api.csproj -c Release -o /opt/wabi-insight/api
chown -R root:wabi-insight /opt/wabi-insight/api && find /opt/wabi-insight/api -type d -exec chmod 750 {} + && find /opt/wabi-insight/api -type f -exec chmod 640 {} +
wi_api migrate
systemctl start wabi-insight-api
cd /opt/wabi-insight/src/src/web && npm ci && npm run build -- --configuration production && rsync -a --delete dist/wabi-insight-web/browser/ /var/www/wabi-insight/
cmp -s /opt/wabi-insight/src/deploy/systemd/wabi-insight-api.service /etc/systemd/system/wabi-insight-api.service || echo "unit file changed: review it, then repeat step 25 Do"
```

**Verify:** `curl -s "https://$WI_DOMAIN/health/ready"` reports `Healthy`. Sign in, and **System Health** is green.
