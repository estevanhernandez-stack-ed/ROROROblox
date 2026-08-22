# Clan Discord post — v1.22.0.0

> Post when the Store listing actually shows 1.22, not when certification clears — same rule as
> 1.21. Setup.exe users get it within a day of the GitHub release going live regardless.
>
> **Written for someone on v1.21**, which after the catch-up submission is finally where the Store
> crowd actually is. Single release of news this time.
>
> **The mute fix is stated as a fix,** because anyone it bit knows it was broken — "muted an
> account and the pings kept coming" is a thing people experienced, not a confession. Say it
> plainly, say it's gone, move on.
>
> **The Claude section stays short and honest about who it's for.** Most of the clan will never
> use it; the ones who will are exactly the ones running big alt farms, and "your alts fall out of
> game while you're away, you tell Claude to put them back" is their exact life. It links the repo
> and stops. No AI evangelism in a Pet Sim channel.
>
> **What is deliberately NOT in here:** contract 0.9.0, capability names, the settings
> single-owner refactor, the accessibility fence, the shell architecture. One line covers the
> screen-reader work; the rest is invisible and stays that way.

---

```
**RoRoRo v1.22 is out** 🎉

Store: it'll arrive on its own, or open the Store app and hit "Get updates."
Setup.exe folks: it updates itself as usual.

**Everything lives in one window now**

Games, Settings, History, Diagnostics, Plugins and About used to be six
separate pop-ups, and every one of them blocked the window behind it.
They're pages of one window now, with a list down the left. It opens next
to your accounts instead of on top of them, and you can leave it open while
you play. Checking History no longer means closing Settings first.

**Muting an account actually mutes it**

If you ever muted an account's alerts and the Discord pings kept coming
anyway until you restarted — that's fixed. Alert changes land the moment
you click them now. The idle-warning slider had the same problem and got
the same fix.

**Keyboard shortcuts exist**

First time ever. Ctrl+N adds an account, Ctrl+L launches your selection,
Ctrl+J is Squad Launch, Ctrl+F jumps to the account filter, Ctrl+G opens
Games, Ctrl+H History. Press F1 for the whole list. "Stop all" still takes
a click on purpose — that one shouldn't be a typo away.

**The RAM warning learns your PC**

The "room for another client?" math used a fixed number measured on one
machine that isn't yours. RoRoRo now watches what your clients actually
use and warns based on that.

**If you use Claude (the AI assistant)**

There's a new plugin, RoRoRo Ur MCP, that lets Claude drive RoRoRo for
you: launch accounts, follow your main, check who's in game, run your
Ur Task macros. The scenario it exists for — your internet blips while
you're out, alerts say three alts dropped, you remote in and tell Claude
"launch them and run the farm macro on repeat." Setup and install:
<https://github.com/estevanhernandez-stack-ed/rororo-ur-mcp>

Like every plugin: you install it yourself, it asks permission for
exactly what it does, and you can revoke it.

**One more**

Every button and row in the app now announces itself properly to screen
readers. If that's not you, you'll never notice. If it is you, the whole
app just opened up.

Full notes: <https://github.com/estevanhernandez-stack-ed/ROROROblox/releases>
```

---

## If you'd rather post something shorter

```
**RoRoRo v1.22 is out.** Store: "Get updates" pulls it. Setup.exe updates
itself.

The six pop-up windows (Games, Settings, History...) are one window now,
with a list down the left — it opens next to your accounts and stays open
while you play. Muting an account's alerts actually works instantly now
instead of waiting for a restart. Keyboard shortcuts exist for the first
time (F1 shows the list). And the RAM warning now measures what YOUR
clients use instead of assuming every PC is mine.

For the Claude users: a new plugin lets Claude drive RoRoRo — launch alts,
follow your main, run your Ur Task macros. "Three alts dropped, put them
back" while you're away from the keyboard is the whole point.
<https://github.com/estevanhernandez-stack-ed/rororo-ur-mcp>

Full notes: <https://github.com/estevanhernandez-stack-ed/ROROROblox/releases>
```
