# Privacy Policy — ROROROblox

**Effective date:** *(set on the day of public Store listing)*
**Version:** *(matches release version when published)*
**Publisher:** 626 Labs LLC
**Contact:** estevan.hernandez@gmail.com

---

## TL;DR

- **Your Roblox password is never seen by ROROROblox.** Login happens inside Roblox's own page, embedded in a Microsoft Edge WebView2 frame.
- **Roblox session cookies are stored locally only**, encrypted with the Windows Data Protection API (DPAPI), tied to your Windows user account. They cannot be moved off the machine and decrypted.
- **No telemetry. No analytics. No third-party tracking.** ROROROblox makes network calls only to Roblox-owned endpoints (during launch and avatar fetching) and to GitHub Releases (for auto-update checks).
- **No data leaves your PC** except the Roblox-side calls described below — the same calls Roblox.com would make from your browser.
- **You can delete everything** by uninstalling the app. The Store-installed MSIX automatically removes the encrypted vault on uninstall.

---

## What ROROROblox stores on your PC

| Location | Contents | Encryption |
|---|---|---|
| `accounts.dat` (in the app's local data folder) | Your saved Roblox session cookies and minimal account metadata (display name, account ID, avatar URL) | **DPAPI-encrypted** per Windows user |
| `settings.json` (in the app's local data folder) | UI preferences (default game URL, compact-mode state, etc.) | Plain text — no secrets |
| WebView2 cache (in the app's local data folder) | Embedded-browser cache used during Add Account | Wiped before every Add Account |
| `last-update-check.txt` (in the app's local data folder) | Timestamp of the most recent update check | Plain text — no secrets |
| Logs (in the app's local data folder) | Structured operational logs. Cookie values are **never** logged — only redacted indicators. | Plain text — no secrets |

For Microsoft Store installs, the "app's local data folder" is the package's virtualized LocalState directory, which Windows automatically removes when the app is uninstalled.

For sideload installs (the alternative pre-Store distribution path), the folder is `%LOCALAPPDATA%\ROROROblox\`. Sideload uninstalls do not auto-clean this folder; you can delete it manually.

---

## What ROROROblox does NOT store

- Your Roblox password. ROROROblox never receives it; it travels directly from your keystrokes inside the embedded login page to Roblox's servers via HTTPS.
- Personally identifiable information beyond what Roblox itself exposes via your saved session (display name, account ID, avatar URL — all of which are public on Roblox).
- Any data on Microsoft, Anthropic, or 626 Labs servers. There is no backend; ROROROblox runs entirely on your PC.

---

## Network connections ROROROblox makes

ROROROblox initiates HTTPS connections **only** to:

| Host | When | Purpose |
|---|---|---|
| `auth.roblox.com` | During *Launch As* | Roblox's documented authentication-ticket endpoint — exchanges the saved cookie for a one-time launch ticket. The same endpoint Bloxstrap and other launchers use. |
| `users.roblox.com` | When listing accounts | Public account metadata (display name, ID). Used to confirm the saved cookie still maps to a real account. |
| `thumbnails.roblox.com` | When listing accounts | Public avatar imagery. |
| `api.github.com` | At app startup | Velopack auto-update checks against the public ROROROblox GitHub Releases. |
| `objects.githubusercontent.com` | When applying an update | Velopack downloads the update package from GitHub Releases. |
| Optional: `gist.githubusercontent.com` | At app startup | Fetches `roblox-compat.json` (current known-good Roblox version + mutex name). Used so we can ship config updates within hours when Roblox renames the singleton mutex. |

ROROROblox sends a `User-Agent` header of `ROROROblox/<version>` on every request. We do **not** spoof a browser UA. We are transparent and identifiable to the receiving servers.

ROROROblox makes **no other network connections**. There are no analytics endpoints, telemetry endpoints, or third-party SDKs.

---

## Account cookies and DPAPI

When you click *Add Account*, ROROROblox opens an embedded Microsoft Edge WebView2 control pointed at `https://www.roblox.com/login`. The login page is Roblox's own — same HTML, same form, same HTTPS connection your browser would make. Your keystrokes go from the embedded browser straight to Roblox's servers. ROROROblox is the window frame, not the form handler.

After Roblox confirms a successful login, ROROROblox captures the `.ROBLOSECURITY` session cookie that Roblox sets in your browser. Before writing it to disk, ROROROblox runs it through Windows' [Data Protection API](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection) — encryption tied to your specific Windows user account on your specific machine. The encrypted file (`accounts.dat`) is unreadable on any other PC, by any other Windows user, or even by you if Windows ever loses its DPAPI master key (e.g., after a from-scratch reinstall).

The cookie value is held in plaintext only briefly in memory during a single *Launch As* operation, then goes back to disk in encrypted form. The cookie value is **never** written to logs, **never** included in error reports, and **never** transmitted to any party other than Roblox.

---

## Diagnostics bundle

The Diagnostics window (Help → Diagnostics) lets you save a bundle for filing bug reports. The bundle contains:

- The current operational log file (cookie values redacted).
- Roblox client version + WebView2 version (as detected from your system).
- ROROROblox version + Windows version.
- A list of saved account display names + IDs (no cookies, no avatars).

**No cookie values are ever in the bundle.** You can inspect the bundle before sharing it with anyone — the bundle is a `.zip` file you save to a location of your choice; ROROROblox does not auto-upload it.

---

## Children's privacy

ROROROblox is a launcher for the Roblox platform. We do not collect data from anyone, including children. Children should follow the privacy practices of Roblox itself when using the Roblox platform. ROROROblox launches the official Roblox client unmodified; we do not interpose between the user and Roblox's privacy-relevant flows.

---

## Trademark notice

"Roblox" and the Roblox logo are trademarks of Roblox Corporation. ROROROblox is an independent third-party tool, **not affiliated with, endorsed by, or sponsored by Roblox Corporation**. The trademarked term is used solely to describe compatibility with the Roblox platform.

---

## Changes to this policy

If we update this policy, we'll change the **Effective date** at the top and bump the **Version** to match. Material changes (e.g., adding a new network endpoint, adding any kind of data collection) will be called out in the release notes for the version that introduces them.

---

## Contact

Questions, concerns, or rights requests: [estevan.hernandez@gmail.com](mailto:estevan.hernandez@gmail.com)

Source code is open: [github.com/estevanhernandez-stack-ed/ROROROblox](https://github.com/estevanhernandez-stack-ed/ROROROblox)
