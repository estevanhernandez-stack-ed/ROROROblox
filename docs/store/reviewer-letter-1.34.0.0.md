# Notes for certification — reviewer letter (v1.34.0.0)

> Paste the block below the `---` marker — everything from `Hello reviewer,` to `626 Labs LLC` —
> into Partner Center → your app → **Submission options** → **Notes for certification**. The same
> text, paste-ready and already plain, is `reviewer-letter-1.34.0.0.paste.txt`.
> **Partner Center only.** `submit_write.py` never sends `notesForCertification`, so confirm it on
> the Partner Center screen or not at all.
>
> **Both twins written together, in Phase 2.** v1.33 produced only the `.paste.txt` and the gap
> surfaced at Phase 7, because the listing console reads the letter by its `.md` name from the pinned
> ref. This time the `.md` and the `.paste.txt` were written in the same pass, and the body below is
> the paste file verbatim.
>
> **A single-version delta, and a small one.** Certification last saw v1.33.0.0. 81 files changed,
> +4,457/−155, 42 under `src/`. Theme sharing, metric alerts naming only running accounts, idle
> threshold behaviour, the package-version fix, three hints, and one plugin-facing contract change.
>
> **Section 2 names the plugin-facing change first, and says who can read it.** `GetHostInfo` gains
> three fields, one of them the host's own executable path. A reviewer who found that unannounced
> would be right to ask what can read it. Checked rather than assumed: `RpcMethodCapabilityMap` maps
> `GetHostInfo` to `null`, a free read, unchanged from v1.33; the plugin pipe accepts only the
> current Windows user (Kestrel's `CurrentUserOnly` default, verified live 2026-08-30); and that
> user's processes can already read the app's path from the process list. The letter says "needs no
> capability" rather than implying a gate that is not there.
>
> **Section 5 asks only for what a reviewer can do without help.** Making a custom theme means
> pasting an AI-generated theme description into the builder, which a reviewer cannot be expected
> to do. So the exercise stops at what is checkable cold: the two buttons are where the letter says,
> Share is greyed out under a built-in theme, and Import opens the standard file dialog. A first draft
> walked through a full share-and-import round trip and was cut back for exactly that reason.
>
> **Every claim was verified against `git diff v1.33.0.0..chore/v1.34-pretag-fixes`, not assumed:**
>
> - 81 files changed, +4,457/−155; 42 under `src/`.
> - `Package.appxmanifest`: the only differing line is the `Version` attribute (1.33.0.0 to 1.34.0.0).
> - `plugin_contract.proto`: three added lines, `install_flavour = 4`, `process_path = 5`,
>   `informational_version = 6` on `HostInfo`, plus a comment. Nothing removed or renumbered.
> - Input-synthesis and interop grep over added lines under `src/`: **zero** hits for `SendInput`,
>   `keybd_event`, `mouse_event`, `SetWindowsHookEx`, `PostMessage`, `SendMessage`,
>   `WriteProcessMemory`, `OpenProcess`, `DllImport`, `LibraryImport`, `CreateRemoteThread`,
>   `SetCursorPos`.
> - URL strings in added lines under `src/`: **zero**. Three appear in added lines elsewhere, all in
>   `docs/`, none in the build.
> - New `PackageReference` lines: **zero**. Capability or RPC-map changes: **zero**.
> - Theme sharing goes through `Microsoft.Win32` save and open dialogs; the manifest still declares
>   only `runFullTrust`.

---

Hello reviewer,

RoRoRo is a launcher that runs several Roblox clients side by side, each signed
in as a different account the user saved. This is v1.34.0.0. Certification last
saw v1.33.0.0, so this is one version's delta, and a small one.

1. WHAT CHANGED

  - Theme sharing. The theme builder gains Share theme and Import theme. Share
    writes the user's own custom theme to a file through the standard Windows
    save dialog; Import reads one back through the standard open dialog and
    applies it. The user grants access by choosing the file. Nothing is
    uploaded, no new capability is declared, and broadFileSystemAccess is still
    NOT declared.
  - Metric alerts name only accounts running on this PC. A notification about
    a number used to list every saved account; it now lists only the ones that
    are open. When none of them moved the number, the text says so.
  - The idle-warning threshold now always matches a value the picker offers,
    and a hand edit to the settings file takes effect within about 30 seconds
    instead of at the next start.
  - About and the app's log now show the installed package version on packaged
    builds.
  - Three short explanatory lines in Settings, under the idle and alert-sound
    controls.

2. THE ONE PLUGIN-FACING CHANGE, STATED PLAINLY

The app can launch optional plugin programs the user installs and consents to.
One existing read, GetHostInfo, returns three more fields: install_flavour
(store, sideload or dev), process_path (the app's own executable path) and
informational_version (its version plus build commit). The plugin contract
version moves from 0.11.0 to 0.12.0.

  - The change is additive. Fields 1 to 3 are unchanged, and a plugin built
    against the older contract keeps working without seeing the new fields.
    Verified against the diff of plugin_contract.proto, not assumed.
  - No RPC, capability or consent prompt was added. GetHostInfo was already a
    read that needs no capability, and still is.
  - The new fields describe the app's own binary, not the user. The plugin
    channel accepts only processes running as the same Windows user, and any
    such process can already see the app's path in the process list.

3. NOTHING ELSE MOVED

  - Package.appxmanifest differs from v1.33.0.0 by the Version attribute only.
    Same runFullTrust capability, languages, protocols and startup task.
  - No new network address and no new network call of any kind. No URL string
    appears in any added line of source code.
  - No new package dependency.
  - No new permission, and no change to what data the app stores.

4. NO INPUT SYNTHESIS, NO NEW INTEROP

No added line calls SendInput, keybd_event, mouse_event, SetWindowsHookEx,
PostMessage, SendMessage, WriteProcessMemory or OpenProcess, and no DllImport
or LibraryImport was added anywhere in the change. The app still synthesizes no
input and injects into no process.

5. WHAT YOU CAN EXERCISE

No Roblox account is needed for this.

Open Settings, then Language and Appearance, and open the theme builder. Share
theme and Import theme sit beside Open themes folder. With a built-in theme
active, Share theme is greyed out, which is correct: it shares only a theme the
user made, and the built-in ones are already on every machine. Import theme
opens the standard Windows file dialog, and cancelling it changes nothing.

In Settings, Alerts, the explanatory lines sit under the idle controls and
beside the alert-sound picker.

About shows 1.34.0.0.

6. UNCHANGED, RESTATED

  - The app synthesizes no input and injects into no process.
  - Sign-in happens inside Roblox's own page in a WebView2 frame; only the
    session cookie is captured, encrypted to the Windows user.
  - No telemetry and no analytics.

Thank you for your time.

626 Labs LLC
