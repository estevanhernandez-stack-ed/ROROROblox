# Notes for certification — reviewer letter (v1.37.0.0)

> Paste the block below the `---` marker — everything from `Hello reviewer,` to `support@626labs.dev` —
> into Partner Center → your app → **Submission options** → **Notes for certification**. The same
> text, paste-ready and already plain, is `reviewer-letter-1.37.0.0.paste.txt`.
> **Partner Center only.** `submit_write.py` never sends `notesForCertification`, so confirm it on
> the Partner Center screen or not at all.
>
> Every "nothing moved" line is measured against `v1.36.0.0` on 2026-11-10, not carried forward: the
> manifest diff (empty before the version stamp), the `.proto` diff (empty), the interop and
> input-synthesis grep over added source (one `DllImport`, for `AssocQueryStringW`, and nothing else),
> the new-URL grep (one, the Store review link) and the registry grep (one read). See
> `submission-packet-1.37.0.0.md` for the measurements.
>
> The letter leads its disclosures with the `DllImport` rather than letting a reviewer find it. Last
> release's letter said the interop count was zero; this one cannot, and says exactly what the one is.
>
> The rating request is described against Store policy 10.1's expectations in plain words: nothing is
> offered for a rating and no feature depends on one.

---

Hello reviewer,

RoRoRo is a launcher that runs several Roblox clients side by side, each signed
in as a different account the user saved. This is v1.37.0.0. Certification
last saw v1.36.0.0, so this is one version's delta, and a small one: two fixes
for new users, three more frame-rate choices, and a one-time request for a
Store rating.

1. WHAT CHANGED

- Before a launch, the app now checks that Windows has a program registered to
  open Roblox's own launch link. If none is, the app shows its existing
  "Roblox needed" dialog with a button to download Roblox, instead of letting
  Windows show its "you'll need a new app to open this link" message. When the
  Microsoft Store edition of Roblox is the one installed, the dialog says so.
- Adding a saved account a second time no longer creates a duplicate row. The
  app refreshes that account's sign-in and tells the user to add a different
  Roblox account.
- The per-account frame-rate cap list gains 75, 100 and 180.
- On a Microsoft Store install only, after a launch has worked on three
  separate days, the app shows one line in its main window the next time it
  starts, with two buttons: "Rate RoRoRo" and "Not now". Either one ends it
  permanently. Nothing is offered in exchange for a rating, no feature depends
  on it, and it never appears during a launch.

2. THE THREE THINGS A CODE REVIEW WOULD FIND, STATED PLAINLY

  - One new Windows API call: AssocQueryString in shlwapi.dll. It asks Windows
    which program is registered for a link type. It is read-only, and it is
    the check described in the first item above.
  - One new registry read: HKEY_CLASSES_ROOT\roblox-player\shell\open\command.
    Read-only, the same check. The app writes nothing to the registry here.
  - One new link the app can open: ms-windows-store://review/?ProductId=9NMJCS390KWB,
    the Microsoft Store's own review page for this product, opened only when
    the user presses "Rate RoRoRo".

The rating request keeps its state in one small file in the app's data folder,
rating-ask.json: up to three dates and the user's answer. It is never sent
anywhere. The privacy policy lists the file and was updated for this release.

3. NOTHING ELSE MOVED

Measured against the v1.36.0.0 tag, not assumed:

  - Package.appxmanifest: only the Version attribute changes. Same identity,
    publisher, runFullTrust, languages, protocols and startup task.
  - plugin_contract.proto: byte for byte unchanged. No capability, consent
    prompt or RPC was added.
  - No new network calls. The app contacts no new host.
  - No input synthesis. Across every added line of source, the count of
    SendInput, keybd_event, mouse_event, SetWindowsHookEx, PostMessage,
    SendMessage, WriteProcessMemory, OpenProcess, LibraryImport and
    RegisterRawInputDevices is zero. DllImport appears once, for the
    AssocQueryString call named above.
  - No new permission.

The app still launches the official Roblox client unmodified and injects into
nothing. It is not affiliated with, endorsed by, or sponsored by Roblox
Corporation.

4. HOW TO SEE IT

Add an account with "Add account" (Roblox's own sign-in page opens inside the
app), then add the same account again: the second sign-in refreshes the first
row and a line under the toolbar says the account is already saved. The
frame-rate list is the drop-down on each account's row. The rating line needs
three separate days of use, so it will not appear during a review session; the
"Roblox needed" dialog appears only on a PC with no Roblox desktop client.

Thank you,
626 Labs LLC
support@626labs.dev
