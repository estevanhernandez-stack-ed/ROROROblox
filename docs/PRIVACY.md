---
title: Privacy Policy
description: How RORORO handles your Roblox session data on Windows.
permalink: /privacy/
---

# Privacy Policy — RORORO

**Effective date:** *(set on the day of public Store listing)*
**Version:** *(matches release version when published)*
**Publisher:** 626 Labs LLC
**Contact:** estevan.hernandez@gmail.com

---

## TL;DR

- **Your Roblox password is never seen by RORORO.** Login happens inside Roblox's own page, embedded in a Microsoft Edge WebView2 frame.
- **Roblox session cookies are stored locally only**, encrypted with the Windows Data Protection API (DPAPI), tied to your Windows user account. Copying the file to another PC won't work — *unless you deliberately export your accounts* (Account export, v1.6), which re-encrypts them under a passphrase you choose into a file you save where you want. No cloud, no upload.
- **No telemetry. No analytics. No third-party tracking.** RORORO makes network calls to Roblox-owned endpoints while it runs, not just at launch, including a presence heartbeat about every 25 seconds per saved account, and to GitHub Releases (for the daily update check, the compatibility feed, and, in direct-download builds only, the plugin catalog when you open the Plugins page).
- **No data leaves your PC** except the Roblox-side calls described below — the same calls Roblox.com would make from your browser — and, if you choose to use it, a passphrase-encrypted account-export file that you save yourself (never auto-uploaded).
- **You can delete everything**, but uninstalling alone does not do it: for every install type the data folder is `%LOCALAPPDATA%\ROROROblox\` and Windows leaves it behind on uninstall — delete that folder yourself for a full clean. (Until 2026-08-30 this bullet claimed the Store uninstall auto-removed the vault; a live test showed the Store build uses the same real folder.)

---

## What RORORO stores on your PC

| Location | Contents | Encryption |
|---|---|---|
| `accounts.dat` (in the app's local data folder) | Your saved Roblox session cookies and minimal account metadata (display name, account ID, avatar URL) | **DPAPI-encrypted** per Windows user |
| `settings.json` (in the app's local data folder) | UI preferences: theme, window placement, compact-mode state, memory thresholds, toggles | Plain text — no secrets |
| `webview2-data\` (in the app's local data folder) | Embedded-browser profiles used during Add Account. Every Add Account gets a fresh folder and older ones are swept afterwards, so a login never inherits the previous account's cookie. | Plain — swept, never reused across accounts |
| `last-update-check.txt` (in the app's local data folder) | Timestamp of the most recent update check | Plain text — no secrets |
| Logs (in the app's local data folder) | Structured operational logs. Cookie values are **never** logged — only redacted indicators. | Plain text — no secrets |
| `consent.dat` *(v1.4+)* (in the app's local data folder) | Per-plugin consent records — which plugins you installed, which capabilities you granted, autostart toggles | **DPAPI-encrypted** per Windows user |
| `plugins\<plugin-id>\` *(v1.4+)* (in the app's local data folder) | Files for plugins you installed (manifest, EXE, dependencies). Plugins are SHA-256-verified against the publisher's hash before extraction; the unpacked files are plain on disk. | Plain — integrity is enforced at install time, not at rest |
| `favorites.json`, `private-servers.json` | Your games library and saved private-server links (a private-server link is a soft credential: anyone with it can join that server) | Plain text |
| `session-history.json`, `session-stats.json` *(v1.23+)* | Your launch history (last 100 rows) and the stats rollup built from it. Local only; nothing is transmitted. | Plain text — no secrets |
| `notify.dat` *(v1.25+)* (in the app's local data folder) | Phone-alert settings: which push service you picked, your Pushover user key and application token, your ntfy topic and server. Every one of these is a bearer credential. | **DPAPI-encrypted** per Windows user |
| `themes\` | Custom theme files you added or built | Plain text — no secrets |
| `streamer-identities.dat` *(v1.11+)* | The invented names streamer mode assigns to your friends (account identities live in `accounts.dat`) | Plain text — holds no secrets despite the extension |
| `last-known-mutex.txt`, `.welcome-shown` | The last verified singleton name from the compatibility feed; a marker that the welcome tour has been shown | Plain text — no secrets |

For every install type — Microsoft Store, sideload MSIX, and direct download — the "app's local data folder" is `%LOCALAPPDATA%\ROROROblox\`. Verified live on 2026-08-30: the Store build writes to this same real folder (Windows does not virtualize the app's file writes), so an uninstall of any flavor leaves it in place; delete the folder yourself for a full clean. (Until that date this section said Store installs used a virtualized LocalState folder that uninstall removes.)

---

## What RORORO does NOT store

- Your Roblox password. RORORO never receives it; it travels directly from your keystrokes inside the embedded login page to Roblox's servers via HTTPS.
- Personally identifiable information beyond what Roblox itself exposes via your saved session (display name, account ID, avatar URL — all of which are public on Roblox).
- Any data on Microsoft, Anthropic, or 626 Labs servers. There is no backend; RORORO runs entirely on your PC.

---

## Network connections RORORO makes

RORORO initiates HTTPS connections **only** to:

| Host | When | Purpose |
|---|---|---|
| `auth.roblox.com` | During *Launch As* | Roblox's documented authentication-ticket endpoint — exchanges the saved cookie for a one-time launch ticket. The same endpoint Bloxstrap and other launchers use. |
| `users.roblox.com` | When listing accounts | Public account metadata (display name, ID). Used to confirm the saved cookie still maps to a real account. |
| `thumbnails.roblox.com` | When listing accounts | Public avatar imagery. |
| `presence.roblox.com` and related Roblox endpoints (games, friends, universes, `apis.roblox.com` for pasted share links, `clientsettingscdn.roblox.com` for the current client version before a batch launch) | Continuously while RORORO is running, including a presence heartbeat about every 25 seconds per saved account | Keeps each saved account's online status, game details, and friends list current. The same kind of ongoing call the Roblox website makes while you're signed in there. |
| `api.github.com` | At app startup, at most once a day | The update check against the public RORORO GitHub Releases. RoRoRo only checks and notes a newer version in its log; it does not download or install updates on its own. Direct-download users get a new version by running the new `Setup.exe`; Store users update through the Store. |
| `objects.githubusercontent.com` | When installing a plugin (v1.4+) | Plugin installs download `manifest.json`, `manifest.sha256`, and `plugin.zip` from the GitHub release URL you paste into Plugins → Install (or pick from the in-app marketplace in direct-download builds). |
| `github.com` (Releases) | At app startup | Fetches `roblox-compat.json` (current known-good Roblox version + mutex name) from `releases/latest/download/`. Used so we can ship config updates within hours when Roblox renames the singleton mutex. Signed (ECDSA P-256/SHA-256) since v1.14.1.0 (2026-08-03): the app verifies the raw bytes against a pinned public key before trusting anything the feed returns; a missing or invalid signature is treated as no update available, never a crash and never a fallback to unverified content. In direct-download builds (v1.9+), opening the Plugins page also fetches `plugins-catalog.json` from the same location for the in-app marketplace; the Store edition never does. |
| `api.pushover.net` *(v1.25+)* | Only when **you** set up phone alerts with Pushover in Settings › Alerts, and an alert (or your own test) actually fires | Sends the alert's title and text to Pushover for delivery to your phone. Off by default; uses the user key and application token from **your own** Pushover account, stored DPAPI-encrypted; account names are streamer-masked when streamer mode is on. RoRoRo ships no Pushover credentials of its own. |
| `ntfy.sh` — or a server address you set yourself *(v1.25+)* | Only when **you** set up phone alerts with ntfy in Settings › Alerts, and an alert (or your own test) actually fires | Publishes the alert's title and text to your randomly generated ntfy topic for delivery to your phone. Off by default. The topic is a secret RoRoRo generates for you (anyone holding it can read and spoof your alerts — don't share it); it travels to the server you configure and nowhere else. |
| Plugin-publisher URLs *(v1.4+)* | Only when **you** paste a plugin install URL | RoRoRo fetches `manifest.json`, `manifest.sha256`, and `plugin.zip` from the exact URL you provide. Never auto-fetched. Each plugin's own network behavior after install is governed by that plugin's policy, not this one. |

RORORO sends a `User-Agent` header of `RORORO/<version>` on every request. We do **not** spoof a browser UA. We are transparent and identifiable to the receiving servers.

RORORO makes **no other network connections**. There are no analytics endpoints, telemetry endpoints, or third-party SDKs.

---

## Account cookies and DPAPI

When you click *Add Account*, RORORO opens an embedded Microsoft Edge WebView2 control pointed at `https://www.roblox.com/login`. The login page is Roblox's own — same HTML, same form, same HTTPS connection your browser would make. Your keystrokes go from the embedded browser straight to Roblox's servers. RORORO is the window frame, not the form handler.

