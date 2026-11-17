# Run Brief: data-folder-override

## Ask
"do it", in answer to: "The data-folder override ... The recommended set is: dev builds only, set by an environment variable, with a visible marker in the window title and log. Say 'go with the recommended' and I'll build it."

## Source
- **Kind:** one-line ask, planned here
- **Location:** `docs/cycles/2026-11-16-quality-and-stability/board.md`, "New rows the build turned up": a dev build cannot be pointed at another data folder.

## Intent
A dev build of RoRoRo can be run against an empty test folder, so unreleased code gets a live run before it reaches the owner, and never touches his real accounts file. v1.39 reached his smoke with no live run behind it because this did not exist.

## Done when
- With no override set, every data path RoRoRo uses is exactly what it was, and a test says so for each one.
- A dev build started with `RORORO_DATA_DIR=<folder>` keeps all of its own data in that folder and none in `%LOCALAPPDATA%\ROROROblox`.
- A packaged build and a Velopack-installed build ignore the variable.
- A run under the override says so in the log and in the window title, and leaves no trace in the registry.
- `dotnet build ROROROblox.slnx -c Release` has 0 errors and the full unit and harness suites pass.

## Out of scope
- **Roblox's own folders.** Ten sites resolve `%LOCALAPPDATA%\Roblox\...` or `...\Packages` (the client's settings, versions and storage). They are Roblox's, and they do not move.
- **Running two RoRoRos at once.** The single-instance guard, the plugin pipe and the Roblox singleton are machine-wide by nature; the override does not rename them. The owner's RoRoRo still has to be closed for a dev run.
- **A user-facing feature.** Shipped builds ignore the variable, so `docs/PRIVACY.md`, the listing and the reviewer letter do not change.
- **Changing how a Store-only install redirects its folder.** That is Windows, not this.

## Inferred
Each line is something the owner did not say.
- "The recommended" is the three choices offered: dev builds only, an environment variable, a visible marker. (from: the message he answered)
- The variable is `RORORO_DATA_DIR`. (from: the name floated in that message)
- A dev build is one that is neither packaged nor laid out by Velopack (no `Update.exe` one level above the app folder). (from: `Distribution.Win32DistributionMode.IsPackaged` and `Program.IsPortableInstall`, which already read those two facts)
- The value must be an absolute path; anything else is ignored, and the log says it was ignored. (default — a relative path would depend on the working directory, which is how a test profile ends up somewhere nobody expects)
- Under the override RoRoRo neither writes nor removes its machine-wide registrations: the join link scheme under `HKCU\Software\Classes`, and the run-on-login value. (from: reading `App.xaml.cs`; a test profile's default settings would otherwise be synced over the owner's real ones)
- The 23 RoRoRo sites move behind one helper, and a source fence with an exact allow-list keeps a 24th from appearing. (from: a grep on 2026-11-16: 33 sites resolve `LocalApplicationData`, 23 for RoRoRo's folder and 10 for Roblox's)
- This rides in the next release with the exit fix (PR #293), not on its own. (default — nothing here is visible to a user)

## Mode
- **Mode:** Attended, autonomous, one commit per item on `feat/data-folder-override`.
- **Triggered by:** attended by default (16:17 local, five items).
