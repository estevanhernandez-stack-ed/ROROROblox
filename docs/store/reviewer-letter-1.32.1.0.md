# Notes for certification — reviewer letter (v1.32.1.0)

> Paste the block below the `---` marker — everything from `Hello reviewer,` to `626 Labs LLC` —
> into Partner Center → your app → **Submission options** → **Notes for certification**. The same
> text, paste-ready and already plain, is `reviewer-letter-1.32.1.0.paste.txt`.
> **Partner Center only.** `submit_write.py` never sends `notesForCertification`, so confirm it on
> the Partner Center screen or not at all.
>
> **A single-version delta, and the smallest one yet.** Certification last saw v1.32.0.0. One bug
> fix, no new feature, no new capability.
>
> **The letter's job this time is to be short and to prove a negative.** There is nothing here a
> reviewer needs to assess for policy: the change is *when* an existing setting is read. The risk is
> the opposite of v1.32's — not that something alarming gets misread, but that a reviewer assumes a
> release must contain more than it says and goes looking. So section 2 enumerates what did not move,
> and section 3 states the interop negative explicitly rather than leaving it to be inferred.
>
> **Section 4 is exercisable in about a minute**, which is the point. v1.32's letter had to admit
> that seeing auto-rejoin fire took a 20-minute idle kick. This one can be verified end to end while
> the reviewer watches: untick, readouts clear, tick, they come back within ~30 seconds.
>
> **Every claim was verified against `git diff v1.32.0.0..HEAD`, not assumed:**
>
> - Six files changed, +387/−11, two of them tests.
> - `Package.appxmanifest`: the only differing line is the `Version` attribute.
> - `plugin_contract.proto`: no diff at all.
> - Input-synthesis and interop grep over added lines: zero hits for `SendInput`, `keybd_event`,
>   `mouse_event`, `SetWindowsHookEx`, `PostMessage`, `SendMessage(`, `WriteProcessMemory`,
>   `OpenProcess`, `DllImport`, `LibraryImport`.
> - No added `http`/`https` string anywhere in `src/`.
>
> **Written late, and that is worth recording:** this letter was missed when the rest of the 1.32.1
> paperwork was drafted, and was written only after Este asked for it at the submission screen. The
> release playbook's phase list does not name the reviewer letter — it lives in the per-release
> artifact set instead — which is how it fell through. Worth adding to Phase 2 so the next cut cannot
> repeat it.

---

Hello reviewer,

RoRoRo is a launcher that runs several Roblox clients side by side, each signed
in as a different account the user saved. This is v1.32.1.0. Certification last
saw v1.32.0.0, so this is one version's delta, and a small one: a single bug
fix, no new feature.

1. WHAT CHANGED

One setting now takes effect when the user changes it instead of only at the
next launch.

Settings has a checkbox, "Watch memory while accounts are running", which
controls whether the app samples how much memory each running Roblox client is
using so it can warn before the machine runs out. The app read that checkbox
once, at startup. Unticking it saved the user's choice but did not stop the
sampling already running, so the warnings continued for the rest of that
session with nothing on screen explaining why.

It is now read whenever the user changes it. Unticking stops the sampling
immediately; ticking starts it again. The three numbers under it (how much
memory to keep free, when to warn about one account, how far ahead to warn)
behave the same way.

2. NOTHING ELSE MOVED

  - Package.appxmanifest differs from v1.32.0.0 by the Version attribute only.
    Same runFullTrust capability, languages, protocols and startup task.
  - No change to the plugin interface. plugin_contract.proto is byte for byte
    what v1.32.0.0 shipped; no capability, consent or RPC was added, and the
    contract version is unchanged.
  - No new network address, and no new network call of any kind.
  - No new permission, no change to what data the app stores or where.

3. NO INPUT SYNTHESIS, NO NEW INTEROP

No added line calls SendInput, keybd_event, mouse_event, SetWindowsHookEx,
PostMessage, SendMessage, WriteProcessMemory or OpenProcess, and no DllImport
or LibraryImport was added. Six files changed in total, all of them the app's
own, and two of those are tests. The app still synthesizes no input and injects
into no process.

4. WHAT YOU CAN EXERCISE

Save an account and launch it, so there is a running client to measure. Open
Settings, then "Alerts & memory", and find the Memory section at the bottom.
Untick "Watch memory while accounts are running": the memory readouts beside
the account clear, and no further memory warning appears. Tick it again and the
readouts come back on the next check, within about 30 seconds. No restart is
needed in either direction, which is the whole of this release.

5. UNCHANGED, RESTATED

  - The app synthesizes no input and injects into no process.
  - Sign-in happens inside Roblox's own page in a WebView2 frame; only the
    session cookie is captured, encrypted to the Windows user.
  - No telemetry and no analytics.

Thank you for your time.

626 Labs LLC