After Roblox confirms a successful login, RORORO captures the `.ROBLOSECURITY` session cookie that Roblox sets in your browser. Before writing it to disk, RORORO runs it through Windows' [Data Protection API](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection) — encryption tied to your specific Windows user account on your specific machine. The encrypted file (`accounts.dat`) is unreadable on any other PC, by any other Windows user, or even by you if Windows ever loses its DPAPI master key (e.g., after a from-scratch reinstall).

The cookie value is held in plaintext only briefly in memory during a single *Launch As* operation, then goes back to disk in encrypted form. The cookie value is **never** written to logs, **never** included in error reports, and **never** transmitted to any party other than Roblox.

---

## Account export and import (v1.6 and later)

RoRoRo v1.6 added the ability to move your saved accounts to another PC. This is the **only** way your cookies leave the machine, and it only happens when you choose to do it:

- **Export** asks you for a passphrase, then writes a single `.rororo-accounts` file wherever you pick. Inside, your accounts (cookies included) are encrypted with AES-256-GCM under a key derived from your passphrase (PBKDF2-SHA256, 600,000 iterations). The file is useless to anyone without the passphrase.
- **Import** on the other PC asks for the same passphrase, decrypts the file, and re-encrypts the accounts into that machine's DPAPI vault. Accounts you already have are skipped.
- **The file is yours.** RoRoRo never uploads it, never sends it anywhere, and never stores the passphrase. If you lose the passphrase the file can't be opened — by design; there's no recovery key, because a recovery key would mean we held a way in.
- Treat the file like a password: anyone with both the file **and** the passphrase can sign in as you. Keep it private.

