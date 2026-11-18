# Notes for certification — reviewer letter (v1.40.0.0)

> **SUBMITTED 2026-11-17, CERTIFIED AND PUBLISHED 2026-11-18.** The owner pasted this letter into
> Partner Center before the 14:43 CST commit, and certification passed with no report. Drafted
> 2026-11-17 on `release/1.40.0.0`; this banner said "PREPARED, NOT SHIPPED" until then.
>
> Paste the block below the `---` marker, from the greeting to `support@626labs.dev`, into Partner
> Center. The same text, paste-ready and already plain, is `reviewer-letter-1.40.0.0.paste.txt`.
> **Partner Center only.** `submit_write.py` never sends `notesForCertification`, so confirm it on
> the Partner Center screen or not at all.
>
> **Where the box is, and what is in it.** Notes for certification live on a product-level page,
> **Additional Testing Information** (its address ends `/suppinfo/additionaltestinginfo`). The box
> holds the v1.39.0.0 letter. **Select everything in the box and replace it with this letter.** Do
> not paste above or below what is there.
>
> **Length.** 4,983 characters. The first draft was 8,175, against v1.39's 4,681 and v1.29's 5,781,
> the longest Partner Center has taken from us. It was cut to stay inside what is proven to fit. Cut:
> the direct-download button (it does not ship through the Store), the plugin stop fix, the
> development-build data folder, the registry and native-method detail, and the long tests-only list.
> Kept in full: the Store call, the URI, the window-message change and the two set-aside file names.
>
> **Every "nothing moved" line is measured against `v1.39.0.0` on 2026-11-17, not carried forward.**
> At `f1d0eeb`: `git diff --stat v1.39.0.0..HEAD -- src/ROROROblox.App/Package.appxmanifest` is empty
> (the version stamp will add the Version attribute and nothing else); `git diff --stat
> v1.39.0.0..HEAD -- '*.proto'` is empty and both `NativeMethods.txt` files are unchanged; added
> lines under `src/ROROROblox.App` and `src/ROROROblox.Core` matched against `SendInput`,
> `keybd_event`, `mouse_event`, `SetWindowsHookEx`, `SendMessage`, `PostMessage`,
> `WriteProcessMemory`, `OpenProcess`, `DllImport`, `LibraryImport`: zero; one added URI
> (`ms-windows-store://pdp/?productid=9NMJCS390KWB`) and no new http(s) host; no new registry key or
> value. See `submission-packet-1.40.0.0.md` for the commands.
>
> **One existing call the letter names on purpose.** `RobloxProcessTracker.RequestClose` posts
> `SC_CLOSE` to a Roblox window the app itself launched, for Stop and Recycle. It is in v1.39.0.0 too;
> this release edits only that method's `catch`. A reviewer grepping the tree would find it, so the
> letter says what it is.
>
> **What the letter does not claim.** It does not say the Store call works. It says the call was not
> run on a Store-installed build, is tested through a seam, and that a failure shows no button. It
> says `taskkill` without `/F`, not Task Manager: this cycle's runs used `taskkill` only.
>
> **Three things are new at the boundary and the second section leads with them:** the Store query
> (the one new network call), the app reading `WM_CLOSE` for any of its own windows through a managed
> WPF event, and two file names that hold cookies after Start Fresh. A reviewer reading the diff would
> stop at each.

---

Hello reviewer,

RoRoRo is a launcher that runs several Roblox clients side by side, each signed
in as a different account the user saved. This is v1.40.0.0. Certification
last saw v1.39.0.0, so this is one version's delta: one new feature, an
"Update available" button, which adds one network call on Store installs, plus
reliability fixes.

1. WHAT CHANGED

- NEW: an "Update available" button in the main window's header, shown only
  while an update is waiting. Clicking it opens this app's page in the
  Microsoft Store app. The app downloads and installs nothing.
