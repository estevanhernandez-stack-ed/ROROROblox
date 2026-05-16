# Notes for certification — reviewer letter (v1.4.3.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.4.3.0 is a MINOR bump on top of v1.4.2.0 focused on the plugin-system user experience and forward-compatibility. The plugin-system policy 10.2.2 alignment letter from v1.4.0.0 still applies in full — this letter points back to that submission rather than re-litigating the architecture. Reviewers picking up v1.4.3.0 cold can read [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) for the full plugin-system framing.
>
> The one new item that affects the disclosure surface — a `system.read-screen` capability vocabulary entry — is called out below. It is disclosure-only; the host adds no new Windows permission and no new code path that accesses the screen.

---

```
Hello reviewer,

Thank you for your time on v1.4.3.0. This release is a plugin-system
UX and forward-compatibility cut on top of v1.4.2.0. No new
runFullTrust-adjacent capabilities are requested. No new outbound
network endpoints. No new dependencies. The plugin-system policy 10.2.2
alignment documented in the v1.4.0.0 cert notes still applies in full
and is not re-litigated here.

WHAT CHANGED IN v1.4.3.0

The release bundles four areas:

  1. ONE NEW CAPABILITY VOCABULARY ENTRY: system.read-screen.

     This is added to the plugin capability catalog in:

       src/ROROROblox.App/Plugins/PluginCapability.cs

     The capability is DISCLOSURE-ONLY. RoRoRo does not implement any
     new screen-reading code path, does not call any new Windows API,
     and does not request any new MSIX capability. The entry lets
     plugin AUTHORS — who write their own separate EXE that runs in
     their own process — declare on their manifest that their EXE will
     read pixels from the user's screen. The user reads this declaration
     on the existing per-capability consent sheet and can decline the
     capability without affecting any other plugin behavior.

     Plugin processes already run in the user's session under the same
     user account that launched RoRoRo and already have full access to
     their own user-space environment (this is the policy 10.2.2 frame
     from v1.4.0.0 — RoRoRo cannot sandbox a separate EXE). The new
     vocabulary entry exists so plugin authors can be HONEST with users
     about behavior the OS already permits. RoRoRo itself does no new
     screen-capture work.

     The capability catalog is enumerated in PluginCapability.cs lines
     ~26-43; the new entry is one constant + one Display() catalog row.
     No code outside the catalog mentions system.read-screen.

  2. THREE NEW OPTIONAL MANIFEST FIELDS (back-compat schema extension):

     In src/ROROROblox.App/Plugins/PluginManifest.cs:

       - autostartDefault ("on" | "off"): plugin authors can declare
         the INITIAL ConsentRecord.AutostartEnabled value for a fresh
         install. Does not override existing consent on re-install.
         User can flip it at any time via the existing Autostart
         checkbox.
       - minHostVersion (semver string): plugin authors can declare
         the minimum RoRoRo version their plugin requires. PluginInstaller
         compares to typeof(App).Assembly.GetName().Version before the
         zip download and refuses installs that would require a newer
         host with an actionable "Update RoRoRo" message.
       - entrypoint (filename): plugin authors can declare their EXE
         filename when it does not match <plugin-id>.exe. Validated at
         install time — the file must exist in the unpacked zip, else
         the install is rejected with an actionable error and the
         install directory is cleaned up.

     schemaVersion stays at 1. Adding nullable JSON fields is
     back-compat both ways: old host + new manifest ignores unknown
     fields, new host + old manifest defaults the missing fields to
     today's behavior (autostart off, no host-version gate, EXE
     name from plugin id).

  3. PER-PLUGIN LAUNCH BUTTON ON THE PLUGINS WINDOW (UI-only change).

     In src/ROROROblox.App/Plugins/PluginsWindow.xaml +
     PluginsViewModel.cs: each row in the Plugins window gets a Launch
     button right of its Autostart checkbox. Click spawns the plugin's
     EXE on demand via the existing PluginProcessSupervisor.Start path
     that was added pre-v1.4.2.0 (commits bee8c23 / ec200c6 — already
     submitted to the Store as part of v1.4.2.0's predecessor bundle).
     The button binds to a new IsRunning observable on the per-row VM
     so it disables while the plugin is running and re-enables on the
     supervisor's PluginExited event.

     No new Windows API surface, no new MSIX capability, no new
     network calls. The Launch button is a UI convenience over the
     existing plugin process lifecycle that was already exercised by
     the install dialog's "install and run" flow.

  4. PLUGIN LIFECYCLE FIXES ALREADY ON main PRE-v1.4.3.0
     (bundled together for the first time in a Store cut):

       - In-session start on fresh install (bee8c23): the plugin's
         EXE spawns immediately after consent is granted, instead of
         requiring a RoRoRo restart.
       - Orphan-kill on re-install (3f39228, 6637772, 05151af): the
         installer now kills any plugin process running out of the
         target install directory — including processes that outlived
         the RoRoRo session that started them — before wiping the
         directory. Closes a Directory.Delete "access denied" path
         users hit when re-installing over a running plugin.

     These already passed cert as part of v1.4.2.0's predecessor work
     in the main branch but were not previously bundled into a Store
     submission. They are called out here for transparency.

WHY THE MANIFEST CHANGES MATTER

The user-side bug that motivated the cut: installing a plugin on
v1.4.2.0 with autostart off left the plugin EXE never started, even
across RoRoRo restarts. The user had to toggle autostart on AND
restart RoRoRo. (1) closes the symptom directly. The manifest
forward-flags (2) let plugin authors avoid the next class of bug
proactively — when a plugin needs a newer host than the user has, the
user gets a clear message instead of a downstream symptom (a plugin
gRPC call that fails because the host method doesn't exist yet).

PluginInstaller now takes a Version constructor parameter so the
host-version gate can be tested under mocked host versions. DI passes
typeof(App).Assembly.GetName().Version, defaulting to 0.0.0.0 (safe
— that refuses every minHostVersion-bearing manifest, which is the
right failure mode for an environment that can't resolve the host's
assembly version).

WHAT STAYED THE SAME FROM v1.4.2.0

  - Identity Name (626LabsLLC.RoRoRoBlox), Publisher CN, Privacy
    policy URL, Source repository URL, Roblox-side compatibility
    surface, DPAPI-encrypted account vault, Authenticode signing.
  - The plugin contract WIRE SURFACE
    (src/ROROROblox.PluginContract/Protos/plugin_contract.proto) is
    unchanged. Plugins built against v1.4.0.0's contract keep working
    unchanged.
  - The plugin manifest schemaVersion is unchanged at 1. The three
    new fields are nullable optionals.
  - The plugin policy 10.2.2 alignment story documented in the
    v1.4.0.0 cert notes: MSIX still contains no plugin code, plugin
    install is still user-initiated from a GitHub URL, downloads are
    still SHA-256-verified, capabilities are still consent-gated, the
    named pipe is still per-user ACL'd, and plugins remain optional
    (Plugins window still shows empty state on a fresh install).
  - No telemetry has been added. RoRoRo continues to make no
    analytics, crash-reporting, or usage-tracking calls.

FILES TOUCHED (relative to v1.4.2.0)

  - src/ROROROblox.App/Plugins/PluginCapability.cs — one capability
    constant + one catalog row (system.read-screen).
  - src/ROROROblox.App/Plugins/PluginManifest.cs — three nullable
    properties + DTO fields + autostartDefault enum-value validation.
  - src/ROROROblox.App/Plugins/PluginInstaller.cs — hostVersion
    constructor parameter, minHostVersion pre-check, entrypoint
    validation, autostartDefault → ConsentRecord wiring.
  - src/ROROROblox.App/Plugins/InstalledPlugin.cs — ExecutablePath
    honors manifest.Entrypoint when set.
  - src/ROROROblox.App/Plugins/PluginsViewModel.cs — LaunchPluginCommand
    and per-row IsRunning state.
  - src/ROROROblox.App/Plugins/PluginsWindow.xaml — Launch button on
    each row, bound to LaunchPluginCommand.
  - src/ROROROblox.App/App.xaml.cs — DI passes the App assembly
    version into PluginInstaller.
  - src/ROROROblox.App/ROROROblox.App.csproj — Version 1.4.2.0 →
    1.4.3.0, InternalsVisibleTo(ROROROblox.Tests) for VM test access.
  - src/ROROROblox.App/Package.appxmanifest — Identity Version
    1.4.2.0 → 1.4.3.0.
  - docs/plugins/AUTHOR_GUIDE.md — capability table row for
    system.read-screen.
  - src/ROROROblox.Tests/ — 11 new tests across PluginManifestTests,
    PluginInstallerTests, PluginRowTests covering the new fields,
    the version gate, the entrypoint validation, and the per-row
    INPC contract.

The manifest delta from v1.4.2.0 is one attribute: Identity Version
1.4.2.0 → 1.4.3.0. Nothing else in Package.appxmanifest changed.

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

The v1.4.2.0 reviewer letter (most recent predecessor) is at:
https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/store/reviewer-letter-1.4.2.0.md

If anything in this submission is unclear, please reach out and we
will respond same-day.

Thank you again for your time and consideration.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v1.4.3.0-specific)

- [ ] Version in `Package.appxmanifest` is `1.4.3.0`
- [ ] Version in `ROROROblox.App.csproj` is `1.4.3.0`
- [ ] Manifest delta from v1.4.2.0 is **only** the Identity Version attribute (diff before submit)
- [ ] `dist/RORORO-Store.msix` is built off the `v1.4.3.0` tag
- [ ] Reviewer letter (this file's `---`-delimited block) is pasted verbatim into Partner Center → Submission options → Notes for certification
- [ ] Authenticode signature on the MSIX validates with `signtool verify`
- [ ] `system.read-screen` exists in `PluginCapability.Catalog` and `AUTHOR_GUIDE.md` capability table; no other RoRoRo code references it (it is disclosure-only)
- [ ] No new package references in `ROROROblox.App.csproj` between v1.4.2.0 and v1.4.3.0
- [ ] `dotnet test src/ROROROblox.Tests` passes — manifest-flags + Launch-button tests included

## Source

This file is the v1.4.3.0 reviewer letter. Predecessor letters live at:

- v1.4.2.0: [`reviewer-letter-1.4.2.0.md`](reviewer-letter-1.4.2.0.md) (AppStorageDefender captcha fix)
- v1.4.1.0: [`reviewer-letter-1.4.1.0.md`](reviewer-letter-1.4.1.0.md) (AccountSummary RobloxUserId hotfix)
- v1.4.0.0: [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (plugin-system policy 10.2.2 framing — still load-bearing)
- v1.1.2.0: [`reviewer-letter.md`](reviewer-letter.md) (the rename letter)

When v1.5+ ships, copy this file to `reviewer-letter-1.5.0.0.md` and update — predecessors stay frozen for audit.
