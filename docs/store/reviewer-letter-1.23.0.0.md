# Notes for certification — reviewer letter (v1.23.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**.
>
> **Routine single-version jump with zero disclosure-surface change** — the lightest letter shape
> we have. No new package capability, no new outbound endpoint, no telemetry, no new plugin RPC or
> capability, no manifest change of any kind. The headline feature is local statistics computed
> from a file the app already writes; the letter's job is to say that precisely and stop.
>
> Sources: `docs/store/release-notes-1.23.0.0.md`,
> `docs/superpowers/specs/2026-08-22-rororo-session-stats-design.md`.

---

```
Hello reviewer,

Thank you for your time on v1.23.0.0. Certification last saw
v1.22.0.0; this is a routine single-version update with no change
to any disclosure surface. No new package capability (still
runFullTrust only, unchanged since v1.4.0.0), no new outbound
endpoint, no telemetry, no new plugin capability or RPC, no
manifest change.

1. THE HEADLINE FEATURE IS LOCAL-ONLY STATISTICS.

   The session History page now shows usage statistics: total
   playtime, most-played game, and per-account login streaks. All
   of it is computed on the user's machine from the launch-history
   file the app has always written locally, and stored beside it
   in a second local file. Nothing is transmitted, to us or to
   anyone - the app still contains no telemetry or analytics of
   any kind, matching the published privacy policy, which is
   unchanged.

2. EVERYTHING ELSE IS BUG FIXES.

   A plugin-initiated client stop now completes reliably; a plugin
   consent dialog no longer clips its own buttons on long
   descriptions; account nicknames now appear in session history;
   a rare startup crash (a thread-affinity race in tray-icon
   construction) is fixed. None of these alter any permission,
   network, or data-handling behaviour.

Network behaviour is unchanged and remains limited to
Roblox-owned endpoints and GitHub Releases (update checks and the
compatibility feed), with the app's own User-Agent. Credential
handling is unchanged: session cookies remain DPAPI-encrypted,
local-only, and are never exposed to plugins.

The trademark position is unchanged from prior certifications:
RoRoRo is an independent tool, not affiliated with Roblox
Corporation, and the disclaimer appears on the Store description,
the About box, and the privacy policy.

Thank you,
626 Labs LLC
```
