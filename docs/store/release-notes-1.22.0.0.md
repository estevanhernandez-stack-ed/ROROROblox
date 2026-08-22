# RoRoRo v1.22.0.0 — release notes

The release where the six pop-up windows become one, the app learns its first keyboard
shortcuts, and — if you want it — Claude gets hands: a new plugin lets an AI assistant launch
your alts and run your macros, behind the same consent sheet as everything else.

If you only read one line: **Games, Settings, History, Diagnostics, Plugins and About are now one
window you can keep open next to your accounts**, and **there is a plugin that lets Claude drive
RoRoRo for you.**

## Short list, for the GitHub release and the Discord post

```
• The six tool pop-ups are one window now, with a list down the left side. It opens beside your accounts instead of on top of them, you can leave it open while you work, and switching between Games and Settings no longer means closing one to open the other. Every door — the buttons, the Tools menu, the tray — leads to the same window.
• Keyboard shortcuts exist, for the first time. Ctrl+G Games, Ctrl+, Settings, Ctrl+H History, Ctrl+N add account, Ctrl+L launch multiple, Ctrl+J Squad Launch, Ctrl+F jump to the account filter. F1 shows the whole list. Inside the tools window, Ctrl+1 through Ctrl+6 jump straight to a page. Stop-all deliberately has no shortcut — that one you point at.
• Settings changes land everywhere the moment you make them. Muting an account's alerts from its right-click menu used to quietly not reach Discord alert routing until a restart — alerts kept firing for muted accounts. Fixed, along with the idle-warning threshold doing the same thing, by giving those settings one owner instead of three copies.
• New plugin: RoRoRo Ur MCP. Claude Code or Claude Desktop can list your accounts, launch them, follow your main, check who is in game, stop clients, and run or stop Ur Task macros. Installed and consented like any plugin, autostart off — Claude starts it, you can revoke it. Needs this version or newer. github.com/estevanhernandez-stack-ed/rororo-ur-mcp
• The RAM headroom warning learns what a Roblox client actually costs on YOUR machine, instead of trusting a number measured once on ours.
• Every control RoRoRo draws now announces its name to assistive tech. The main window had 86 unnamed interactive controls at the start of the audit; the ones we compose are at zero.
• For plugin authors: contract 0.9.0 adds GetAccounts — every saved account with its Roblox user id and which one is the main, behind a new consent-gated capability.
```

---

## Longer form

### One tools window

Every secondary surface in RoRoRo was a pop-up that blocked the window behind it. Checking History
meant closing Settings first; comparing your saved games against a running account meant a
close-and-reopen round trip every time. Six separate windows, all of them in your way.

They are pages now. One window, a list down the left — Games, Settings, History, Diagnostics,
Plugins, About — and it is not modal: it opens beside the main window and stays open as long as
you want. The title bar names whichever page you are on, pages keep their state while the window
is open, and all the old doors (the two toolbar buttons, the Tools menu, the tray items) lead to
the same place instead of each spawning their own copy.

### Keyboard shortcuts, and a list that cannot lie

RoRoRo shipped its whole life without a single keyboard shortcut. Now: Ctrl+N adds an account,
Ctrl+L launches your selected accounts, Ctrl+J opens Squad Launch, Ctrl+F puts you in the account
filter, and Ctrl+G / Ctrl+, / Ctrl+H / Ctrl+D / Ctrl+P open the tool pages. F1 opens the full
list, which lives on the About page.

The list, the hints in the Tools menu, and the keys that actually fire are all generated from the
same table, with tests holding them together — so the list can never teach you a key that does
nothing. Two absences are deliberate: Stop-all has no shortcut because a destructive mass action
should not be one typo away, and per-row actions have none because they need a row to point at.

### Settings that land the moment you change them

Two settings quietly did not take effect when you changed them. Muting an account's alerts from
its right-click menu wrote the preference to disk — and Discord alert routing kept reading the old
copy until you closed Settings from the tray, or restarted. Alerts kept firing for an account you
had muted, and nothing on screen said why. The idle-warning threshold had the same disease on one
of its two doors.

The cause was three copies of the same settings record, kept honest only by a dialog being modal.
Those settings have one owner now: every change goes through it, everything that reads them reads
it, and the going-modeless tools window is safe because of it. Your webhook URLs also can no
longer be silently reverted by an unlucky pair of clicks — that race is structurally gone.

### Claude gets hands — the Ur MCP plugin

New in the Ur family: **RoRoRo Ur MCP**, a plugin that lets Claude Code or Claude Desktop drive
RoRoRo. Thirteen tools: list accounts, launch one, launch into a specific game or private server,
follow your main or a friend, see who is running and where, wait for an account to land in game,
stop clients, read idle times, and list / run / stop Ur Task macros — including running a macro on
repeat until you say stop.

The scenario it was built for: your internet drops mid-session, alerts say three alts fell out of
game, you remote in and tell Claude — "launch Pokey, Spud, and Clover, run the get-in-position
macro on all three, then the farm macro on repeat." Claude operates, RoRoRo launches, Ur Task's
hands do the clicking.

The wall holds. It installs like any plugin (paste the release URL in the Plugins window), the
consent sheet says exactly what it can touch, autostart stays off — Claude starts it, never
RoRoRo — and the macro keystrokes remain Ur Task's own consented capability; this plugin contains
no input synthesis of any kind. It needs v1.22.0.0 or newer to install.

Repo and setup: `github.com/estevanhernandez-stack-ed/rororo-ur-mcp`

### The RAM estimate is yours now

The "do you have room for another client" warning used a fixed per-client memory figure, measured
once on one machine. Your machine is not that machine. RoRoRo now measures what clients actually
cost as you run them and uses that, so the headroom warning tracks your reality instead of ours.

### The app introduces itself to assistive tech

An audit pass found 86 interactive controls on the main window that announced nothing to a screen
reader — including all 56 follow chips. Every control RoRoRo composes now carries a real name, a
History row reads as one sentence instead of five loose fragments, and warning banners read their
message before offering their dismiss button. There is a fence in the test suite that fails the
build if a new unnamed control ever lands.

### Compatibility

Nothing breaks. Saved accounts, themes, plugins, and settings all carry over. The plugin contract
bump to 0.9.0 is additive — every existing plugin keeps working unchanged. The Ur MCP plugin
refuses to install on hosts older than this version, on purpose, because it needs an API this
version introduces.