---

## Plugins (v1.4 and later)

RoRoRo v1.4 introduced a plugin system. Plugins are **separate products** — separate Windows programs you choose to install by pasting a GitHub release URL into the Plugins page. The Microsoft Store edition never auto-fetches plugins, never polls a curated list, and never bundles plugin code in its own MSIX. Direct-download builds (v1.9+) also offer an in-app marketplace: opening the Plugins page fetches `plugins-catalog.json` from the RoRoRo GitHub release; installing still happens only when you click Install.

What this means for your privacy:

- **Each plugin's network behavior is governed by that plugin's own privacy policy**, not this one. When you install a plugin, review the publisher's policy on its release page.
- **Capabilities are gated.** When you install a plugin, RoRoRo shows a consent sheet listing every capability the plugin asks for, in plain language. You grant capabilities individually. RoRoRo's gRPC server refuses any plugin call that needs a capability you haven't granted.
- **Plugins run in their own processes**, separately from RoRoRo. A plugin process cannot read RoRoRo's memory, your saved accounts, or `accounts.dat`.
- **Plugin install downloads** hit the URL you paste — typically `github.com` and `objects.githubusercontent.com` for the manifest + SHA + zip. The same hosts RoRoRo uses for its own updates.
- **Removing a plugin** via Plugins → Remove kills its process, deletes its install directory, and removes its consent record from `consent.dat`.

