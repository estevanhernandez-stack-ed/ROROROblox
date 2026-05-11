# Notes for certification — reviewer letter (v1.4.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.4 introduces a plugin system. This letter is structured to surface the architecture decision (plugins are out-of-process, separately distributed, never bundled in the Store binary) before any feature copy — the reviewer needs to see policy 10.2.2 explicitly addressed in the first 30 seconds.

---

```
Hello reviewer,

Thank you for your time on v1.4.0.0. This release adds a plugin system
to RoRoRo. The architectural choice we made is specifically designed to
keep the Store-listed binary inside the bounds of policy 10.2.2; this
letter leads with that decision.

WHAT'S NEW IN v1.4

RoRoRo v1.4 introduces an out-of-process plugin system. RoRoRo
(Store-distributed) hosts a gRPC server on a per-user ACL'd Windows
named pipe (\\.\pipe\rororo-plugin-host). Plugins are SEPARATE PRODUCTS
— separate Windows EXEs, separate distributions, sideload-only via
GitHub releases. RoRoRo never loads, fetches, embeds, or auto-installs
plugin code. The Store-listed product's described functionality stays
"multi-launcher for Windows."

POLICY 10.2.2 ALIGNMENT

Policy 10.2.2 states that an app must not contain or permit dynamic
inclusion of code or content that fundamentally changes the
functionality described in the Store listing. Our architecture honors
this:

  1. RoRoRo's MSIX does not contain any plugin code. Plugins are not
     bundled in the package. Inspect the MSIX contents — there is no
     plugin EXE inside.

  2. Plugin install is user-initiated. RoRoRo never auto-fetches
     plugins, never polls for new plugins, never reads a curated list
     from a server. The user pastes a GitHub release URL into the
     Plugins window and clicks Install. This matches the same shape as
     "user runs Setup.exe to install a separate program" — RoRoRo just
     happens to be the ergonomic wrapper for the Windows side of that
     pattern.

  3. Plugin downloads are SHA-256-verified before extraction. Each
     plugin release ships three artifacts: manifest.json, manifest.sha256,
     plugin.zip. RoRoRo refuses to extract if the actual SHA-256 of the
     downloaded zip does not match the published hash. This isn't a
     malware defense per se — it's an integrity contract between the
     plugin author and the user, and it makes any silent bait-and-switch
     attack on the wire detectable.

  4. Plugin capabilities are gated by user consent. When a plugin is
     installed, RoRoRo shows a consent sheet listing every capability
     the plugin has declared in its manifest, with plain-language
     explanations. The user accepts capabilities individually. The
     gRPC server's interceptor blocks any RPC call whose required
     capability the user has not granted, returning PERMISSION_DENIED.

  5. The plugin process runs as the same Windows user as RoRoRo, no
     elevated privileges. The named pipe inherits the current Windows
     user's ACL — connections from other Windows users on the same
     machine cannot reach the pipe.

  6. Plugins are never required for RoRoRo's core functionality.
     Multi-instance launching, account management, FPS limiting,
     default-game widgets, and every other v1.0-v1.3 feature works
     identically with zero plugins installed. The Plugins window
     correctly shows an empty state on a fresh install.

WHAT v1.4 SHIPS

  - The plugin system itself: contract NuGet (ROROROblox.PluginContract),
    Plugins/ App-side module, gRPC over named-pipe wire transport,
    capability interceptor, consent UI, install flow, status banner.
  - Documentation for plugin authors at docs/plugins/AUTHOR_GUIDE.md
    (linked from README + privacy policy update).
  - Three milestone tags in source (plugin-system-m1/m2/m3) corresponding
    to foundation / wire transport / user-facing UI.

WHAT v1.4 DOES NOT SHIP

  - No plugins. The first plugin (auto-keys, an AFK-defeat cycler) is
    being authored in a sibling repository and will be distributed via
    its own GitHub releases page when it's ready. It will never be
    bundled into RoRoRo.

WHAT REMAINS UNCHANGED FROM v1.3

  - Identity Name (626LabsLLC.RoRoRoBlox).
  - Source repository URL.
  - Privacy policy URL.
  - Roblox-side compatibility surface — same documented endpoints, same
    user-agent ROROROblox/<version>, no browser spoofing.
  - DPAPI-encrypted account vault.
  - Authenticode signing on every shipped MSIX.

PRIVACY POLICY UPDATE

The privacy policy has been updated to disclose the new
%LOCALAPPDATA%\ROROROblox\ paths added in v1.4:

  - consent.dat (DPAPI-encrypted, per-plugin consent records)
  - plugins\<plugin-id>\ (installed plugin files)

No telemetry has been added. RoRoRo continues to make no analytics,
crash-reporting, or usage-tracking calls. Plugins are themselves
separate products — their network behavior is governed by their own
privacy policies, which they are required to publish.

WHAT THE APP IS

RoRoRo is a Windows launcher that lets a user save multiple Roblox
account credentials and launch any of them — including several at once,
in separate client windows — without having to log out and back in
between accounts. v1.4 adds a plugin system that lets users extend the
launcher with separately distributed Windows applications.

WHAT IT IS NOT

RoRoRo does not modify the Roblox client. It does not inject into, hook
into, attach a debugger to, or read memory from the Roblox process. It
does not bundle game content, scripts, plugins, exploits, or automation.
Plugins, when installed, run as their OWN processes — not inside RoRoRo,
not inside the Roblox client. RoRoRo continues to include no chat, no
multiplayer networking, no user-generated content, no in-app purchases,
and no advertising.

HOW IT WORKS (TECHNICAL — v1.4 additions)

The plugin system uses Microsoft-canonical patterns:

  - Microsoft.AspNetCore.Server.Kestrel + named-pipe transport (shipped
    in the .NET 8+ shared framework).
  - Grpc.AspNetCore for the gRPC server pipeline.
  - Grpc.Net.Client for the plugin-side client.
  - System.IO.Pipes for the underlying transport (per-user ACL
    inherited from the current Windows user).

The wire format is HTTP/2 over a Windows named pipe. Plugins implement
the gRPC client; RoRoRo is the server. Documented at
https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes.

TRADEMARK NOTICE

"Roblox" and the Roblox logo are trademarks of Roblox Corporation.
RoRoRo is an independent third-party tool, not affiliated with, endorsed
by, or sponsored by Roblox Corporation. Plugins distributed for use with
RoRoRo are also independent third-party tools, not affiliated with
RoRoRo or with Roblox Corporation.

PRIVACY POLICY

Live at:
https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/

Source code is open under the MIT License at:
https://github.com/estevanhernandez-stack-ed/ROROROblox

The plugin author guide is at:
https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/plugins/AUTHOR_GUIDE.md

WHERE TO LOOK IF YOU HAVE CONCERNS ABOUT 10.2.2

  - Inspect the MSIX. There is no plugin EXE inside.
  - Read docs/superpowers/specs/2026-05-09-rororo-plugin-system-design.md
    in the source repository — the architecture decision and policy
    framing are written down before a single line of code was committed.
  - Walk the install flow on a clean Win11 VM with no plugins. The
    Plugins window correctly shows an empty state.
  - The CapabilityInterceptor's PERMISSION_DENIED path is testable in
    integration test coverage at
    src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs
    (RequestLaunch_DeniedWhenCapabilityNotGranted).

If anything in this submission is unclear, please reach out and we will
respond same-day.

Thank you again for your time and consideration.

Estevan Hernandez
626 Labs LLC
```

