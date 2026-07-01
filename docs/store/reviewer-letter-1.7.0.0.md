# Notes for certification — reviewer letter (v1.7.0.0)

> Paste the block between the `---` markers into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> Kept deliberately short so it fits the field without truncating: the three disclosure-surface changes (the one new outbound endpoint, the stop-all process termination, the two new plugin RPCs) lead and are stated in full; everything else is compressed to a line. The v1.4.0.0 plugin-system policy 10.2.2 framing still applies and is not re-litigated.

---

```
Hello reviewer,

Thanks for your time on v1.7.0.0. Three changes touch the disclosure
surface; they lead and are stated in full. No new MSIX capability (still
runFullTrust only), no telemetry, no new at-rest data.

1. ONE NEW OUTBOUND ENDPOINT.
   To detect a pending Roblox client update before launching, RoRoRo
   adds exactly one outbound call:
       GET https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer
   - Documented, public Roblox endpoint. RoRoRo compares the installed
     RobloxPlayerBeta.exe FileVersion to the version it reports; that is
     the call's entire purpose.
   - User-Agent is ROROROblox/<version> — no browser spoofing, ever.
   - Degrade-safe: any failure (non-200, parse, timeout) returns "no
     update pending" and never blocks a launch.
   - It is the ONLY new endpoint, with no telemetry attached.
   It drives one new behavior: when an update is pending mid-launch,
   RoRoRo updates the first client (showing a "Roblox is updating" hold),
   then launches the rest into the updated client. RoRoRo does not take
   over the roblox-player: handler and does not manage Roblox versions.

2. "STOP ALL ROBLOX INSTANCES" — PROCESS TERMINATION.
   A user-initiated, confirm-gated tray action force-closes the current
   user's running RobloxPlayerBeta.exe processes. No elevation; same-user
   only. Termination ONLY — no injection, no memory access, no macros, no
   modification of the Roblox client. (The update detector above also
   does a read-only RobloxPlayerInstaller.exe presence scan — nothing
   more.)

3. TWO NEW CAPABILITY-GATED PLUGIN RPCs (contract 0.2.0).
   The out-of-process plugin system gains RequestLaunchTarget (launch an
   account into a link/friend the plugin provides) and GetCurrentServer
   (read the most-recent private-server link to share). Same model as
   v1.4: each is a separately-listed capability the user grants on the
   consent sheet; plugins run out-of-process; install is SHA-verified;
   the host gates every RPC and owns all cookie/launch handling, so a
   plugin never sees a .ROBLOSECURITY cookie. Policy 10.2.2 framing from
   the v1.4.0.0 letter still applies.

NO OTHER NEW DISCLOSURE SURFACE. The singleton mutex NAME is now read
from roblox-compat.json — remote DATA (a validated string passed to
CreateMutex, not code; 10.2.2-clean; that fetch already existed for the
version-drift banner; degrade-safe to a hardcoded default). A tray
"reload" recovery affordance and a startup-crash fix add no new network,
capability, or data.

UNCHANGED FROM v1.6.0.0. Only runFullTrust is declared (no
broadFileSystemAccess, no internetClient). No telemetry. The DPAPI
account vault, the v1.6 passphrase export/import, the auth-ticket flow,
presence reads, and the Authenticode signing identity are all unchanged.
The Package.appxmanifest delta is one attribute: Identity Version
1.6.0.0 -> 1.7.0.0.

Full design detail is in the open-source repo (MIT):
  docs/superpowers/specs/2026-05-21-rororo-install-deferral-design.md
  docs/superpowers/specs/2026-05-21-plugin-private-server-contract-bump-design.md

"Roblox" and the Roblox logo are trademarks of Roblox Corporation.
RoRoRo is an independent third-party tool, not affiliated with, endorsed
by, or sponsored by Roblox Corporation.

Privacy policy: https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/
Source (MIT):   https://github.com/estevanhernandez-stack-ed/ROROROblox

Happy to clarify anything — same-day response.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v1.7.0.0-specific)

- [ ] Version in `Package.appxmanifest` is `1.7.0.0`
- [ ] Version in `ROROROblox.App.csproj` is `1.7.0.0`
- [ ] Manifest delta from v1.6.0.0 is **only** the Identity Version attribute (diff before submit)
- [ ] `dist/RORORO-Store.msix` is built off the `v1.7.0.0` tag
- [ ] Reviewer letter (this file's `---` block) pasted into Partner Center → Submission options → Notes for certification
- [ ] App still declares ONLY `runFullTrust` — no `broadFileSystemAccess`, no `internetClient` (the new CDN check is outgoing HTTPS from a full-trust app; needs no declaration)
- [ ] The clientsettingscdn client-version GET uses the `ROROROblox/<version>` User-Agent — **no** `Mozilla/5.0`, no Edge spoofing
- [ ] The clientsettingscdn check is degrade-safe — any failure returns "no update pending" and never blocks a launch
- [ ] "Stop all Roblox instances" is confirm-gated, same-user only, no elevation; `count==0` is a quiet no-op
- [ ] No injection / memory tampering / macros / Roblox-client modification anywhere (process scan is read-only; stop-all is terminate-only)
- [ ] Plugin contract NuGet is `0.2.0`; handshake string is still `"1.0"`; the two new RPCs are capability-gated and listed separately on the consent sheet
- [ ] `roblox-compat.json` mutex-name read is degrade-safe (falls back to the hardcoded default); the published config carries the mutex name
- [ ] Privacy policy live copy is current (no new data surface this release; the v1.6 account-export disclosure remains accurate)
- [ ] `dotnet test ROROROblox.slnx` passes (unit + integration harness; full-solution CI gate green)

## Source

This file is the v1.7.0.0 reviewer letter. Predecessors:

- v1.6.0.0: [`reviewer-letter-1.6.0.0.md`](reviewer-letter-1.6.0.0.md) (account export/import disclosure)
- v1.5.0.0: [`reviewer-letter-1.5.0.0.md`](reviewer-letter-1.5.0.0.md) (presence disclosure + tags)
- v1.4.3.0: [`reviewer-letter-1.4.3.0.md`](reviewer-letter-1.4.3.0.md) (plugin lifecycle + manifest flags)
- v1.4.0.0: [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (plugin-system policy 10.2.2 — still load-bearing)
