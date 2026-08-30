# What's new in this version — v1.24.0.0

> Paste the fenced block into Partner Center → your app → **Store listings** → *What's new in this
> version*.
>
> **Written for someone on the v1.23 Store install** — the audience for whom both headline items
> were silently broken. Direct-download users always had them; this field's readers never did.

---

```
"Start with Windows" works now
• The Settings toggle "Start RoRoRo when Windows starts" now works
  on the Store version. It used to flip without doing anything —
  fixed. Turn it on and Windows lists RoRoRo under Settings > Apps
  > Startup, where you stay in control and can override it any
  time. It stays off unless you turn it on.

Discord joins reach a closed RoRoRo
• Clicking Join in Discord now works even when RoRoRo isn't
  running — it starts itself and takes the join. Private-server
  joins ask you first, same as always. Before, joins only
  worked if RoRoRo was already open.

Your hours got more honest
• A launch that never made it into a game no longer counts as a
  tiny session, and a session no longer ends early when Roblox
  restarts its own client mid-game. Streaks and total hours use
  the corrected accounting from this version on.

Privacy policy corrected
• Uninstalling RoRoRo leaves your data folder in place — the
  policy now says so plainly and names the folder, so a full
  clean is one delete away. App behavior didn't change; the
  wording was wrong and now it isn't.
```

---

## What is deliberately not in this copy

- **The mechanism (registry virtualization, manifest protocols, StartupTask).** The reviewer
  letter carries the precise version; this field's reader needs the outcome, not the plumbing.
- **"Both-signals rule," bootstrapper respawn, presence-confirmed ends.** "Roblox restarts its
  own client" is the same fact in this reader's language.
- **That the fix ships as a manifest change.** Store customers don't read manifests; reviewers
  do, and theirs is section 2 of the packet.
