# Notes for certification — reviewer letter (v1.33.0.0)

> Paste the block below the `---` marker — everything from `Hello reviewer,` to `626 Labs LLC` —
> into Partner Center → your app → **Submission options** → **Notes for certification**. The same
> text, paste-ready and already plain, is `reviewer-letter-1.33.0.0.paste.txt`.
> **Partner Center only.** `submit_write.py` never sends `notesForCertification`, so confirm it on
> the Partner Center screen or not at all.
>
> **This file was missing, and the rule that it should not be was already written.** v1.32.1's
> letter was drafted late — only after Este asked for it at the submission screen — and it said why:
> the playbook's phase list never named the reviewer letter. That got fixed afterwards. Phase 2 now
> says "write the reviewer letter in this phase too", and names **both** `reviewer-letter-X.Y.Z.0.md`
> **and** its `.paste.txt`.
>
> This cut still produced only the `.paste.txt`. The letter itself was written on time, in Phase 2,
> with the release notes; it was the annotated `.md` twin that did not get written, and nothing
> failed — the paste file is the one a human needs, so the gap was invisible. What surfaced it was
> the listing console, which reads the letter by its `.md` name from the pinned ref and would have
> reported it missing at Phase 7. **The lesson is narrower than v1.32.1's and worth keeping separate
> from it:** a playbook line that says "X plus its .paste.txt" can be half-satisfied silently,
> because the half that gets used is the half that gets written.
>
> **A single-version delta, and a wide one.** Certification last saw v1.32.1.0. This is the alerts
> cycle: twelve built items, 133 files, +12,670/−1,300.
>
> **The letter's job this time is to be specific about the one addition and plain about the rest.**
> The risk profile is the inverse of v1.32.1's. That release had to prove a negative and persuade a
> reviewer not to go looking for more. This one genuinely adds something plugin-facing —
> `RORORO_LAUNCH_REASON` — and a reviewer who finds it unannounced would be right to slow down. So
> section 2 names it first, states its five values, and says why it is an environment variable rather
> than a command-line argument: a plugin that does not read it is completely unaffected.
>
> **Section 5 is exercisable without a Roblox account** for the sound work, and needs one running
> client for the routing work. That asymmetry is stated in the letter rather than left for the
> reviewer to discover.
>
> **Every claim was verified against `git diff v1.32.1.0..HEAD`, not assumed:**
>
> - 133 files changed, +12,670/−1,300; 92 under `src/`.
> - `Package.appxmanifest`: the only differing line is the `Version` attribute.
> - `plugin_contract.proto`: no diff at all.
> - Input-synthesis and interop grep over added lines: **zero** hits for `SendInput`, `keybd_event`,
>   `mouse_event`, `SetWindowsHookEx`, `PostMessage`, `SendMessage(`, `WriteProcessMemory`,
>   `OpenProcess`, `DllImport`, `LibraryImport`.
> - Seven URL strings in added lines, none of them contacted: four XML namespace declarations and
>   three obviously-fake Discord webhook fixtures in tests. **This one was corrected rather than
>   carried** — the first draft of the letter said "a XAML namespace declaration and fake webhook
>   strings", which undercounted. The SVG namespace and a C2PA attribute inherited from the brand
>   wordmark's own export were also there, and the two SVG files holding them are design sources
>   present in neither the build output nor the package. Checked, not assumed.
>
> Full measured delta and the open items (verifier not run, clean-VM smoke not done, About
> screenshot a release stale) are in `submission-packet-1.33.0.0.md`.

---

Hello reviewer,

RoRoRo is a launcher that runs several Roblox clients side by side, each signed
in as a different account the user saved. This is v1.33.0.0. Certification last
saw v1.32.1.0, so this is one version's delta. It is a larger one than the last
few: a pass over how the app notifies the user.

1. WHAT CHANGED

All of it is about notifications the app already sent, and where they go.

The app can warn the user about things happening to their own accounts: one
dropped out, one is using a lot of memory, one went idle. Settings has a grid
letting the user pick, per warning, whether it appears on the desktop, goes to
a Discord channel they configured, or goes to their phone.

One of those warnings ignored the grid. Memory warnings always raised a desktop
balloon, whatever the user had chosen. That is fixed: every warning now goes
through one place, so the grid, the per-account mute and the repeat limit apply
to all of them equally.

Three smaller changes in the same area:

  - The user chooses how often a warning may repeat, from every time up to once
    an hour. It was fixed at five minutes, which is still the default.
  - The desktop notification is now a window the app draws itself, in the app's
    own theme, rather than a Windows shell balloon. Clicking one that names a
    single account brings the app forward at that account.
  - The user picks what it sounds like — a bundled chime, the Windows default
    sound, or silence — and can press a button to hear it. The chime is a small
    WAV file we generated ourselves; it is not licensed audio.

2. THE ONE PLUGIN-FACING CHANGE, STATED PLAINLY

The app can launch optional plugin programs the user installs and consents to.
Those launches now carry one environment variable, RORORO_LAUNCH_REASON, whose
value is one of: autostart, manual, install, update, restart. It lets a plugin
tell a start the user asked for from one that happened automatically.

It is an environment variable and not a command-line argument deliberately, so
that a plugin which does not read it is completely unaffected. Nothing else
about the plugin interface moved:

  - plugin_contract.proto is byte for byte what v1.32.1.0 shipped. Verified
    against the diff, not assumed.
  - No capability, consent prompt or RPC was added, and the contract version is
    unchanged.

3. NOTHING ELSE MOVED

  - Package.appxmanifest differs from v1.32.1.0 by the Version attribute only.
    Same runFullTrust capability, languages, protocols and startup task.
  - No new network address and no new network call of any kind. Seven URL
    strings appear in added lines and not one is an address the app contacts:
    four are XML namespace declarations (two XAML, the SVG namespace, and a
    C2PA attribute carried in from the brand wordmark's own export), and three
    are obviously-fake Discord webhook fixtures inside unit tests. The two SVG
    files carrying namespace declarations are design sources; they are in the
    repository only and appear in neither the build output nor the package.
  - No new permission, and no change to what data the app stores or where.

4. NO INPUT SYNTHESIS, NO NEW INTEROP

No added line calls SendInput, keybd_event, mouse_event, SetWindowsHookEx,
PostMessage, SendMessage, WriteProcessMemory or OpenProcess, and no DllImport
or LibraryImport was added anywhere in the change. The app still synthesizes no
input and injects into no process.

5. WHAT YOU CAN EXERCISE

No Roblox account is needed for most of it.

Open Settings, then Alerts. The section lists each warning on its own row, with
where it goes and how often beside it. Change "Alert sound" and press "Hear it"
— the sound plays immediately. Setting it to Silent and pressing the button
plays nothing, which is correct.

To see a real notification: save an account and launch it, so there is a
running client. In the same section, lower "Warn when one account passes (MB)"
to a figure below what the client is using. Within about thirty seconds a
themed notification appears at the corner of the screen naming the account.
Untick Desktop for that row and repeat: nothing appears, which is the fix this
release is mostly about.

6. UNCHANGED, RESTATED

  - The app synthesizes no input and injects into no process.
  - Sign-in happens inside Roblox's own page in a WebView2 frame; only the
    session cookie is captured, encrypted to the Windows user.
  - No telemetry and no analytics.

Thank you for your time.

626 Labs LLC