If you have not installed any plugins, this section does not apply to you — none of the plugin paths are written and no plugin-related network calls are made.

---

## Diagnostics bundle

The Diagnostics window (Help → Diagnostics) lets you save a bundle for filing bug reports. The bundle contains:

- The current operational log file (cookie values redacted).
- Roblox client version + WebView2 version (as detected from your system).
- RORORO version + Windows version.
- A list of saved account display names + IDs (no cookies, no avatars).
- *(v1.12+)* Your PC's total and currently-available RAM, and the memory each running Roblox client is using. See [Memory monitoring](#memory-monitoring-v112-and-later) below.

**No cookie values are ever in the bundle.** You can inspect the bundle before sharing it with anyone — the bundle is a `.zip` file you save to a location of your choice; RORORO does not auto-upload it.

**One thing to know before you share a bundle.** The log files inside it were written by Windows applications, and Windows error messages routinely quote full file paths — and a Windows user-profile path contains your account name. We don't put your name anywhere ourselves, but we can't promise it never turns up inside an operating-system error string. If you're posting a bundle somewhere public, open the `.zip` and have a look first.

---

## Memory monitoring (v1.12 and later)

RoRoRo v1.12 added a memory watchdog. The Roblox client uses more memory the longer it runs, and several clients at once will eventually fill your PC — which is what was killing people's alt windows on long sessions. RoRoRo now watches for that and warns you before it happens.

Here is exactly what that involves:

- **What it reads.** For each Roblox client *that RoRoRo launched*, it reads the private-bytes counter — a number Windows already publishes about every running process. It's the same figure Task Manager shows in its Memory column. RoRoRo also reads your PC's total and available RAM. It samples this about every 30 seconds.
- **What it does not read.** RoRoRo does **not** read the contents of the Roblox client's memory. It does not attach a debugger, inject code, hook the client, or call any memory-reading API. It reads one number per process. The source is open — the entire memory-reading surface is a single line in [`ProcessMemoryProbe.cs`](https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/src/ROROROblox.Core/Diagnostics/ProcessMemoryProbe.cs).
- **Where it goes.** On screen, and into your local log file — one line every 15 minutes recording what each client was using. That log stays on your PC like every other log. **Nothing about your memory use is transmitted anywhere.** There is no endpoint to send it to.
- **Why the log line exists.** When someone reports "my windows closed on their own," that line is what lets us tell whether the machine ran out of memory. It's the difference between diagnosing a report in minutes and guessing.
- **What it can do about it.** Nothing on its own. RoRoRo warns you; *you* click Recycle to close and reopen a client. It never closes a client with an open game window without asking.

If a plugin asks for the memory-pressure capability, it receives these same figures — account identifier and memory numbers, no credentials — and only after you grant that capability on the consent sheet, like every other capability.

---

## Discord integration (v1.15 and later)

RoRoRo can connect to the Discord app on your PC to show what you're playing, and can send you alerts when something happens to an account while you're away. **Both are off until you turn them on**, and a fresh install makes no Discord connection of any kind.

There are two halves and they are independent — you can use either, both, or neither.

### Rich presence and Join

