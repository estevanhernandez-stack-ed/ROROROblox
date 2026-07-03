# How to grab your logs (RoRoRo + Ur Task)

When something breaks — a plugin won't start, RoRoRo crashes, an install goes sideways — these two files let us diagnose it without a screen-share. Nothing in them leaves your machine until you send them; RoRoRo never writes cookies or passwords to its logs.

## Ur Task log (plugin problems: won't start, crashed, froze, macro weirdness)

**Easiest way:** right-click the **Ur Task icon in your system tray** (the little `^` near your clock) → **Open log folder** → drag `ur-task.log` into the Discord thread.

**If the tray icon isn't there** (plugin never started): press `Win+R`, paste this, hit Enter, and grab `ur-task.log`:

```text
%LOCALAPPDATA%\626Labs\RoRoRoUrTask\logs
```

There may also be a `ur-task.1.log` (older history) — send both if they exist. They're small (1 MB max each).

## RoRoRo log (host problems: crashes, installs, launches, updates)

Press `Win+R`, paste this, hit Enter:

```text
%LOCALAPPDATA%\ROROROblox\logs
```

Grab the file named with **today's date** (e.g. `rororoblox-20260703.log`). These can be 10 MB+, so **right-click → Send to → Compressed (zipped) folder** first and send the zip.

## Which one do we need?

| Symptom | Send |
| --- | --- |
| "Ur Task stopped — click to restart" banner, plugin vanishes | **Both** |
| Plugin installed but nothing ever appeared | **Both** |
| Macro played wrong / skipped an alt / hotkeys dead | Ur Task log |
| RoRoRo itself crashed, won't launch, or an update broke it | RoRoRo log |
| Install failed partway | RoRoRo log |

## Privacy note

Logs contain your account **display names** and Roblox user ids (never cookies, never passwords). Send them in a DM or the private support thread — don't paste them in public channels.
