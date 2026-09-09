# Store screenshots — one folder per listing language

```
en/  fr/  de/  ru/  pt-BR/  pl/  es/      10 files each
```

Every folder is a complete upload set. Seven frames per language are captured in that
language; three carry over from English and say so in the filename with an **`-en`** suffix.

| file | in the translated folders | why |
|---|---|---|
| `01-accounts-running` | captured | account rows, buttons and status are all localized |
| `03-about` … `08-theme-builder` | captured | six app surfaces, all localized text |
| `02-themes-en` | English | a 2×2 theme panel. It shows colours, not words |
| `09-compact-en` | English | a strip of running clients; almost no chrome in frame |
| `10-multi-instance-en` | English | eight Roblox windows and a sliver of RoRoRo |

The `-en` suffix is the point: a reviewer glancing at `pl/` can see at once which three
frames are not Polish, rather than having to know. It costs ~17 MB of duplicated PNGs
across six folders, and `10-multi-instance-en.png` is 2.6 MB of that on its own — worth
knowing if repo weight ever becomes the deciding factor.

**Uploading a listing:** take the whole folder. No cross-referencing.

## Regenerating

Requires the app running, **streamer mode ON** (the capture refuses otherwise — real
account names must never reach a shipped asset), and for `01`, live Roblox clients.

```powershell
pwsh -Command "& './scripts/sweep-languages.ps1' -Cultures @('en','fr','de','ru','pt-BR','pl','es')"
```

Switches language through the app's own picker rather than restarting. A restart would
raise the leftover-processes dialog on every pass while clients are running — correct app
behaviour, fatal to an unattended sweep.

Filenames come out engine-shaped (`05-about--brand.png`); `capture-store-screenshots.ps1`
maps them to carousel names.

## What these are not

Not desktop captures. `PrintWindow` reads the window's own device context, so nothing
behind the app is ever in frame — that is why a working machine can produce shippable
assets. The navy is a composited canvas, not a wallpaper.
