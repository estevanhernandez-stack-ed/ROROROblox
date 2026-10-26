# Run Brief: 246-crash-survival

## Ask
"Yeah, we need a takeover for that broken session." Accepting the offer: build the #246 fix (handled DWM exception, last-exit reason, restart registration, with tests) and fold in the presence log change, one PR.

## Source
- **Kind:** issue + field-log diagnosis
- **Location:** GitHub #246; MacBook logs at `%USERPROFILE%\LabShare\rororoorlogs` (5 MB, never read whole: grep only); dashboard decisions TOU8800leR8ezJoKecBo, jmJJRVXYtxz5vSOIzZVN

## Intent
A momentary loss of desktop composition no longer kills RoRoRo and every account it holds. When RoRoRo does die, it comes back by itself and the next session can say why the last one ended. A day of logs stops being 1.7 MB of presence noise.

## Done when
- `0x80263001` thrown from `WindowChromeWorker` is logged at Warning and survives; every other dispatcher exception still crashes.
- The next session logs the previous exit reason at startup and `HostInfo` reports it (contract 0.14.0, additive).
- RoRoRo registers for restart at startup (crash and servicing, not reboot).
- Presence logs a line only when an account's state changes.
- `dotnet build` + full `dotnet test ROROROblox.slnx -c Release` green; `docs/decisions.md` mirrors the decisions.

## Out of scope
- #245 reconnect tracking, #247 server join: separate issues.
- Publishing NuGet 0.14.0, tagging, the Store build: release-time, Este's call.
- Finding what flips composition on the MacBook: needs the machine.

## Inferred
- Root cause is the crash, not Store servicing: both relaunches ran v1.33.0 (from: log lines 4262, 10026 of rororoblox-20261024.log).
- Narrow match (HResult AND a `WindowChromeWorker` frame), not the HResult alone, to keep "crash loud" for our own code (from: App.xaml.cs:3508 comment, Este's crash-loud rule).
- `RESTART_NO_REBOOT`: run-on-login is opt-in and off on the farm machine, so a reboot must not start the app (from: memory project_rororo_autostart_login_race).
- Last exit kept as a small file in the data folder, written synchronously from the crash handler (from: default; nothing in the app persists exit state today).
- HostInfo grows three fields; wire `contract_version` stays "1.0", NuGet 0.13.0 → 0.14.0 (from: the 0.13.0 commit 6c34905 pattern).

## Mode
- **Mode:** Attended
- **Triggered by:** attended by default (19:24 local, 4 items)
