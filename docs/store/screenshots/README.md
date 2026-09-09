# Store screenshots — one folder per listing language

```
en/     10 files    fr/  7    de/  7    ru/  7    pt-BR/  7    pl/  7    es/  7
```

`en/` holds the full carousel. The six translated folders hold **seven**, and that is
deliberate — not a broken capture.

## Why seven, not ten

| file | translated? | why |
|---|---|---|
| `01-accounts-running` | **yes** | account rows, buttons and status are all localized |
| `03-about` … `08-theme-builder` | **yes** | six app surfaces, all localized text |
| `02-themes` | no — use `en/` | a 2×2 theme panel. It shows colours, not words |
| `09-compact` | no — use `en/` | a strip of running clients; almost no chrome in frame |
| `10-multi-instance` | no — use `en/` | eight Roblox windows and a sliver of RoRoRo |

Copying those three into six folders would add ~17 MB of byte-identical PNGs to the
repo's history for no visible difference. Point at `en/` instead.

**Uploading a listing:** take the seven from that language's folder, then `02`, `09` and
`10` from `en/`.

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
