# Live smoke, 2026-10-11 — where a packaged RoRoRo actually writes its data

**Result: `LocalCache`.** On a machine where `%LOCALAPPDATA%\ROROROblox` does not exist, the
shipped Store build does **not** create it. Its data is redirected into the package's own folder,
which a Store uninstall removes. This settles the question `docs/PRIVACY.md` had been answering
wrongly in both directions since v1.23, and promotes row 3 of that policy's table from "likely" to
measured.

## What was tested, and why this way

The question was only ever: **when the real folder is absent, does a packaged build create it or
get redirected?** Every prior test ran on Neb, where the dev build created
`%LOCALAPPDATA%\ROROROblox` years ago — which is precisely the condition that forces in-place
writing and so could never answer it.

Two approaches were tried.

**A clean Windows user account, which failed and could not have worked.** The plan was a new local
user (virgin AppData) plus the 1.33 sideload MSIX. Windows refused the install six times with
`0x80073CFB`: *"the provided package has the same identity as an already-installed package but the
contents are different."* The sideload and Store packages share one identity —
`626LabsLLC.RoRoRoBlox_1.33.0.0_x64__wz1chhb2h2v4a` — because `dev-cert.pfx` carries the same
publisher CN (`CN=177BCE59-0966-4975-9962-10E36652141F`) as the Store signing identity. Windows'
own remedy is "remove the old package for every user on the system," which is not an option against
a live app holding a live vault. **Noting it so nobody designs this test that way again: the
sideload build cannot be installed anywhere the Store build is installed.** Windows Sandbox was not
an alternative either — both 626 Labs machines run Windows 11 Home, which has no Sandbox.

**Parking the real folder, which worked and used the real Store build.** With RoRoRo closed,
`%LOCALAPPDATA%\ROROROblox` was renamed aside, the Store package launched once, and the result
measured. Better fidelity than the sideload would have given, because it is the actual shipped
package. Script and transcript kept outside the repo under
`%PUBLIC%\RoRoRo-CleanBox-Test\` — `TEST-V2-run-as-estev.ps1` and
`RESULT-V2-2026-10-11-100207.txt`.

## The measurement

| | |
|---|---|
| Package | `626LabsLLC.RoRoRoBlox_1.33.0.0_x64__wz1chhb2h2v4a` (Store build, from `C:\Program Files\WindowsApps`) |
| Machine | Nebuchadnezzar, Windows 11 Home, 10.0.26220 |
| Before | real folder parked aside, 1,189 files; real path confirmed absent; 0 of our files in package `LocalCache` |
| Action | launched once from Start, main window reached, no account added, quit from tray |
| After | real path **still did not exist**; **9** of our files in `LocalCache` |

Everything it wrote went to
`%LOCALAPPDATA%\Packages\626LabsLLC.RoRoRoBlox_wz1chhb2h2v4a\LocalCache\Local\ROROROblox\`:

```
settings.json              session-stats.json
last-known-mutex.txt       last-update-check.txt
known-issues.cache.json    known-issues.cache.json.sig
.welcome-shown             .language-notice-shown
logs\rororoblox-20261011.log
```

Restore verified in the same run: 1,189 files back, `accounts.dat` identical at 13,846 bytes and
`2026-10-09 08:55:24`. Nothing was deleted at any point.

## What this proves, and what it does not

**Proves:** a packaged RoRoRo does not recreate the real AppData folder when it is absent. The
redirect applies to this app, with its manifest, on this OS build. Because the direction of the
result is "redirected," the parked-folder method is conclusive here — a `LocalCache` answer cannot
be an artifact of the folder having previously existed, whereas a `REAL PATH` answer might have
been, which was stated before the run rather than after.

**Does not prove:** that `accounts.dat` specifically redirects. No account was added, so the vault
file was never written. It lives in the same directory as the nine files above and is created by the
same `Environment.GetFolderPath(LocalApplicationData)` resolution, so it follows — but it is an
inference from the folder, not a direct observation of that file.

**Also unmeasured:** whether a genuinely fresh machine behaves identically to a parked folder. The
mechanism says yes and the result is in the predicted direction, but a clean box has not run it.

## Consequences

- **`docs/PRIVACY.md`** — row 3 is now measured. Store-only users' data is inside the package
  folder and a Store uninstall removes it. The August 2026-08-30 correction, which said the real
  folder was used "for every install type," was wrong for exactly those users.
- **No code change.** The app resolves its folder through
  `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` in every site rather
  than a hardcoded literal, so redirection is transparent to it and nothing is broken. Only the
  documentation was wrong.
- **One support-facing wrinkle, not a bug.** The Diagnostics page surfaces a `DataDirectory`
  derived the same way; on a redirected install it can name a path that does not contain the files.
  Worth knowing before telling a user to look there.
- **`CLAUDE.md`'s claim that "Store and dev builds share one data folder"** holds only on a machine
  where the real folder already exists. On a Store-only machine they would not share it at all.
- Cross-app context, including why Sanduhr's 3.4.3 is expected to behave the *opposite* way (it
  requests `unvirtualizedResources` and disables `desktop6:FileSystemWriteVirtualization`, which
  RoRoRo declined): `store-listing-console/docs/msix-capabilities.md`.
