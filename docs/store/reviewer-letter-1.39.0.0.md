# Notes for certification — reviewer letter (v1.39.0.0)

> Paste the block below the `---` marker — everything from `Hello reviewer,` to `support@626labs.dev` —
> into Partner Center. The same text, paste-ready and already plain, is
> `reviewer-letter-1.39.0.0.paste.txt`.
> **Partner Center only.** `submit_write.py` never sends `notesForCertification`, so confirm it on
> the Partner Center screen or not at all.
>
> **Where the box is, and what is in it.** Notes for certification live on a product-level page,
> **Additional Testing Information** (its address ends `/suppinfo/additionaltestinginfo`). The box
> holds the v1.38.0.0 letter. **Select everything in the box and replace it with this letter.** Do
> not paste above or below what is there.
>
> Every "nothing moved" line is measured against `v1.38.0.0` on 2026-11-16, not carried forward: the
> manifest diff (empty before the version stamp), the `.proto` diff (empty), `NativeMethods.txt`
> (unchanged), the interop and input-synthesis grep over added source under `src/ROROROblox.App` and
> `src/ROROROblox.Core` (zero, with `PostMessage` matching once in a doc comment, which the letter
> says), the new-URL grep (zero), the registry grep (zero) and the `.resx` diff (empty). See
> `submission-packet-1.39.0.0.md` for the measurements.
>
> **Two things are new at the boundary and the letter leads its second section with them:** a second
> file that holds cookies (`accounts.dat.bak`), and a subscription to the app's own queued window
> messages. Neither is a new API call, but a reviewer reading the diff would stop at both, so the
> letter says what each is and is not.
>
> The "how to see it" section gives one command, `taskkill /IM ROROROblox.App.exe` without `/F`.
> That was measured against a probe window, and `ROROROblox.App.exe` is the executable the manifest
> names. `taskkill` itself has not been run against a real build of this version. Task Manager's
> End task, which delivers the same posted `WM_CLOSE`, was: the owner's smoke on 2026-11-16 logged
> "An external close request arrived; RoRoRo is quitting." and a clean exit.

---

Hello reviewer,

RoRoRo is a launcher that runs several Roblox clients side by side, each signed
in as a different account the user saved. This is v1.39.0.0. Certification
last saw v1.38.0.0, so this is one version's delta: six reliability fixes and
no new feature.

1. WHAT CHANGED

- The file that holds the user's saved accounts is now written more safely.
  Each save is flushed to disk before it replaces the old file, and the
  previous good copy is kept beside it as accounts.dat.bak. If the main file
  is missing, empty or unreadable at the next read, the app loads the backup
  and repairs the main file from it. Removing an account rewrites the backup
  too. The file is also written less often: a background refresh that changed
  nothing no longer rewrites it.
- A Roblox client that exits at the same moment the app is exiting could
  raise an unhandled exception during shutdown, and the next start then
  showed the "closed unexpectedly" line added in v1.38 after what had been a
  normal quit. The call is now dropped once shutdown has begun.
- When the user launches an account that is already running, the account's
  row now follows the newer client. Before, the older client closing made the
  row read as stopped.
- When an account's Roblox session expires while it is in a game, its row no
  longer stays on "In game".
- A close request from another program now quits the app. Before, the window
  hid to the tray on every close, so "taskkill" without /F left the process
  running. Clicking the X, Alt+F4 and the taskbar's Close still hide the
  window to the tray, as before.
- The tray icon's memory warning now appears only while at least one Roblox
  client is running.

2. WHAT A CODE REVIEW WOULD FIND

One new file on the user's PC: accounts.dat.bak, in the same folder as
accounts.dat. It is a copy of the previous accounts file, so it holds the
same kind of data, Roblox session cookies included, encrypted with Windows
DPAPI for the current user exactly as accounts.dat is. It is never
transmitted. The privacy policy gained one row for it:
https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/PRIVACY.md

One new observation of the app's own window messages. To tell a close
request that came from another program from the user clicking the X, the app
subscribes to WPF's ComponentDispatcher.ThreadFilterMessage on its own UI
thread and notes when a WM_CLOSE addressed to its own main window arrives
through the message queue. This is a managed event on the app's own thread.
It is not a Windows hook (no SetWindowsHookEx), it sees no other process's
messages, and it sends or synthesizes nothing.

No new Windows API call, no new registry access, no new link or URL, and no
new network host.

3. NOTHING ELSE MOVED

Measured against the v1.38.0.0 tag, not assumed:

  - Package.appxmanifest: only the Version attribute changes. Same identity,
    publisher, runFullTrust, languages, protocols and startup task.
  - plugin_contract.proto: byte for byte unchanged. No capability, consent
    prompt or RPC was added.
  - No new network calls. The app contacts no new host.
  - No input synthesis and no new interop. Across every added line of
    shipped source, the count of SendInput, keybd_event, mouse_event,
    SetWindowsHookEx, SendMessage, WriteProcessMemory, OpenProcess,
    DllImport, LibraryImport and RegisterRawInputDevices is zero. The word
    PostMessage appears once, in a comment that describes how another
    program delivers WM_CLOSE; the app does not call it there.
  - No text on screen changed, in any language.
  - No new permission.

The app still launches the official Roblox client unmodified and injects into
nothing. It is not affiliated with, endorsed by, or sponsored by Roblox
Corporation.

4. HOW TO SEE IT

Add an account with "Add account" (Roblox's own sign-in page opens inside the
app). Click the X on the main window: it hides to the tray and the app keeps
running, as before. Then, from a command prompt, run

    taskkill /IM ROROROblox.App.exe

without /F. The app now exits. Before this version it stayed running.

After an account has been added and one more change saved (for example,
typing a note on the account), accounts.dat.bak sits beside accounts.dat in
the app's data folder. For a Store install on a PC that never had the direct
download, that folder is inside the package's LocalCache.

The other fixes need conditions a review session will not have: a client
exiting during shutdown, an expired session, an account launched twice.

Thank you,
626 Labs LLC
support@626labs.dev
