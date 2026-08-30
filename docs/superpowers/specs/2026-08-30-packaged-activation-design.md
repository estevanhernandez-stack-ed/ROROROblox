# RoRoRo — packaged activation: run-on-login and inbound URIs on Store/sideload installs

---
**Date:** 2026-08-30
**Status:** Approved design — implementation in the same session
**Author:** The Architect + Este
**Scope:** Make the two features that silently do nothing on packaged (MSIX) installs work: the Settings run-on-login toggle, and cold-start URI activation (`roblox-rororo:` joins and Discord's `discord-<appid>` launch scheme). Ships as v1.24.0.0 with F-125 and the corrected privacy policy.
**Origin:** The 2026-08-30 onboarding pass's live test: the Store 1.23 build, launched alone, registered its URI schemes and Run key into the package's virtual registry hive — invisible to Explorer, Discord, and winlogon.
---

## §0 What was measured before designing

1. **Packaged registry writes are virtualized; file writes are not.** Live test 2026-08-30 (decisions.md, same date): after a Store-build run, the real `HKCU\Software\Classes\roblox-rororo` still pointed at the dev build while the package's `SystemAppData\Helium\UserClasses.dat` carried the write; the same run wrote its log and cache files into the real `%LOCALAPPDATA%\ROROROblox`.
2. **The toggle lies today.** `StartupRegistration.IsEnabled()` reads the merged registry view, so a packaged install reads back its own virtualized Run value: Settings shows run-on-login enabled while Windows never runs it.
3. **Cold-start URIs arrive as plain argv.** `JoinUriParser.TryParse(string[] args)` scans arguments for `roblox-rororo://join/…`; the single-instance guard relays the same raw argument. Nothing parses activation events.
4. **Two schemes are in play.** Ours (`roblox-rororo`, written by `JoinUriScheme.Register`) and Lachee's (`discord-1501748116985221272`, written inside `DiscordRpcClient.RegisterUriScheme()`, command fixed up by `JoinUriScheme.FixupDiscordSchemeCommand`). Lachee's call must keep running everywhere — its internal "scheme registered" flag gates `Subscribe(Join)`.
5. **Manifest schema facts** (learn.microsoft.com, fetched 2026-08-30):
   - `uap10:Protocol` supports `Parameters` (e.g. `%1`) so a packaged desktop app receives the URI **on the command line**. Namespace `…/uap/windows10/10`, min build 19041; our floor is 19045.
   - `desktop:StartupTask` (`TaskId` required, `Enabled`, `DisplayName`) under `desktop:Extension Category="windows.startupTask"`, namespace `…/desktop/windows10`; runtime control is `Windows.ApplicationModel.StartupTask` (WinRT), which requires a `net10.0-windows10.0.19041.0`-style TFM.
   - `desktop6:RegistryWriteVirtualization` **requires the `unvirtualizedResources` restricted capability**, documented as "intended … only by certain types of desktop PC games published by Microsoft and our partners."

## §1 The decision

**Use the platform's own extension points; stop writing the registry on packaged builds.**

1. **Manifest** (`Package.appxmanifest`, new namespaces `desktop` and `uap10`):
   - `desktop:Extension Category="windows.startupTask"` → `desktop:StartupTask TaskId="RoRoRo" Enabled="false" DisplayName="RoRoRo"`.
   - `uap10:Extension Category="windows.protocol"` → `uap10:Protocol Name="roblox-rororo" Parameters="%1"`.
   - A second `uap10:Protocol Name="discord-1501748116985221272" Parameters="%1"` so Discord's cold-start launch scheme resolves to the packaged app too.
2. **TFM:** `ROROROblox.App`, `ROROROblox.Tests`, `ROROROblox.PluginTestHarness` move `net10.0-windows` → `net10.0-windows10.0.19041.0` (Core stays; a higher TFM may reference a lower one). This is what exposes `Windows.ApplicationModel.StartupTask`.
3. **Code:**
   - `Startup/PackagedStartupRegistration : IStartupRegistration` drives `StartupTask.GetAsync("RoRoRo")` / `RequestEnableAsync()` / `Disable()`. `DisabledByUser` and `*ByPolicy` states surface as a thrown `InvalidOperationException` naming Windows **Settings → Apps → Startup**, which the existing Settings-page catch turns into a warning dialog plus a reverted toggle.
   - `IDistributionMode` moves into DI (today `Win32DistributionMode` is new'd inline for the Plugins page); the composition root registers `PackagedStartupRegistration` when `IsPackaged`, `StartupRegistration` otherwise.
   - When packaged, `App.OnStartup` **skips** `JoinUriScheme.Register` and `JoinUriScheme.FixupDiscordSchemeCommand` — the manifest owns both schemes there. Unpackaged behavior is unchanged.
4. **Fence:** `PackagedActivationManifestTests` parses `Package.appxmanifest` and asserts: the startup task exists with `TaskId="RoRoRo"` and `Enabled="false"`; both protocols exist with `Parameters="%1"`; and the Discord protocol name matches the application id committed in `appsettings.json` — the manifest and the id can no longer drift apart silently.

## §2 Rejected alternatives, with reasons

- **Disable registry write virtualization** (`desktop6:RegistryWriteVirtualization`): requires the `unvirtualizedResources` restricted capability — Partner Center approval scoped to Microsoft-partner games. Dead on arrival for this listing, and it would also have left Run/scheme commands pointing at versioned `WindowsApps\…_1.x.0.0_…` paths that die on every Store update.
- **AppExecutionAlias + real-registry writes:** same restricted-capability blocker; the alias half solved only the stale-path problem.
- **`Enabled="true"` on the StartupTask** (no WinRT, no TFM change): forces run-on-login ON at first launch for every Store user; the product default is off and the toggle would still control nothing.
- **Activation-args (`AppInstance.GetActivatedEventArgs`) instead of `Parameters`:** a second inbound-URI code path to keep in lockstep with argv parsing; `Parameters="%1"` makes packaged activation land in the exact argv shape the unpackaged registry command already produces.

## §3 What deliberately does not change

- Unpackaged (Velopack/portable/dev) builds keep the registry paths verbatim.
- Lachee's `RegisterUriScheme()` still runs on packaged builds (its write is virtualized and harmless; its internal flag gates `Subscribe`). Only our fixup is skipped.
- The 2026-08-03 rule "no URI entry point in a dark build" is weakened on packaged installs: manifest protocols are static. Accepted because every shipped build carries the application id, and dispatch remains gated on the live `JoinEnabled` setting either way.
- Old Run-key values from pre-v1.24 packaged installs stay in the virtual hive; nothing migrates them (they never worked).

## §4 Test plan

1. Unit: manifest fence (above); `PackagedStartupRegistration` state mapping over a seam (the WinRT call itself is not unit-testable; the wrapper's state→behavior table is).
2. Full suite on the bumped TFMs, x64 + arm64 via CI.
3. `scripts/build-msix.ps1 -Sideload` — makeappx validates the new manifest elements.
4. Live packaged test (replaces the installed Store build with the sideload build until the next Store update; maintainer consent per step): toggle run-on-login → the task appears in Task Manager → Startup apps; sign out/in → RoRoRo starts; `start roblox-rororo://join/<secret>` cold-starts the app with the confirm dialog; Windows Settings → Apps → Startup shows "RoRoRo".