- **How it connects.** Through a **local named pipe** to the Discord desktop app already running on your PC. This is the standard mechanism every game with a Discord status uses. It never leaves your machine and it is not a network connection.
- **What it publishes.** The game name, how many of your accounts are in it, and how long the session has been going. If you also turn on Join, a friend clicking Join receives a code that points at the server your accounts are in.
- **Streamer mode holds.** With streamer mode on, presence publishes your masked names and hides the roster count — the same promise the app makes on screen, kept on the way out.
- **A visibility limit worth knowing.** While Roblox is running, Discord shows *Roblox* to your friends rather than RoRoRo, because Discord gives the "playing" slot to a game it detects and RoRoRo is an application, not a detected game. Your own card is always correct. This is a Discord platform rule, not a setting we can change.
- **URI registration.** Every build that carries a Discord application id (all releases since v1.15) registers a `roblox-rororo:` link handler for your Windows user at startup, whether or not Join is on, so Discord can hand a join back to the app; with Join off, an inbound link is ignored. Every inbound join shows a confirmation naming what is about to launch before anything starts.

### Alerts

- **What triggers one.** Two things only: an account dropping out of a game unexpectedly, and a client crossing the memory warning threshold. Closes you asked for — Stop, Recycle, quitting RoRoRo — deliberately do not alert.
- **Where they go.** A Windows notification, and/or a Discord channel of your choosing. The channel is reached with a **webhook URL that you create and paste in.** RoRoRo cannot see, discover, or reach any Discord channel you have not explicitly given it a webhook for.
- **What an alert says.** The account name and the game name; for a memory warning, that client's memory figure. Nothing else.
- **What an alert can never contain.** A private-server link, invite, or access code. This is enforced by the shape of the type that carries the message — it has two text fields and no field a link could occupy — and a test fails the build if anyone adds one. A Discord Join reaches only people who can see your Join button; a channel post is read by everyone in that channel, now and in future, so the two are held to different rules.
- **Streamer mode, with one deliberate exception.** Alerts use your masked names everywhere except a channel you have designated as your **clan channel**, which sends real account names. That room is one you deliberately joined, with people who already know which accounts are yours, and a board of invented names there would be unusable. Note the consequence: if that channel is visible while you are streaming, real names are visible too.

### What this adds to the tables above

| Location | Contents | Encryption |
|---|---|---|
| `discord.dat` (in the app's local data folder) | Discord settings: which alerts are on, where each goes, any webhook URLs you pasted, and which accounts you muted | **DPAPI-encrypted** per Windows user |

| Host | When | Purpose |
|---|---|---|
| `discord.com` | Only when you have pasted a webhook URL and chosen a destination that uses it | Posting your alerts to the channel that webhook belongs to, and a one-time read of the webhook to show you which channel it posts to before the first alert lands there. Sends `User-Agent: RORORO/<version>`, same as every other request. |

A webhook URL is a credential — anyone holding it can post to that channel until you delete it in Discord. RoRoRo stores it encrypted, **never writes it to the log file**, and never includes it in a diagnostics bundle. If you paste something that looks like a Discord bot token, RoRoRo tells you what it is and refuses it rather than storing it.

**No telemetry, still.** RoRoRo does not report whether you set any of this up, whether it worked, or whether you turned it off.

---

## Children's privacy

RORORO is a launcher for the Roblox platform. We do not collect data from anyone, including children. Children should follow the privacy practices of Roblox itself when using the Roblox platform. RORORO launches the official Roblox client unmodified; we do not interpose between the user and Roblox's privacy-relevant flows.

---

## Trademark notice

"Roblox" and the Roblox logo are trademarks of Roblox Corporation. RORORO is an independent third-party tool, **not affiliated with, endorsed by, or sponsored by Roblox Corporation**. The trademarked term is used solely to describe compatibility with the Roblox platform.

---

## Changes to this policy

If we update this policy, we'll change the **Effective date** at the top and bump the **Version** to match. Material changes (e.g., adding a new network endpoint, adding any kind of data collection) will be called out in the release notes for the version that introduces them.

---

## Contact

Questions, concerns, or rights requests: [estevan.hernandez@gmail.com](mailto:estevan.hernandez@gmail.com)

Source code is open: [github.com/estevanhernandez-stack-ed/ROROROblox](https://github.com/estevanhernandez-stack-ed/ROROROblox)