---

## Defenses by clause (cheat sheet for v1.4 resubmission edits)

If this submission is rejected on a clause, reinforce the defense already present rather than introducing new arguments:

| Clause | Defense paragraph in this letter | If rejected, what to add |
|---|---|---|
| **10.2.2** dynamic-code inclusion | "POLICY 10.2.2 ALIGNMENT" — six numbered points | Offer a video walkthrough of MSIX inspection + the empty-state Plugins window on a clean VM. The architecture is the defense; demonstration is the proof. |
| **10.1.4.4.b** unique lasting value | (carried forward from v1.1.2.0) | Add: "Plugin system is itself a unique lasting feature — no other Roblox launcher exposes a typed-contract plugin surface." |
| **10.10** security / functionality | "HOW IT WORKS (TECHNICAL — v1.4 additions)" + "WHERE TO LOOK IF YOU HAVE CONCERNS ABOUT 10.2.2" | Cite the named-pipe per-user ACL, the SHA-256 verify on plugin downloads, the consent-gated capability surface. Privacy policy lists every endpoint hit. |
| **10.2.10** prohibited uses | "WHAT IT IS NOT" | RoRoRo does not enable malicious behavior; plugins run as their own processes and are governed by their own Store / sideload review separately. RoRoRo's facilitation is no different than Windows's facilitation of Setup.exe. |

## Pre-submission sanity check (v1.4-specific)

Before clicking **Submit for review** for v1.4.0.0, eyeball:

- [ ] Version in `Package.appxmanifest` is `1.4.0.0` (Phase 3 of `release-playbook.md` should have done this atomically with the csproj)
- [ ] Privacy policy URL contents include the v1.4 disclosure for `consent.dat` + `plugins\<id>\` paths
- [ ] Inspect the `.msix` after build — there is no plugin EXE inside (`tar tf <msix> | grep -i plugin` should match only `*.PluginContract.dll`-style framework refs, not any `*Plugin*.exe`)
- [ ] In-app Plugins window shows the empty state on a fresh install
- [ ] Right-click tray → Plugins menu item is present
- [ ] Manual smoke on clean Win11 VM completes per Phase 7 of `release-playbook.md`
- [ ] Reviewer letter (this file's `---`-delimited block) is pasted verbatim into Partner Center → Submission options → Notes for certification
- [ ] Authenticode signature on the MSIX validates with `signtool verify`

## Source

This file is the v1.4.0.0 reviewer letter. Predecessor letters live at:

- v1.1.2.0: [`reviewer-letter.md`](reviewer-letter.md) (the rename letter)

When v1.5+ ships, copy this file to `reviewer-letter-1.5.0.0.md` and update — predecessors stay frozen for audit.
