# RoRoRo v1.25.0.0 — release notes

The release where your phone joins the crew. Route any alert to your pocket through Pushover or
ntfy — an alt drops out while you're away, and your phone knows before you're back in the chair.
And because silence is the one thing a dead PC can't report, uptime marks now say "all good"
every two hours, so a missing mark is itself the news.

If you only read one line: **RoRoRo can page your phone now — drops, memory warnings, recycle
completions, and an every-two-hours all-good mark — through Pushover or ntfy, set up once in
Settings.**

## Short list, for the GitHub release and the Discord post

```
• Phone alerts. Settings > Alerts has a new push section: pick Pushover (the app most of the clan already carries — paste your user key and an app token) or ntfy (free, no account — RoRoRo makes you a secret topic, you subscribe to it in the ntfy app). Test my phone tells you it works before you trust it.
• Alerts fan out. Desktop, your Discord channel, the clan channel, your phone — tick any mix per alert instead of picking exactly one from a dropdown.
• Two new alerts, both off until you turn them on: a recycle finishing tells you how much memory it clawed back, and uptime marks say "4h up — 6 accounts in" every two hours while anything runs. A mark that doesn't arrive means the PC or RoRoRo died — the one failure nothing can announce directly.
• Roblox stays windowed. If a crash or a mid-game Alt+Enter left Roblox saved as fullscreen, RoRoRo turns that off right before every launch. New toggle in Settings > Startup, on by default; untick it if you actually want fullscreen clients.
• Settings polish: the window no longer flashes white when it opens, and the two blank-means-automatic memory boxes now show the number RoRoRo picked for your PC — "3859 (auto)" — instead of looking empty.
• Rolling back? If you ever go back to an older version after setting up the new alert routing, re-set your routing there — older versions don't know the new options and will quietly skip them.
```

---

## Longer form

### Your phone, paged

Settings → Alerts grew a push section with two services behind it:

- **Pushover** — the app most of the clan already uses. You paste two codes from pushover.net:
  your user key, and a token for an application you create there (name it anything — and the
  **Save the RoRoRo icon** button hands you the 128×128 mark for the form's icon slot). Drops
  arrive as priority pages that cut through quiet hours; everything else respects them.
- **ntfy** — free, no account anywhere. RoRoRo generates a long random topic (that topic is the
  whole secret — anyone holding it can read and fake your alerts, so don't share it), you
  subscribe to it in the ntfy app, done. On iPhone, ntfy delivery is best-effort; Pushover is
  the reliable choice there.

**Test my phone** sends a real notification down the real path, so "it says configured" and "it
actually buzzes" are never two different things. Your keys and topic are stored encrypted on
your PC (same protection as your account vault), never logged, and RoRoRo ships no service
credentials of its own — what leaves the machine when an alert fires is the alert's title and
text, nothing else, and only to the service you set up.

### Tick every place an alert should go

The routing dropdowns became checkboxes. "An account drops out" can hit your desktop AND your
phone AND the clan channel at once; nothing ticked means that alert is off. Your old routing
carried over automatically as ticked boxes.

### Two new alerts

- **An account gets recycled** — the completion notice for a recycle you clicked (or a plugin
  drove) before walking away: *"BaronBloxwell — recycled · was 4.1 GB · back in its server."*
- **Uptime marks** — every two hours of continuous running time: *"4h up — 6 accounts in."* The
  clock starts when your first account launches and resets when everything stops. Its real job
  is the inverse: once you expect a mark, a mark that never comes tells you the PC, RoRoRo, or
  the network died — the only way a machine can report its own death is by missing a heartbeat.

### Roblox stays windowed

Roblox remembers fullscreen from last time — one mid-game Alt+Enter, or a crash that skipped its
own save, and the next client swallows a whole screen. RoRoRo now clears that saved flag in
Roblox's own settings file right before each launch — the same local file it has always written
your frame-rate cap into; nothing new leaves the machine. On by default; Settings → Startup has
the toggle, and your window sizes are never touched — whatever size you (or a macro) leave a
client at still sticks.

### Settings polish

The Settings window opened with a bright white flash since the shell shipped; it now appears
dark and complete, a beat later, with zero white — hunted down with a frame-capture instrument
rather than eyeballs, so it stays dead. And the two memory boxes that mean "RoRoRo picks when
blank" now show what it picked: *3859 (auto)* on this machine's 47 GB, computed from the same
arithmetic the watchdog actually runs.

### Compatibility

Saved accounts, themes, plugins, history, settings — everything carries over, and your existing
alert routing migrates into the checkboxes automatically. No new Windows permissions; the app
package is unchanged from v1.24. Two optional outbound services join the network table
(api.pushover.net and ntfy.sh — or your own ntfy server), used only if you set them up, and the
privacy policy names them.

**One honest warning:** if you roll back to an older version after using the new routing, the
old version quietly skips alert options it doesn't know. Re-set your routing there, or better,
don't roll back.

### Thanks where it's due

The phone alerts stand on two services that do the hard half — the actual push to your pocket:

- **[Pushover](https://pushover.net)** by Superblock — reliable native notifications on both
  phone platforms, with the priority semantics that let a drop cut through quiet hours.
- **[ntfy](https://ntfy.sh)** by Philipp C. Heckel — an open-source pub-sub notification
  service generous enough to run a free public server that makes "no account, just subscribe"
  possible.

Both are independent products, not affiliated with RoRoRo or 626 Labs; you bring your own
account or topic, and their own terms apply to their services.

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
— updated this release with the two optional push services.
