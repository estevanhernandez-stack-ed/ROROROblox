# Run Brief: update-button

## Ask
"We need to put an update available button on the main part of RoRoRo."

## Source
- **Kind:** one-line ask, planned here; three choices answered by the owner the same minute
- **Location:** this file. The trigger: on 2026-11-17 the owner's own Store install was still 1.38.0.0, a day after 1.39.0.0 published. A running app holds a Store update back, and nothing in RoRoRo says an update exists.

## Intent
A person running RoRoRo can see that a newer version is waiting and get to it in one click, on both the Store build and the direct-download build.

## Done when
- On a Store install with an update waiting, the main window's header shows an "Update available" button, and one click opens RoRoRo's page in the Microsoft Store.
- On a direct-download install with an update downloaded, the same button says what it does and one click quits RoRoRo so the update installs.
- With no update, or when the check fails for any reason, there is no button and nothing else changes.
- The check never delays startup and never throws out of its own code.
- `dotnet build ROROROblox.slnx -c Release` has 0 errors and the full unit and harness suites pass.

## Out of scope
- **Installing a Store update in place** (`RequestDownloadAndInstallStorePackageUpdatesAsync`). The button opens the Store. A follow-up once the check is proven on a live Store build.
- **Restarting RoRoRo after a direct-download update.** The staged update already installs when RoRoRo closes, without restarting it; that stays.
- **A setting to hide the button, a toast, or a tray notice.**
- **Release notes inside the app.**
- **Version stamp, Store submission, tag.**

## Inferred
Each line is something the owner did not say.
- The owner chose: the Store API as the signal for Store installs; the header beside Tools; the same release as the stability fixes. (answered 2026-11-17 12:45)
- The Store button opens `ms-windows-store://pdp/?productid=9NMJCS390KWB` rather than installing in place. (default — the URI launch can be tested now, an in-place install cannot, and there is no evidence here of what the Store does to a running packaged desktop app)
- The direct-download build shows the same button, with its own label, once `UpdateChecker` has staged an update. (default — the ask was a button on the main window, and that build has the same blind spot)
- The button goes at the left end of the header's button group, before Games, so Games, Settings and Tools keep their positions. (default — "beside Tools" was the placement picked from a sketch that had only Tools in it)
- The Store check runs after the window is up and again every six hours, because the case that prompted this is an app left running for days. (default)
- The Store API path cannot be exercised live before release: it needs a Store-installed build. The owner was told and accepted that. Tests use a seam. (from: the answer)
- New strings in six languages through the catalog pipeline, and a Microsoft Store row in `docs/PRIVACY.md`. (from: the keystone)

## Mode
- **Mode:** Attended, autonomous, on `fix/stability-part-two`.
- **Triggered by:** attended by default (12:45 local, two items).