- A close request from another program (taskkill without /F) now also quits
  the app while a startup dialog or the first-run tour is on screen. The X,
  Alt+F4 and the taskbar's Close still hide the window to the tray.
- Quitting no longer waits out a 2-second cleanup timeout.
- A data file with the wrong shape is read around instead of stopping startup.
  An accounts file with no valid account list goes to its backup, never to an
  empty list.
- On the "saved accounts can't be unlocked" dialog, Start Fresh reads the
  accounts again first and sets files aside only if they still cannot be read.
  It never deletes them.
- An account whose Roblox session expired raises the existing "dropped out"
  alert when its client exits.
- A launch that fails before the client starts shows on the account's row.
- The About page's shortcut list and on-screen dates follow the UI language.

2. WHAT A CODE REVIEW WOULD FIND

One new network call, on Store installs only. The app calls
Windows.Services.Store.StoreContext.GetDefault()
.GetAppAndOptionalStorePackageUpdatesAsync() and reads only how many updates
come back: once after startup, then every six hours, with a 30-second timeout.
Windows' Store service makes the query for the installed package; the app
sends nothing of its own. No Request... method is called, so there is no Store
UI and nothing is downloaded or installed. Any failure reads as "no update".
We have NOT run this call on a Store-installed build: it needs one. It is
tested through a seam and unproven against the live service. If it fails, no
button appears and nothing else changes. PRIVACY.md has a row for it:
https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/PRIVACY.md

One new URI, not a web address: ms-windows-store://pdp/?productid=9NMJCS390KWB,
this app's own Store ID, handed to the Windows shell by the button.

The app now reads WM_CLOSE for any of its own windows, not only the main one,
through WPF's ComponentDispatcher.ThreadFilterMessage on its own UI thread. It
is a managed event, not a Windows hook. It sees no other process's messages
and sends or synthesizes nothing.

Two new file names can appear in the data folder, only after the user presses
Start Fresh: accounts.dat.corrupt-<time> and accounts.dat.bak.corrupt-<time>.
They are the renamed unreadable accounts file and its backup, holding the same
data (Roblox session cookies included), encrypted with Windows DPAPI for the
current user as accounts.dat is. They are never transmitted, never read again
and never deleted by the app. PRIVACY.md lists both.

3. NOTHING ELSE MOVED

Measured against the v1.39.0.0 tag on 2026-11-17, not assumed:

  - Package.appxmanifest: only the Version attribute. Same identity, publisher,
    runFullTrust, languages, protocols and startup task. No new capability.
  - plugin_contract.proto: unchanged. No capability, consent prompt or RPC.
  - Network: the one Store query above. No new host.
  - No input synthesis and no new native import. In every added line of shipped
    source the count of SendInput, keybd_event, mouse_event, SetWindowsHookEx,
    SendMessage, PostMessage, WriteProcessMemory, OpenProcess, DllImport and
    LibraryImport is zero. The one existing PostMessage (Stop asks a Roblox
    window the app launched to close) is edited only to catch an exception.
  - Registry: no new key or value.
  - Text: 17 new strings in English and the six translated languages.

The app still launches the official Roblox client unmodified and injects into
nothing. It is not affiliated with, endorsed by, or sponsored by Roblox
Corporation.

4. HOW TO SEE IT

The update button needs an installed version older than the one the Store
serves, so it cannot be shown on the version under review. No button is the
expected state.

Close during startup: on a fresh profile, start the app; the first-run tour
opens over the main window. Run "taskkill /IM ROROROblox.App.exe" without /F.
The app exits within about a second. In v1.39.0.0 it kept running.

Run live on development builds, 2026-11-17: the close request during the
leftover-clients dialog and the first-run tour, wrong-shape data files at
startup, Start Fresh on an unreadable accounts file, and quit timing. Tests
only: the Store query's failure handling, the expired-account alert, the
failed-launch row, and the translated shortcut list.

Thank you,
626 Labs LLC
support@626labs.dev
