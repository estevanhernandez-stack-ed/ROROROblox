# Notes for certification — reviewer letter (v1.7.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.7.0.0 is a feature bump on v1.6.0.0. Unlike v1.6.0.0 (which truthfully reported "no new outbound endpoints"), v1.7.0.0 **does add one** — a single documented Roblox CDN GET that checks whether a Roblox client update is pending before a launch. It is called out first and in full. The release also adds a user-initiated, confirm-gated "stop all Roblox instances" action and two new capability-gated plugin RPCs. The plugin-system policy 10.2.2 alignment from v1.4.0.0 still applies and is not re-litigated.

---

```
Hello reviewer,

Thank you for your time on v1.7.0.0. This release makes multi-launch
survive a mid-launch Roblox client update, adds a one-click "stop all
Roblox instances" tray action, and adds two new capability-gated plugin
RPCs. Three items touch the disclosure surface; they are detailed first.
One new outbound network endpoint is added (a documented Roblox CDN
version check) — it is the only new endpoint, and it is described in
full below. No new MSIX capabilities. No telemetry. No new at-rest data.

WHAT CHANGED IN v1.7.0.0

  1. ONE NEW OUTBOUND ENDPOINT (the disclosure-surface change).

     RoRoRo launches accounts through Roblox's own client. When Roblox
     needs a client update, RobloxPlayerBeta.exe spawns its updater
     ("RobloxPlayerInstaller.exe") reactively, mid-launch. In a
     multi-launch this could land the wrong account, throw a scary
     "check your antivirus" timeout, or interrupt the batch. v1.7.0.0
     pre-warms the FIRST client through the update, waits for the
     installer to clear and that client to attach, THEN launches the
     rest of the batch — so the update happens once, up front.

     To decide whether an update is even pending, RoRoRo adds ONE new
     outbound call:

         GET https://clientsettingscdn.roblox.com/v2/client-version/
             WindowsPlayer

     - It is a documented, public Roblox endpoint. RoRoRo compares the
       installed RobloxPlayerBeta.exe FileVersion to the version GUID
       the CDN reports. That is the entire purpose of the call.
     - The User-Agent stays ROROROblox/<version>. RoRoRo does NOT spoof
       a browser and never has — no Mozilla/Edge string.
     - It is degrade-safe. Any failure (non-200, parse error, timeout)
       returns "no update pending" and NEVER blocks a launch. If the
       endpoint shifts, detection falls back to the read-only installer-
       process scan below, then to launching normally.
     - This is the ONLY new outbound endpoint in v1.7.0.0. There is no
       telemetry, analytics, or crash reporting attached to it or
       anywhere else.

     A "Roblox is updating — hold on" banner shows during the pre-warm
     wait so a slow update reads as an intended hold, not a hang. If
     Bloxstrap or Fishstrap is the registered roblox-player: handler
     (they update proactively themselves), RoRoRo detects that and skips
     its own deferral so users are not double-updated. RoRoRo does NOT
     take over the roblox-player: handler and does NOT manage the Roblox
     version — that is a different product and against our posture.

     Implemented in the launch/tracker path (RobloxProcessTracker,
     RobloxCompatChecker version read, install-deferral gating). Design:
     docs/superpowers/specs/2026-05-21-rororo-install-deferral-design.md.

  2. "STOP ALL ROBLOX INSTANCES" TRAY ACTION (process termination).

     A new tray menu item force-closes every running Roblox client in
     one click. Disclosure posture, stated plainly:

     - It is user-initiated and confirm-gated. Clicking it opens a
       branded confirmation modal ("N Roblox clients are running. This
       closes them all immediately.") before anything is closed. When no
       Roblox client is running it is a quiet no-op.
     - It force-closes only RobloxPlayerBeta.exe processes owned by the
       SAME Windows user running RoRoRo. No elevation is requested. No
       foreign-user processes are touched.
     - It is process termination only. There is NO injection, NO memory
       reading or tampering, NO macros, and NO modification of the
       Roblox client. RoRoRo never alters Roblox's files or memory.

     Related, the install-deferral above performs a READ-ONLY process
     scan for RobloxPlayerInstaller.exe to detect an in-progress update.
     That scan reads process presence only — it does not open, modify,
     or terminate the installer.

  3. TWO NEW CAPABILITY-GATED PLUGIN RPCs (plugin contract 0.2.0).

     The out-of-process plugin system from v1.4.0.0 gains two additive
     RPCs so a plugin (e.g. a clan-coordination Discord bridge) can:

     - RequestLaunchTarget — launch one of the user's accounts into a
       Roblox server from a link or friend the plugin provides; and
     - GetCurrentServer — read the private-server link the user most
       recently launched, so the plugin can share it.

     These ride the existing plugin security model unchanged: each is a
     distinct, separately-listed capability the user must grant on the
     consent sheet; plugins run out-of-process; install is SHA-verified
     from a GitHub release; and the host gates every RPC by capability.
     The host owns all cookie handling, link parsing, and launching — a
     plugin never sees a .ROBLOSECURITY cookie or an auth ticket; it
     passes an opaque link string and the host does the rest.
     "launch-target" is deliberately a separate, more-sensitive grant
     than the v1.4 "request-launch" (launching into a server someone
     else chose is more powerful than launching an account's default),
     so the consent sheet says so unmistakably.

     The plugin contract NuGet bumps 0.1.0 -> 0.2.0 (additive, proto3
     wire-compatible; the handshake string stays "1.0" so every existing
     plugin keeps working). The Store-policy 10.2.2 framing established
     in the v1.4.0.0 reviewer letter still applies and is not
     re-litigated here. Design:
     docs/superpowers/specs/2026-05-21-plugin-private-server-contract-bump-design.md.

  4. SINGLETON MUTEX NAME NOW READ FROM REMOTE CONFIG (data, not code).

     RoRoRo's multi-instance trick holds a named Windows mutex. v1.7.0.0
     resolves that mutex NAME from roblox-compat.json (published to our
     GitHub Releases) instead of a hardcoded constant, so if Roblox
     renames the mutex we can fix it with a config push instead of an app
     rebuild. Disclosure posture:

     - This is remote DATA, not remote CODE. The fetched field is a
       validated string used as the name argument to CreateMutex. No
       code, script, or executable is downloaded or run. This is
       Store-policy-10.2.2-clean.
     - The GitHub-Releases fetch itself is not new — v1.x already fetched
       roblox-compat.json for the Roblox-version-drift banner. Only the
       FIELD consumed (the mutex name) is new.
     - It is degrade-safe: if the fetch fails or the field is missing,
       RoRoRo falls back to the hardcoded default mutex name and
       multi-instance still works.

  5. RELOAD MULTI-INSTANCE FROM THE TRAY (recovery, no new surface).

     Previously, if the multi-instance mutex was lost (error state) the
     tray toggle was disabled and users had to restart the app to
     recover. The toggle now reads "Multi-Instance: ERROR — click to
     reload" and re-acquires in place. No new network, capability, or
     data — a recovery affordance over existing local mutex logic.

  6. STABILITY (no new surface).

     Fixed a startup crash (a dependency-injection constructor ambiguity
     that crashed the app before the window appeared) and added a
     full-solution CI gate. No new network, capability, or data.

WHAT STAYED THE SAME FROM v1.6.0.0

  - The app declares ONLY runFullTrust. No broadFileSystemAccess, no
    internetClient, no new capability of any kind. (Outgoing HTTPS,
    including the one new CDN check, does not require a manifest
    declaration for a full-trust desktop app.)
  - No telemetry, analytics, or crash reporting.
  - No new at-rest data surface. The DPAPI-encrypted account vault, the
    v1.6 passphrase-encrypted account export/import, the auth-ticket
    launch flow, and the presence reads are all UNCHANGED.
  - Identity Name (626LabsLLC.RoRoRoBlox), Publisher CN, Store ID, and
    Authenticode signing identity are UNCHANGED.

FILES TOUCHED (relative to v1.6.0.0)

  - Install-deferral (item 1): RobloxProcessTracker (install-aware
    attach-timeout 30s -> 120s + install-aware ProcessAttachFailed
    messaging), RobloxCompatChecker (installed-version read reused for
    the pending check + the clientsettingscdn client-version GET),
    BloxstrapDetector (Fishstrap substring + strap-aware skip), and the
    pre-warm batch gating in the launch path.
  - Stop-all + reload (items 2, 5): TrayService / tray menu (the
    confirm-gated "Stop all Roblox instances" item and the
    "Multi-Instance: ERROR — click to reload" state) and the same-user
    RobloxPlayerBeta.exe enumeration/termination helper.
  - Plugin RPCs (item 3): src/ROROROblox.PluginContract/Protos/
    plugin_contract.proto (RequestLaunchTarget + GetCurrentServer
    + messages), PluginCapability (host.commands.launch-target +
    host.queries.current-server + catalog),
    RpcMethodCapabilityMap, IPluginLaunchInvoker +
    MainViewModelLaunchInvokerAdapter, PluginHostService (two overrides),
    SavedPrivateServer.ToShareUrl, contract NuGet 0.1.0 -> 0.2.0.
  - Mutex config (item 4): MutexHolder + roblox-compat config read (mutex
    name resolved from roblox-compat.json with hardcoded fallback).
  - Stability (item 6): DI registration fix for the startup ctor
    ambiguity; full-solution CI gate.
  - src/ROROROblox.App/ROROROblox.App.csproj + Package.appxmanifest —
    Version 1.6.0.0 -> 1.7.0.0.
  - src/ROROROblox.Tests/ + src/ROROROblox.PluginTestHarness/ — new tests
    for install-deferral detection/sequencing/messaging/timeout/strap-
    skip, stop-all enumeration, the two new plugin RPCs (capability map +
    adapter + end-to-end pipe), and the mutex-name config read.

The manifest delta from v1.6.0.0 is one attribute: Identity Version
1.6.0.0 -> 1.7.0.0. Nothing else in Package.appxmanifest changed.

TRADEMARK NOTICE

"Roblox" and the Roblox logo are trademarks of Roblox Corporation.
RoRoRo is an independent third-party tool, not affiliated with,
endorsed by, or sponsored by Roblox Corporation.

PRIVACY POLICY

Live at:
https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/

Source code is open under the MIT License at:
https://github.com/estevanhernandez-stack-ed/ROROROblox

The v1.4.0.0 reviewer letter (plugin-system policy 10.2.2 framing) is
at:
https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/store/reviewer-letter-1.4.0.0.md

If anything in this submission is unclear, please reach out and we will
respond same-day.

Thank you again for your time and consideration.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v1.7.0.0-specific)

- [ ] Version in `Package.appxmanifest` is `1.7.0.0`
- [ ] Version in `ROROROblox.App.csproj` is `1.7.0.0`
- [ ] Manifest delta from v1.6.0.0 is **only** the Identity Version attribute (diff before submit)
- [ ] `dist/RORORO-Store.msix` is built off the `v1.7.0.0` tag
- [ ] Reviewer letter (this file's `---` block) pasted into Partner Center → Submission options → Notes for certification
- [ ] App still declares ONLY `runFullTrust` — confirm no `broadFileSystemAccess` and no `internetClient` crept in (neither is needed; the new CDN check is outgoing HTTPS from a full-trust app)
- [ ] The clientsettingscdn client-version GET uses the `ROROROblox/<version>` User-Agent — **no** `Mozilla/5.0`, no Edge spoofing
- [ ] The clientsettingscdn check is degrade-safe — any failure returns "no update pending" and never blocks a launch (confirm in code + test)
- [ ] "Stop all Roblox instances" is confirm-gated, same-user only, no elevation; `count==0` is a quiet no-op
- [ ] No injection / memory tampering / macros / Roblox-client modification anywhere (process scan is read-only; stop-all is terminate-only)
- [ ] Plugin contract NuGet is `0.2.0`; handshake string is still `"1.0"`; the two new RPCs are capability-gated and listed separately on the consent sheet
- [ ] `roblox-compat.json` mutex-name read is degrade-safe (falls back to the hardcoded default); the published config carries the mutex name
- [ ] Privacy policy live copy is current (no new data surface this release; the v1.6 account-export disclosure remains accurate)
- [ ] `dotnet test ROROROblox.slnx` passes (unit + integration harness; full-solution CI gate green)

## Source

This file is the v1.7.0.0 reviewer letter. Predecessors:

- v1.6.0.0: [`reviewer-letter-1.6.0.0.md`](reviewer-letter-1.6.0.0.md) (account export/import disclosure)
- v1.5.0.0: [`reviewer-letter-1.5.0.0.md`](reviewer-letter-1.5.0.0.md) (presence disclosure + tags)
- v1.4.3.0: [`reviewer-letter-1.4.3.0.md`](reviewer-letter-1.4.3.0.md) (plugin lifecycle + manifest flags)
- v1.4.0.0: [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (plugin-system policy 10.2.2 — still load-bearing)
