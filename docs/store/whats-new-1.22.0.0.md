# What's new in this version — v1.22.0.0

> Paste the fenced block into Partner Center → your app → **Store listings** → *What's new in this
> version*.
>
> **Written for someone on v1.21** — a single-release jump this time, so no catch-up preamble.
> Ordered by what a Pet Sim player actually notices: the one-window change is the thing they see in
> the first five seconds, the alert-mute fix is the thing that was quietly wrong.

---

```
One window for everything
• Games, Settings, History, Diagnostics, Plugins and About used to be
  six separate pop-ups that blocked the window behind them. They are
  now pages of one window with a list down the left side. It opens
  next to your accounts, you can leave it open while you play, and
  every button and menu that used to open a pop-up now takes you
  there.

Muting an account actually mutes it
• Muting an account's alerts from its right-click menu used to look
  like it worked while Discord alerts kept firing until you restarted.
  Alert settings now take effect the moment you change them. The
  idle-warning threshold had the same problem and got the same fix.

Keyboard shortcuts, for the first time
• Ctrl+N adds an account, Ctrl+L launches your selection, Ctrl+G opens
  Games, Ctrl+H History, Ctrl+F jumps to the account filter. Press F1
  for the full list. Stopping everything still takes a click, on
  purpose.

The RAM warning learns your PC
• The "room for another client" warning used to assume every PC is
  ours. It now measures what your Roblox clients actually use and
  warns based on that.

For screen reader users
• Every button, chip and row in the app now announces what it is.
  History entries read as one sentence instead of loose fragments,
  and warning banners read their message before their dismiss button.
```

---

## What is deliberately not in this copy

- **The plugin contract bump (0.9.0) and the new connector plugin.** Plugin-author and power-user
  material; the reviewer letter carries the full disclosure, and the Store edition ships no plugin
  catalog. A Store listing that says "an AI can drive this app" invites a certification
  conversation this field is not the place to have.
- **The tools-window architecture.** "One window with pages" is the whole story a customer needs;
  "non-modal shell" is repo vocabulary.
- **Audit numbers (86 unnamed controls).** True, and meaningless here. "Now announces what it is"
  is the same fact in this reader's language.
