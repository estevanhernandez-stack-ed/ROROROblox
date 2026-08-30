# Notes for certification — reviewer letter (v1.24.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**.
>
> **This submission HAS a manifest delta** — the first since v1.4.0.0 — so the letter leads with
> it and explains each element before the reviewer's diff tool finds it. The delta is the fix:
> two protocol declarations and one startup task move registrations the app has always made
> per-user (HKCU, present in every prior certified build) into the package manifest, because a
> packaged app's registry writes are virtualized and those registrations never took effect on
> Store installs. No new capability, no new endpoint, no telemetry.
>
> Sources: `docs/store/release-notes-1.24.0.0.md`,
> `docs/superpowers/specs/2026-08-30-packaged-activation-design.md`,
> `docs/store/smoke-2026-08-30-packaged-activation.md`.

---

```
Hello reviewer,

Thank you for your time on v1.24.0.0. Certification last saw
v1.23.0.0; this is a single-version update whose headline change
is IN THE MANIFEST, so this letter starts there.

1. THE MANIFEST GAINS THREE EXTENSIONS. ALL THREE FIX EXISTING,
   PREVIOUSLY REVIEWED FEATURES ON PACKAGED INSTALLS.

   The app has always registered, per-user in HKCU at first run:
   a Run entry backing its "start with Windows" Settings toggle,
   and two URI scheme handlers backing its Discord join feature
   (roblox-rororo:, the app's own join link, and
   discord-<application id>:, the scheme the Discord desktop
   client invokes for game joins). Packaged installs virtualize
   HKCU writes, so on the Store build these registrations
   silently never took effect. v1.24 declares them where a
   packaged app should:

   - uap10:Protocol for "roblox-rororo" and
     "discord-1501748116985221272". Join handling is entirely
     inactive unless the user has enabled the Discord features
     in Settings (they default off). A join delivered by URI
     always shows a confirmation dialog before anything
     launches; a join delivered over Discord's own local IPC
     confirms before entering a private server. This behavior is
     unchanged from prior certified versions.

   - desktop:StartupTask, TaskId "RoRoRo", Enabled="false" —
     DISABLED by default. It is enabled only when the user turns
     on the existing "start with Windows" toggle in Settings
     (via Windows.ApplicationModel.StartupTask.
     RequestEnableAsync), appears in Task Manager and
     Settings > Apps > Startup, and the user's choice there
     always wins: if Windows reports the task user-disabled, the
     app's toggle does not re-enable it and instead directs the
     user to Windows Settings.

   No new package capability: the Capabilities element is
   unchanged — runFullTrust only, as in every version since
   v1.4.0.0. No new outbound endpoint, no telemetry, no new
   plugin capability or RPC.

2. THE APP NOW COMPILES AGAINST THE WINDOWS 10.0.19041 API
   SURFACE (for the StartupTask API above). The manifest's
   MinVersion is unchanged at 10.0.19045.0, so the supported OS
   floor does not move.

3. ONE ACCURACY BUG FIX IN THE LOCAL STATISTICS FEATURE
   (introduced v1.23): session end-times are now recorded only
   when the app's two signals agree the session ended, and a
   launch that never reached a game records no play time. As
   before, all statistics are computed locally from local files;
   nothing is transmitted.

4. THE PRIVACY POLICY TEXT WAS CORRECTED (same URL). It now
   states explicitly that uninstalling the app leaves its local
   data folder in place, and names the folder so users can
   remove it manually. This is a documentation correction — data
   handling did not change, and no new data is collected. The
   app still contains no telemetry or analytics of any kind.

Network behaviour is unchanged and remains limited to
Roblox-owned endpoints and GitHub Releases (update checks and
the signed compatibility feed), with the app's own User-Agent.
Credential handling is unchanged: session cookies remain
DPAPI-encrypted, local-only, and are never exposed to plugins.

The trademark position is unchanged from prior certifications:
RoRoRo is an independent tool, not affiliated with Roblox
Corporation, and the disclaimer appears on the Store
description, the About box, and the privacy policy.

Thank you,
626 Labs LLC
```
