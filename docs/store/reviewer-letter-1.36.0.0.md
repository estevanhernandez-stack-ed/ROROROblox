# Notes for certification — reviewer letter (v1.36.0.0)

> Paste the block below the `---` marker — everything from `Hello reviewer,` to `support@626labs.dev` —
> into Partner Center → your app → **Submission options** → **Notes for certification**. The same
> text, paste-ready and already plain, is `reviewer-letter-1.36.0.0.paste.txt`.
> **Partner Center only.** `submit_write.py` never sends `notesForCertification`, so confirm it on
> the Partner Center screen or not at all.
>
> Every "nothing moved" line is measured against `v1.35.0.0`, not carried forward: the manifest diff
> (the Version attribute only), the `.proto` diff (empty), the interop and input-synthesis grep over
> added source (zero), and the new-URL grep (the three facts hosts and nothing else that is
> contacted). See `submission-packet-1.36.0.0.md` for the measurements.
>
> The cadence paragraph was corrected against `AccountFactsService` before it was written here: an
> early draft said "at most every ten minutes" and "at startup"; the code fetches nothing at startup,
> refreshes each account about every ten minutes in the background, and refreshes sooner (never
> within 60 s) on a panel open or a launch end.

---

Hello reviewer,

RoRoRo is a launcher that runs several Roblox clients side by side, each signed
in as a different account the user saved. This is v1.36.0.0. Certification
last saw v1.35.0.0, so this is one version's delta. It is a large one: the main
window becomes an account manager, and the games page becomes a Library.

1. WHAT CHANGED

- Each saved account now shows a few facts under its name: its Robux balance,
  whether it has Roblox Plus, how many friends are online, and a note the user
  writes. Opening an account shows the games it played recently, its favourite
  games and its saved private servers, each one click to play.
- When an account needs attention, a badge says why: its session expired, it
  needs verification, or Roblox has limited it. One click starts the fix the
  app already offered (sign in again, or open Roblox for that account).
- The "Games" page is renamed "Library": every game the user played, favourited
  or saved, in a grid or a list, with search, filters, a box to paste a game
  link, and "Play as" to choose which saved account plays.
- The first-run tour now speaks to someone with one account first.
- The idle clock credits activity to the window that received it, and
  background Roblox processes that never opened a window are closed after two
  minutes.

2. THE NEW NETWORK CALLS, STATED PLAINLY

The account facts are read from Roblox, for each saved account, with that
account's own session cookie, the same calls the Roblox website makes for a
signed-in user:

  - economy.roblox.com: the account's own Robux balance.
  - premiumfeatures.roblox.com: whether the account has Roblox Plus.
  - friends.roblox.com: the number of the account's friends who are online
    (the app already used friends.roblox.com for the friends list).

Nothing is read at startup. While the app runs, a background timer refreshes
one account's facts at a time, each about every ten minutes; opening an
account's panel or finishing a launch can refresh it sooner, but never more
than once a minute per account. These calls are paced so Roblox never sees two
of them in the same second. The results are shown in the app and
cached only inside the existing DPAPI-encrypted
account file. Nothing is sent anywhere else. The privacy
policy names all three hosts and was updated for this release.

3. NOTHING ELSE MOVED

Measured against the v1.35.0.0 tag, not assumed:

  - Package.appxmanifest: only the Version attribute changes. Same identity,
    publisher, runFullTrust, languages, protocols and startup task.
  - plugin_contract.proto: byte for byte unchanged. No capability, consent
    prompt or RPC was added.
  - No input synthesis and no new interop. Across every added line of source,
    the count of SendInput, keybd_event, mouse_event, SetWindowsHookEx,
    PostMessage, SendMessage, WriteProcessMemory, OpenProcess, DllImport,
    LibraryImport and RegisterRawInputDevices is zero. The idle-clock fix
    samples the foreground window more often; it installs no keyboard or
    mouse hook.
  - No new permission.

The app still launches the official Roblox client unmodified and injects into
nothing. It is not affiliated with, endorsed by, or sponsored by Roblox
Corporation.

4. HOW TO SEE IT

Add an account with "Add account" (Roblox's own sign-in page opens inside the
app). The facts appear under the account within a minute or
two, or shortly after its panel is opened. Click the small
triangle before the account's name to open its panel. Open "Library" from the
toolbar.

Thank you,
626 Labs LLC
support@626labs.dev
