# Notes for certification — reviewer letter (v1.22.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**.
>
> **Routine single-version jump** — certification last saw v1.21.0.0, this is v1.22.0.0. One change
> touches the disclosure surface (a new capability-gated plugin RPC); it leads and is stated in
> full. Everything else is compressed to a line. No new package capability, no new outbound
> endpoint, no telemetry, no new at-rest data.
>
> **Framing note.** The plugin paragraph restores the v1.4.0.0 letter's precise language: the Store
> edition ships **no plugin catalog or discovery surface**, and plugin install is **user-initiated
> from a URL the user brings from outside the app**, SHA-256-verified, consent-gated. The v1.21
> letter compressed that to "no in-app download or install path," which overstated it — the
> certified v1.4 letter described the paste-a-URL flow explicitly, and that flow is unchanged. This
> letter goes back to saying exactly what the app does, because the new RPC's first consumer is a
> plugin users will install through that flow.
>
> Sources: `docs/store/release-notes-1.22.0.0.md`, contract 0.9.0
> (`src/ROROROblox.PluginContract/`), `docs/superpowers/specs/2026-08-21-rororo-f013-shell-design.md`.

---

```
Hello reviewer,

Thank you for your time on v1.22.0.0. Certification last saw
v1.21.0.0; this is a routine single-version update. One change
touches the disclosure surface and leads here. No new package
capability (still runFullTrust only, unchanged since v1.4.0.0), no
new outbound endpoint, no telemetry, no new at-rest data.

1. ONE NEW CAPABILITY-GATED PLUGIN RPC (contract 0.9.0).

   The out-of-process plugin system gains GetAccounts: a plugin the
   user has explicitly granted the new "saved accounts" capability
   can read the list of saved accounts - display name, Roblox user
   id, and which one is marked as the main. That is the entire
   payload. No cookies, no tokens, no credentials cross the plugin
   boundary - credential handling stays inside the host, unchanged,
   and a plugin still never sees a .ROBLOSECURITY value.

   Same consent model as every capability since v1.4: listed by
   name on the install-time consent sheet in plain language,
   grantable and revocable individually, and enforced per-call by
   the host's interceptor - an ungranted call returns
   PERMISSION_DENIED. This RPC involves no network activity; it
   reads the same local account list the app's own window shows.

   Its first consumer is a connector plugin we publish separately
   (open source, MIT) that lets a user drive RoRoRo from an AI
   assistant on their own machine. Like every plugin it is a
   separate product: not bundled in this package, not fetched by
   the app on its own, and subject to the same consent sheet.

2. PLUGINS AND POLICY 10.2.2 - UNCHANGED, RESTATED PRECISELY.

   The Store edition ships no plugin catalog and no discovery
   surface; the app never polls for plugins or reads a curated list
   from a server. Plugin install is user-initiated: the user brings
   a release URL from outside the app, pastes it into the Plugins
   window, and clicks Install. The download is verified against a
   SHA-256 the author publishes alongside before extraction, and
   plugins run out-of-process as the same Windows user, holding no
   permissions until granted by name. This is the same architecture
   certified with v1.4.0.0 - stated here in the v1.4 letter's own
   words because this release adds a capability to that system.

EVERYTHING ELSE IN v1.22 ADDS NO DISCLOSURE SURFACE:

  - The six utility windows (Games, Settings, History, Diagnostics,
    Plugins, About) are now pages of one non-modal window. UI
    reorganization only.
  - First keyboard shortcuts (Ctrl+G, Ctrl+H, Ctrl+N, ... F1 shows
    the list). Local input handling only.
  - Two settings that previously required a restart to take effect
    (per-account alert muting, idle-warning threshold) now apply
    immediately. An internal single-owner refactor; same settings,
    same file.
  - The low-memory headroom warning now calibrates from the
    measured memory of the user's own running Roblox clients
    instead of a fixed constant. Local process metrics, read-only,
    never transmitted.
  - Accessibility: every control the app composes now exposes a
    UI Automation name (the main window had 86 unnamed controls at
    audit), and a test gate keeps it that way.

UNCHANGED FROM v1.21.0.0. Outbound calls are the same set: the
documented Roblox authentication-ticket and presence endpoints, the
update feed, and - only if the user pastes in a webhook URL they
created themselves - Discord. Requests identify as
ROROROblox/<version>; no browser spoofing. Saved cookies stay in
the per-user, per-machine DPAPI vault. No code injection, no input
automation, no macros in this product - that boundary is
deliberate. The Package.appxmanifest delta is one attribute:
Identity Version 1.21.0.0 -> 1.22.0.0.

TESTING. 1,845 automated tests pass, including rendered-pixel
contrast gates and plugin-boundary integration tests over the real
named-pipe transport. We still run no automated end-to-end tests
against live roblox.com, deliberately; that path is covered by
manual smoke on a clean Windows 11 install.

Happy to answer anything. Contact details are on the submission.

- Estevan Hernandez, 626 Labs LLC
```

## Pre-submission sanity check (v1.22.0.0-specific)

- [ ] Version in `Package.appxmanifest` is `1.22.0.0`; version in `ROROROblox.App.csproj` is `1.22.0.0`
- [ ] Manifest delta from v1.21.0.0 is **only** the Identity Version attribute (diff before submit)
- [ ] Both `dist/RORORO-Store-x64-1.22.0.0.msix` and `-arm64-` are built off the `v1.22.0.0` tag
- [x] Test count verified: 1,845 passed (1,821 unit + 24 integration, 1 skipped) on the release cut, 2026-08-22
- [ ] App still declares ONLY `runFullTrust` — no `broadFileSystemAccess`, no `internetClient`
- [ ] Contract NuGet `0.9.0` is live on nuget.org; `GetAccounts` payload is name + user id + is-main and nothing else (`plugin_contract.proto` — no place/game data, no cookie fields)
- [ ] The new capability's consent-sheet copy reads in plain language and is listed separately
- [ ] `RpcMethodCapabilityMap` covers `GetAccounts` (host refuses to start otherwise — `AssertExhaustive`)
- [ ] Reviewer letter pasted into Submission options → Notes for certification
- [ ] "What's new" listing field updated from `whats-new-1.22.0.0.md` — separate public field, historically forgotten
- [ ] `dotnet test ROROROblox.slnx` green (unit + integration)

## Source

This file is the v1.22.0.0 reviewer letter. Predecessors:

- v1.21.0.0: [`reviewer-letter-1.21.0.0.md`](reviewer-letter-1.21.0.0.md) (six-version catch-up submission)
- v1.4.0.0: [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (plugin-system policy 10.2.2 — still load-bearing, language restored here)
