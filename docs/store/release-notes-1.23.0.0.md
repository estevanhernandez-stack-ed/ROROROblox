# RoRoRo v1.23.0.0 — release notes

The release where RoRoRo starts keeping score. The History page now opens with your numbers —
the most alts you've ever run at once, total hours across every account, a leaderboard of your
alts, and login streaks that only count when an account actually lands in a game. All of it lives
on your machine and nowhere else.

If you only read one line: **RoRoRo now shows you your stats — peak alts at once, total hours,
per-alt streaks — and stopping a client through the Claude connector actually stops it.**

## Short list, for the GitHub release and the Discord post

```
• The History page now opens with your stats: the most alts you've ever had running at once, total uptime across every account, your most-played game, your longest single session, and a day streak. Under them, a leaderboard of your alts by hours played. Everything is computed on your PC from the launch history RoRoRo already kept — nothing is sent anywhere, same as always.
• Every alt has its own login streak, and it only counts days the account actually landed in a game. An alt that launched but got stuck at the Roblox home screen didn't log in anywhere, and the streak knows the difference. Streaks start counting from this version — they can't be reconstructed from the past.
• Stopping a client through the Claude connector works now. It used to say "stop issued" and the client kept running; it now asks the client to close the same way the Stop button does, waits, asks again, and only force-closes if Roblox never answers. A clean close means Roblox saves its own settings on the way out.
• Renaming an alt now shows up in History. New history rows use your local rename instead of the account's old Roblox name. Rows written before this version keep the name they were written with.
• Installing a plugin with a long description no longer pushes the Install and Cancel buttons off the bottom of the consent window.
• Fixed a rare crash where the app could fail to start if two parts of startup raced each other on a slow morning.
```

---

## Longer form

### Your numbers, on the History page

Open History and the top of the page is now yours: **peak alts at once** (the most clients you've
ever had running simultaneously — the number this app exists for), **total uptime** across every
account, your **most-played game** ranked by hours rather than launches, your **longest single
session**, and a **day streak**. Below them, a leaderboard of all your alts by hours played.

It starts full, not empty — RoRoRo seeds the totals from the launch history it already kept, so
day one shows your last few weeks. Two numbers are exceptions, honestly labeled: peak-alts and
the per-alt streaks say "since history began," because neither can be reconstructed from old
rows — the moment three alts overlapped is gone once its rows are, and an old row records where
a launch was aimed, not whether it landed.

The page is honest about its edges. Sessions that never recorded an end (a crash, a client still
running) are counted and excluded from uptime, and the page says so in small text rather than
quietly guessing. Clearing your history clears the stats with it — the two always agree.

Everything is computed on your machine from files already on your machine. No telemetry, no
analytics, no exceptions — same promise as every release.

### Per-alt login streaks that require landing

Each alt on the leaderboard carries its own streak — consecutive days that account actually
landed in a game, confirmed by presence, the same server-truth signal that fixed ghost rows in
v1.5. Launching isn't enough on purpose: an alt that hit a privacy wall and sat at the home
screen didn't collect anything that day, and a streak that counted it would be lying to you.
One alt breaking its chain doesn't touch the others. Streaks begin at this version.

### The connector's Stop button grows teeth

If you use the Ur MCP plugin to let Claude drive RoRoRo: `stop_accounts` used to report "stop
issued" while the client kept running — Roblox raises its own confirm dialog on close, and the
old code took the dialog appearing as success. It now runs the same sequence as the Stop button:
ask the way clicking the X asks, wait, ask once more (which dismisses Roblox's confirm into a
clean exit), and force-close only if the client never answers. Measured live: a real in-game
client closed cleanly in 7 seconds, no kill, Roblox's own settings saved on the way out.

### Smaller fixes

- **Renames reach History.** New rows show your local rename; the account's dead Roblox name no
  longer haunts the page. Old rows keep the name they were written with — they're history.
- **The consent sheet keeps its buttons.** A plugin with a tall manifest could push Install and
  Cancel off the bottom of the window. The buttons win now.
- **A rare startup crash is gone.** Two parts of startup could race to build the tray icon on
  the wrong thread; it took the app down once on 2026-08-20. Construction is now pinned to the
  right thread no matter who asks first.

### Compatibility

Nothing breaks. Saved accounts, themes, plugins, and settings carry over. No new permissions.
The stats file (`session-stats.json`) is new and lives beside your history file; deleting it
costs you your records and nothing else. Plugin contract unchanged — every existing plugin keeps
working.

### Known limits going into the next update

- Peak-alts and per-alt streaks count from this version forward ("since history began") — the
  past didn't record what they need, and inventing it would be worse than starting at zero.
- Stats accrue only while RoRoRo is running; a client that outlives the app records no end time
  and is counted in the "didn't record an end" note rather than guessed at.
