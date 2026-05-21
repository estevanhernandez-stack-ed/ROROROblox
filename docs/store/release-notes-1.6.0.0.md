# Release notes — v1.6.0.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.
> Feature bump: move your accounts to another PC, saved private servers in the game dropdown, a cleaner tag UI, and a couple of launch fixes.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu.

If you've already got RoRoRo installed via the installer or the Microsoft Store, this update rolls out automatically — no second prompt.

## What changed

### Move your accounts to another PC

You asked for this. **⚙ Settings → Accounts → Export accounts** writes all your saved accounts to a single file, locked with a passphrase you choose. Move that file to another PC (USB, Drive, Discord — wherever), open RoRoRo there, **Import accounts**, type the passphrase, and they're in. Accounts you already have are skipped — it merges, it doesn't overwrite.

A few things worth knowing:
- The file is your account logins. Anyone with the file **and** the passphrase can sign in as you — so use a real passphrase (12+ characters, the meter tells you) and don't post the file publicly.
- There's no recovery if you lose the passphrase. That's on purpose — a backdoor for you is a backdoor for everyone.
- Nothing goes to the cloud. The file is yours; RoRoRo never uploads it.

### Saved private servers now show in the game dropdown

Your saved private servers appear right in each account's game dropdown as "<name> (private server)." Pick one and that alt launches straight into that server — so you can send different alts into different servers (or games) in one Launch multiple pass. Rename them (right-click) so you can tell them apart.

### Cleaner tags + a filter

The empty "add tag" bar is gone. Each row now has a small **"+"** you click to add a tag, so rows stay tidy. And there's a **filter box** above your account list — type a tag (or part of a name) to show just those accounts. (Drag-to-reorder pauses while a filter's on, so nothing gets shuffled by accident.)

### Follow fixes

Following a friend works, and it no longer dumps you on the Roblox home page when the friend isn't in a joinable game (or has their join privacy off) — it tells you instead.

### Fixed: launching the wrong account when Roblox updates mid-launch

If a Roblox install/update popped up while you were launching, RoRoRo could end up launching a different account than the one you picked (with a captcha). The identity now holds through the install delay, so the account you choose is the account that launches.

## Compatibility

- **One reason cookies can now leave your PC:** the account export above — and only when you do it, encrypted under your passphrase, to a file you save. Everything else is unchanged: saved accounts, mutex/multi-instance, the auth-ticket flow, plugins, games, themes.
- No new Windows permissions, no new dependencies, no telemetry. Export/import is fully offline.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are attached if you'd rather install a package than run `Setup.exe`. Download both, right-click `dev-cert.cer` → Install → Local Machine → Trusted People, then open the `.msix`. Not sure? Just use `Setup.exe` above.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)

A 626 Labs product.
