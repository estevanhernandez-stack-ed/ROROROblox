# What's new in this version — v1.21.0.0

> Paste the fenced block into Partner Center → your app → **Store listings** → *What's new in this
> version*.
>
> **Written for someone on v1.15**, not v1.20. The Store's last submission was v1.15.0.0, so a Store
> customer updating from the listing is jumping six releases and has seen none of 1.16 through 1.20.
> The direct-download channel is further ahead; this copy is not for them.
>
> Ordered by what a Pet Sim player actually notices, not by release order. The update-during-launch
> fix leads because it is the one that was visibly breaking the product's headline action.

---

```
This update covers six releases. If you installed from the Store before
now, everything below is new to you.

Launching your alts while Roblox is updating
• RoRoRo used to launch the whole batch, and then every client would
  discover on its own that it was out of date and start updating at the
  same time. It now checks the version that is actually about to run,
  holds the batch, updates once, and then releases the rest.

Settings is a real place
• Every setting that used to need a text editor now has a control:
  memory warnings, how much RAM to keep free, how big one client can
  get before you hear about it, alert timing, and careful mode for
  Squad Launch.
• Leave a box empty and the page tells you what RoRoRo picks for this
  PC, with the actual numbers.
• Type something out of range and it tells you why, and keeps the
  value you already had.

A fourth theme, and buttons that behave
• Flatline is a theme that carries no meaning in colour at all, so
  nothing is lost to colour blindness, a bad monitor, or direct
  sunlight.
• Buttons no longer flash pale Windows blue when you hover them. They
  had done that in every theme since v1.1.
• Build your own theme from ten colours and it shows up in the picker.
  It saves as a file you can hand to someone else.

Readability
• Text that was too faint to read against its own background has been
  fixed in two places, including one that had been that way since the
  theme it belongs to was written. Disabled buttons are legible now.
• History rows separate cleanly, and warning banners are told apart by
  their wording and a marker rather than by colour alone.

Smaller things
• Compact mode stays on after a restart.
• Plugins shut down with RoRoRo instead of being left running.
• A theme that fails to save now says so, and still applies for the
  rest of the session.
• Muted accounts are listed in Settings with one button to unmute all
  of them.
```

---

## Shorter variant, if the field is tight or you want it scannable

```
Six releases in one update.

• Launching alts while Roblox updates no longer goes haywire — RoRoRo
  checks the version that will actually run, updates once, then
  releases the rest.
• Settings is a real place. Memory warnings, RAM headroom, alert
  timing and careful mode all have controls instead of needing a text
  editor.
• Flatline, a fourth theme that carries no meaning in colour, so
  nothing is lost to colour blindness or a bad monitor.
• Buttons stopped flashing Windows blue on hover, in every theme.
• Text that was too faint to read has been fixed, including one case
  that shipped that way from the start.
• Compact mode survives a restart, and plugins shut down with RoRoRo
  instead of being left running.
```

---

## What is deliberately not in this copy

- **The plugin theme feed (v1.19).** It matters to plugin authors and to nobody reading a Store
  listing, and the Store edition ships no in-app plugin catalog anyway.
- **Version numbers.** A Store customer does not know or care what v1.17 was. The reviewer letter
  carries the version-by-version breakdown; this does not.
- **Anything about the mutex, DPAPI or the auth-ticket flow.** That is reviewer material, and it is
  in `reviewer-letter-1.21.0.0.md`. A "what's new" that explains its own threat model reads as
  nervous.
- **Numbers like 4.19:1.** True, and meaningless to this reader. "Too faint to read" is the same
  fact in their language.
