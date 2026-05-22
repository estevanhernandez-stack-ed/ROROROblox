# Bloxstrap update-deferral investigation

**Branch:** `v1.5.0-presence-account-ux` (read-only investigation — no code changes; doc-only commit)
**Opened:** 2026-05-21
**Status:** Sources confirmed. One mechanism detail (whether RoRoRo can read Roblox's installed version GUID without becoming the bootstrapper) needs a small spike before building option (a). Recommendation below is grounded in cited sources; live verification points are flagged explicitly.

## The question

The builder wants to know how Bloxstrap stops Roblox's auto-update from interrupting a launch — the "black installer" box that pops mid-launch and can delay the real `RobloxPlayerBeta` past RoRoRo's tracking windows — and whether RoRoRo can adopt that in the next build.

---

## 1. How Bloxstrap actually does it (cited)

### The interruption originates inside RobloxPlayerBeta, not the protocol layer

Roblox's launch model changed in March 2024. The Bloxstrap wiki deep-dive is the authoritative source:

- Roblox installs to `%LOCALAPPDATA%\Roblox`, with each client version under `%LOCALAPPDATA%\Roblox\Versions\<version GUID>\` (e.g. `version-824aa25849794d67`). The version GUID is what Roblox's deployment system uses to identify a build.
- The latest version GUID is published at `https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer` as JSON, under the `clientVersionUpload` field.
- **Pre-March-2024 model:** `RobloxPlayerLauncher.exe` was the protocol handler — it parsed the launch URI, ran the version check, did the update, then launched `RobloxPlayerBeta.exe`.
- **Current model:** `RobloxPlayerBeta.exe` itself is registered as the protocol handler and is launched directly. **When `RobloxPlayerBeta.exe` starts, it checks for client updates. If an upgrade is needed, it launches `RobloxPlayerInstaller.exe` with the same launch URI, which performs the upgrade and then launches the new `RobloxPlayerBeta.exe` with that same URI.**

`RobloxPlayerInstaller.exe` is a stripped-down launcher used only for upgrades (to minimize launch time). **That installer is the "black box" that pops mid-launch** — it's a reactive update triggered by the game client at the moment of launch when it detects a version-GUID mismatch.

> Source: [A deep dive on how the Roblox bootstrapper works (Bloxstrap wiki)](https://github.com/bloxstraplabs/bloxstrap/wiki/A-deep-dive-on-how-the-Roblox-bootstrapper-works). Corroborated by the [bloxstrap repo](https://github.com/bloxstraplabs/bloxstrap) and community write-ups of the `RobloxPlayerBeta → RobloxPlayerInstaller` handoff.

### Bloxstrap's fix: be proactive, not reactive

Bloxstrap registers **itself** as the `roblox-player:` / `roblox:` protocol handler (under `HKEY_CURRENT_USER\SOFTWARE\Classes\roblox-player\shell\open\command`, where the `%1` token is the launch URI). It deliberately keeps the **old** model — the bootstrapper does its work *before* `RobloxPlayerBeta` ever starts — "because it needs to have control over things **before** RobloxPlayerBeta is started."

So Bloxstrap's launch sequence is:
1. Windows hands the `roblox-player:` URI to **Bloxstrap** (not to Roblox).
2. Bloxstrap fetches the latest version GUID from `clientsettingscdn.roblox.com`, compares to what's installed.
3. If they differ, Bloxstrap downloads the package `.zip` set (cached in `%LOCALAPPDATA%\Roblox\Downloads`, from `setup.rbxcdn.com` + mirror fallbacks), extracts to the version directory — **then** spawns `RobloxPlayerBeta.exe`, which now finds a matching version and never triggers `RobloxPlayerInstaller.exe`.

Because Bloxstrap guarantees the correct version is installed *before* `RobloxPlayerBeta` runs, the reactive mid-launch installer never fires. The update box is replaced by Bloxstrap's own progress UI, which happens up front and once.

> Source: same wiki deep-dive (sections on protocol handler registration + version management).

### The multi-instance-aware part (the piece that matters most for RoRoRo)

Bloxstrap serializes the update across concurrent launches with a bootstrapper-level mutex. From the Fishstrap deep-dive (Fishstrap is a Bloxstrap fork that inherits this behavior, and it documents Bloxstrap's serialization explicitly):

- A `Bloxstrap-Bootstrapper` `AsyncMutex` serializes upgrade + launch across concurrent bootstrap attempts. **The update runs ONCE; while one instance holds the mutex during upgrade, the others show "Waiting for other instances..." and reload config once they acquire it.** "This design ensures only a single upgrade cycle precedes all subsequent player launches."
- Background updates use a separate `Bloxstrap-BackgroundUpdater` mutex; a forced upgrade kills any running background updater to do the immediate upgrade.

> Source: [Multi-Instance Management — fishstrap/fishstrap (DeepWiki)](https://deepwiki.com/fishstrap/fishstrap/6.2-multi-instance-management).

**This is the conceptual blueprint for option (a): detect-and-defer = update once, deliberately, before dispatching the batch.** Bloxstrap doesn't have a magic anti-update trick — it just owns the update step and runs it up front, exactly once, instead of letting each client trigger its own reactive installer.

---

## 2. Why RoRoRo hits the interruption

RoRoRo is **not** the bootstrapper and does not want to be (it does not register the protocol handler; it goes through Roblox's own). Its launch path:

- `RobloxLauncher.ExecuteLaunchAsync` / `ExecuteLegacyLaunchAsync` fetch the auth ticket (`RobloxApi.GetAuthTicketAsync`, `src/ROROROblox.Core/RobloxApi.cs:48`), build the `roblox-player:` URI (`RobloxLauncher.BuildLaunchUri`, `src/ROROROblox.Core/RobloxLauncher.cs:427`), then spawn it via the shell:
  - `var pid = _processStarter.StartViaShell(uri);` — `src/ROROROblox.Core/RobloxLauncher.cs:147` (typed-target path) and `:261` (legacy path).

`StartViaShell` on a `roblox-player:` URI invokes **Windows' registered handler for that protocol — which is Roblox's own `RobloxPlayerBeta.exe`** (current model). That means RoRoRo lands squarely in the reactive path: each launched `RobloxPlayerBeta` independently runs the version check and, on mismatch, spawns `RobloxPlayerInstaller.exe`. Bloxstrap sidesteps this because it *replaced* the handler and updates up front; RoRoRo, by design, hands control straight to Roblox's client at the URI boundary and has no hook before the version check runs.

### What's already mitigated (do not re-recommend)

v1.6.0 shipped `AppStorageDefender` (`src/ROROROblox.App/Diagnostics/AppStorageDefender.cs`) — install-resilient identity defense. It stamps the launched account's identity into `appStorage.json` and holds it (up to a 120s max cap, `MainViewModel.cs:929`) until the real `RobloxPlayerBeta` attaches and consumes it. The 120s cap exists precisely because an install box "can postpone the real RPB's first read of appStorage.json well past the old 12s window."

But `AppStorageDefender` itself documents the gap this investigation targets (`AppStorageDefender.cs:49-54`):

> KNOWN LIMITATION (out of scope for item 9): multilaunch DURING an install. If a second account launches while the first's RPB is still blocked behind a Roblox install, the takeover here cancels the first defender before its identity is consumed. **The full fix is the future Bloxstrap install-deferral cycle** (don't dispatch the next launch until the install completes), not a lifetime tweak in this class.

The batch launcher (`MainViewModel.LaunchAllAsync`, `:1007`) staggers launches by a fixed 5000ms (`:1050`). A 5s gap does not cover an install that can run tens of seconds — so a batch started mid-update can have launch N still behind the installer when launch N+1 fires. **The install-deferral cycle is the named, already-acknowledged next step.**

---

## 3. Fishstrap — materially different for updates?

Yes, and it's relevant to the clan.

- **Fishstrap is a Bloxstrap fork** that intentionally ships the two features Bloxstrap won't: **multi-instance** and **update-skipping / channel control**. It gives "full control over update management… even canceling Roblox updates if needed, and downloading specific versions with their associated hashes."
- **Bloxstrap deliberately does NOT ship multi-instance** — long-standing project policy that multi-instance sits in the ToS gray area, so it's left out on principle. (Worth knowing: that's the same gray area RoRoRo lives in.)
- **Fishstrap's multi-instance mechanism is the same family as RoRoRo's:** a watcher process acquires and holds the singleton mutex before launch. Fishstrap targets `ROBLOX_singletonMutex`. **Note:** RoRoRo holds `Local\ROBLOX_singletonEvent` (per CLAUDE.md / spec) — both are in the singleton-suppression family; the exact handle name is read from `roblox-compat.json` at runtime for RoRoRo, which is the right design if Roblox renames it.

> Sources: [Fishstrap — Multi-Instance Management (DeepWiki)](https://deepwiki.com/fishstrap/fishstrap/6.2-multi-instance-management); [Bloxstrap vs Fishstrap comparison](https://bloxstrap.com/bloxstrap-vs-fishstrap/); [fishstrap/fishstrap repo](https://github.com/fishstrap/fishstrap).

Takeaway: Fishstrap already solves both multi-instance and update-deferral for the clan — which makes option (d) below worth real weight.

---

## 4. Options for the next build — ranked

Posture constraint applied throughout (from CLAUDE.md): RoRoRo stays a transparent, documented-endpoints-only tool. No client injection, no anti-tamper fighting, no UA spoofing. **Replacing or relocating Roblox's install (the Bloxstrap takeover) is materially more invasive than RoRoRo's current posture** and is weighed accordingly.

### Rank 1 — (a) Detect-and-defer: update ONCE before the batch ⭐ recommended

Before dispatching `LaunchAllAsync`, detect that Roblox needs an update; complete it once deliberately (auto or prompt), then run the batch. This is exactly Bloxstrap's `Bloxstrap-Bootstrapper` serialization, rebuilt at RoRoRo's layer — and `AppStorageDefender.cs:49-54` already names it as "the full fix."

- **Detect "update pending":** RoRoRo *can* read both sides without becoming the bootstrapper:
  - Latest GUID: `GET https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer`, read `clientVersionUpload`. This is a documented public endpoint — fully posture-fit (same class as the auth-ticket / presence endpoints RoRoRo already calls). UA stays `RORORO/<version>`.
  - Installed GUID: enumerate `%LOCALAPPDATA%\Roblox\Versions\` for the `version-*` dir containing `RobloxPlayerBeta.exe` (Roblox writes the active version's `AppSettings.xml` / has one current player dir). Mismatch ⇒ update pending. **[SPIKE NEEDED]** — confirm the exact "which installed dir is current" read on a live box (Roblox keeps multiple version dirs around; need the reliable "current" signal — likely the most-recent dir holding `RobloxPlayerBeta.exe`, or the GUID referenced by the protocol-handler command string in the registry).
  - Even simpler/robust detect-the-running-installer signal: watch for a `RobloxPlayerInstaller.exe` process. Cheap, no registry parsing, and directly observable.
- **Trigger one controlled update:** launch a single client first (the auth-ticket path already works), let *its* reactive installer run to completion, observe `RobloxPlayerInstaller.exe` exit + `RobloxPlayerBeta.exe` attach, **then** release the rest of the batch. This is option (b) used as the update-trigger mechanism — they compose.
- **Posture fit:** clean. Reading a public version endpoint + watching local processes is squarely within "documented endpoints, transparent tool." No handler takeover, no install relocation.
- **One cycle?** Yes — the pieces are small: a version-check call, a "current installed version" read, and a gate in front of the batch loop. The only unknown is the installed-version read, which the spike resolves.

### Rank 2 — (b) Pre-warm: launch one, wait for any update, then multilaunch

Launch the first client, wait until no `RobloxPlayerInstaller.exe` is running and the first `RobloxPlayerBeta` has attached, then fire the rest. This is the **mechanism** that powers (a)'s "trigger one controlled update" step; on its own (without the version pre-check) it's the cheapest possible win.

- **Pro:** no version-endpoint dependency at all — purely process-observation, which RoRoRo already does (`OnProcessAttached`, `MainViewModel.cs:1390`). Lowest risk, lowest posture cost.
- **Con:** always pays the "launch one and wait" latency even when no update is pending, unless paired with (a)'s pre-check to skip the wait on the common case.
- **Posture fit:** excellent — pure local process watching. **One cycle?** Yes, comfortably. Strong candidate to ship *with* (a) (pre-check decides whether the pre-warm wait is needed).

### Rank 3 — (e) Lightweight UX: "Roblox is updating — hold on" state

Detect `RobloxPlayerInstaller.exe` running and surface a clear updating state instead of failing/mis-launching. Pairs directly with the v1.6.0 defender (which already holds identity through the install).

- **Pro:** tiny, purely additive, no behavior change to the launch path — strictly better than the current silent-stall. Honest UX for the clan ("Roblox is updating, RoRoRo is waiting").
- **Con:** doesn't *prevent* the interruption, only explains it. Best as a companion to (a)/(b), not a standalone answer to the question asked.
- **Posture fit:** perfect. **One cycle?** Trivially — ship it alongside (a)/(b).

### Rank 4 — (d) Detect + lean on Bloxstrap/Fishstrap

The clan already uses Bloxstrap. If Bloxstrap/Fishstrap is the registered `roblox-player:` handler, then RoRoRo's `StartViaShell(uri)` **already routes through it today** — Windows hands the URI to whatever owns the protocol. So when a clan member has Bloxstrap installed, Bloxstrap is *already* doing the proactive update, and RoRoRo's mid-launch box should already be rarer for them. Fishstrap goes further (its own serialized single-upgrade + multi-instance).

- **What RoRoRo could add:** detect the handler owner (read `HKCU\SOFTWARE\Classes\roblox-player\shell\open\command` and check whether it points at Bloxstrap/Fishstrap vs `RobloxPlayerBeta.exe`). If a strap owns it, RoRoRo can (i) trust the strap to handle updates and skip its own pre-check, and (ii) for the bare-Roblox case, *recommend* installing Fishstrap to the clan as the update-handling layer.
- **Caveat — DO NOT chain into a strap's multi-instance:** if RoRoRo holds the singleton mutex AND a strap also spins up its watcher for the same mutex name, that's two managers fighting over one handle. RoRoRo should detect-and-inform, not co-drive. **[NEEDS LIVE VERIFICATION]** — how RoRoRo's mutex hold interacts with a strap-as-handler launch on a clan box. Plausible they coexist (RoRoRo holds the event, strap launches the client), but unverified.
- **Posture fit:** good for *detect + recommend*; risky for *co-drive*. **One cycle?** The detect-and-recommend slice fits one cycle. The co-drive question needs the spike first.

### Rank 5 — (c) Bloxstrap-style bootstrapper takeover — DO NOT adopt

RoRoRo becomes the `roblox-player:` protocol handler and manages the version directory itself.

- **Why not:** this is a different product. It means owning Roblox's full update lifecycle — package download/extract from `setup.rbxcdn.com`, version-dir management, channel handling, and tracking Roblox's deployment changes forever. That's a permanent maintenance tax and a much larger Roblox-relations surface than RoRoRo's "we hold a mutex and hit documented endpoints" posture. It also collides head-on with Bloxstrap/Fishstrap if a clan member runs both (handler ownership is exclusive). It is strictly more invasive than the current design and buys nothing that (a)+(b) don't.
- **Verdict:** out of scope, on principle — the same wall reasoning as the MaCro boundary. Note it and move on.

---

## 5. Recommendation for the next build

**Ship (b) + (a) + (e) together as one "install-deferral" cycle, with (d)'s detect-and-recommend as a fast-follow.**

Concretely, the next-build cycle:
1. **(b) pre-warm gate** — process-observation only, zero new endpoints, lowest risk: launch one, wait for `RobloxPlayerInstaller.exe` to clear + first `RobloxPlayerBeta` attach, then release the batch. This alone closes the `AppStorageDefender.cs:49-54` gap.
2. **(a) version pre-check** — `clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer` vs installed GUID, so the common (no-update) case skips the pre-warm wait and launches fast. Documented-endpoint, posture-clean.
3. **(e) UX state** — "Roblox is updating — hold on" while the installer runs. Honest, additive, pairs with the v1.6.0 defender.
4. **(d) fast-follow** — detect Bloxstrap/Fishstrap as handler; skip RoRoRo's pre-check when a strap owns it, and add a clan-facing recommendation to use Fishstrap for update handling.

This is exactly Bloxstrap's playbook (update once, deliberately, before launching the batch) rebuilt at RoRoRo's layer **without** taking over the bootstrapper — so it respects the documented-endpoints / no-takeover posture.

### Confirmed vs needs live verification

- **Confirmed from sources:** the interruption is RobloxPlayerBeta's reactive version check spawning `RobloxPlayerInstaller.exe`; the version endpoint + version-GUID directory model; Bloxstrap's proactive-update-before-launch design; Bloxstrap's single-upgrade serialization mutex; Fishstrap's update-control + multi-instance via singleton mutex.
- **[SPIKE NEEDED] before building (a):** the reliable "which installed version GUID is current" read on a live box (or just rely on watching `RobloxPlayerInstaller.exe` as the pending-update signal, which sidesteps the registry/dir read entirely — cheaper and may be enough).
- **[NEEDS LIVE VERIFICATION] before building (d) co-drive:** how RoRoRo's `ROBLOX_singletonEvent` hold interacts with a Bloxstrap/Fishstrap-as-handler launch on a clan box. Detect-and-recommend is safe regardless; co-driving the mutex is the unverified part.

---

## Sources

- [A deep dive on how the Roblox bootstrapper works (Bloxstrap wiki)](https://github.com/bloxstraplabs/bloxstrap/wiki/A-deep-dive-on-how-the-Roblox-bootstrapper-works) — protocol handler, version GUID, `clientsettingscdn` endpoint, RobloxPlayerBeta → RobloxPlayerInstaller flow, proactive-update design.
- [bloxstraplabs/bloxstrap repo](https://github.com/bloxstraplabs/bloxstrap)
- [Multi-Instance Management — fishstrap/fishstrap (DeepWiki)](https://deepwiki.com/fishstrap/fishstrap/6.2-multi-instance-management) — `Bloxstrap-Bootstrapper` single-upgrade serialization, `ROBLOX_singletonMutex` watcher.
- [Bloxstrap vs Fishstrap (comparison)](https://bloxstrap.com/bloxstrap-vs-fishstrap/) — Fishstrap update-skipping/channel control; Bloxstrap's no-multi-instance policy.
- [fishstrap/fishstrap repo](https://github.com/fishstrap/fishstrap)
- RoRoRo source: `src/ROROROblox.Core/RobloxLauncher.cs:147,261,427`; `src/ROROROblox.Core/RobloxApi.cs:48`; `src/ROROROblox.App/Diagnostics/AppStorageDefender.cs:49-54`; `src/ROROROblox.App/ViewModels/MainViewModel.cs:929,1007,1050,1390`.
