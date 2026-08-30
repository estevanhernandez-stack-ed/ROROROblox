# RoRoRo v1.24.0.0 — release notes

The release where the Store version stops quietly dropping two features. "Start RoRoRo when
Windows starts" and Discord joins both worked on direct-download installs and silently did
nothing on Store and sideload installs — not broken loudly, just ignored. Windows redirects a
Store app's registry writes into a private copy nobody reads, so the toggle flipped a switch
that wasn't wired to anything, and then read its own dead switch back and told you it was on.

If you only read one line: **on the Store version, "start with Windows" and Discord joins now
actually work — and Windows itself shows RoRoRo in Settings > Apps > Startup, where you stay in
control.**

## Short list, for the GitHub release and the Discord post

```
• Store and sideload installs: the "Start RoRoRo when Windows starts" toggle works now. It never did on those installs — the switch flipped, Windows never heard about it. RoRoRo now registers the honest way, through the app package itself, and Windows lists it under Settings > Apps > Startup so you can override it there any time.
• Store and sideload installs: clicking Join in Discord now starts RoRoRo even when it's closed. Same root cause, same fix — the join link types are declared in the app package now. Direct-download installs always had both features; nothing changes for them.
• Your hours got more honest. A launch that never made it into a game no longer counts as a tiny session, and a session no longer ends early just because Roblox restarted itself under the hood. Streaks and totals use the corrected accounting from this version on; rows written before it keep their old stamps.
• The privacy policy now says plainly: uninstalling RoRoRo — Store, sideload, or direct download — leaves your data folder at %LOCALAPPDATA%\ROROROblox in place. Delete that folder yourself for a full clean. The app's behavior didn't change; the policy was wrong about the Store version, and now it isn't.
```

---

## Longer form

### The Store version catches up

Windows treats a Store-installed app's registry writes specially: they land in a private
per-package copy, invisible to the rest of the system. RoRoRo registered its run-on-login entry
and its join links there since the first Store build, and nothing ever read them. We caught it
live — a Store build flipped the toggle, reported it on, and Windows had never heard of it.

The fix is to stop using the registry on packaged installs and declare both things where a
packaged app is supposed to: in the app package manifest. Run-on-login is now a real Windows
startup task — off by default, turned on only by your toggle, and visible in Task Manager and
Settings > Apps > Startup, where your choice always wins. If you turn it off there, RoRoRo's
toggle won't fight you; it will tell you where to turn it back on.

### Discord joins reach a closed RoRoRo

A Join click in Discord launches RoRoRo by its join link. On Store installs that link resolved
to nothing, so joins only worked if RoRoRo already happened to be running. Both join link types
are declared in the app package now, so a cold Join click starts RoRoRo and the join goes
through. The confirmations are the same as they've always been: a join link always asks first,
and a join arriving through Discord itself asks before entering a private server.

### Your stats got more honest

Two accounting bugs, both old, both fixed:

- A launch that never actually connected to a game — a privacy wall, a bad day, a client that
  died on the way up — used to record a 30-to-120-second "session." It now records the launch
  with no play time at all, labeled for what it was.
- Roblox sometimes restarts its own client process mid-session. The old accounting ended your
  session at the moment the first process died, even though you were still playing. A session
  now ends only when RoRoRo's two signals agree you actually stopped — the same both-signals
  rule the row status has used since v1.5.

Rows written by older versions keep the stamps they were written with; the corrected accounting
applies from this version on.

### Compatibility

Nothing breaks. Saved accounts, themes, plugins, history, and settings carry over. No new
permissions in the Windows-capability sense — the package still declares exactly what it always
has. What's new in the package is the startup task (off by default) and the two join link
declarations, which are the fix, not a new power. Minimum Windows version is unchanged.

### Known limits going into the next update

- On Store and sideload installs, Windows owns the run-on-login state. If you disable RoRoRo in
  Task Manager's Startup apps, the in-app toggle can't re-enable it — it will point you at
  Settings > Apps > Startup instead. That's Windows policy, and it's the right call.
- Sessions recorded by versions before this one keep their old end stamps; the stats page's
  "didn't record an end" note covers them honestly.

### Other channels

- **Microsoft Store:** [RoRoRo on the Store](https://apps.microsoft.com/detail/9NMJCS390KWB) —
  this version is submitted for certification; the Store updates you automatically once it
  clears.
- **Sideload MSIX:** attached to this release with `dev-cert.cer` — import the cert to Local
  Machine > Trusted People first, same as before. No cert rotation this release.

### Issues, ideas

Something broken, something missing:
[github.com/estevanhernandez-stack-ed/ROROROblox/issues](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues).
Privacy policy:
[estevanhernandez-stack-ed.github.io/ROROROblox/privacy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)
— corrected this release; worth the two-minute read if you ever plan to fully remove the app.
