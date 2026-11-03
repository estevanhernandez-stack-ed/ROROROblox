# Store screenshots — one folder per listing language

```
en/  fr/  de/  ru/  pt-BR/  pl/  es/      10 files each
```

Every folder is a complete upload set of ten, in carousel order. Since v1.36 (2026-11-03) the set is
built from two sources, and two frames carry over from English with an **`-en`** suffix.

| file | source | in the translated folders |
|---|---|---|
| `01-accounts` | test harness (`MarketingFrameTests`), fake accounts | rendered in that language |
| `02-account-panel` | test harness | rendered in that language |
| `03-account-health` | test harness | rendered in that language |
| `04-library` | test harness | rendered in that language |
| `05-multi-instance-en` | the 2026-08 composite of eight Roblox windows | English: almost no RoRoRo chrome in frame |
| `06-history` … `09-plugins` | live app, streamer mode, `capture-store-screenshots.ps1` | captured in that language |
| `10-compact-en` | the 2026-08 live capture | English: a strip of running clients |

**Why the harness for 01-04.** Store frames from the live app must be captured in streamer mode (real
account names never reach a shipped asset), and streamer mode masks exactly what v1.36 adds: Robux,
Plus, friends online and the note read as dots. `MarketingFrameTests` renders the real windows from the
real XAML with made-up accounts (NovaBuilds, PetGrinder07, AltThree, GardenKeeper, BloxRunner) and
composites them with `New-StoreFrame`'s exact math. Set `RORORO_MARKETING_FRAMES=1` and run the test to
regenerate; output lands in `../screenshots-v136/<lang>/` and `docs/videos/v136-shots/<lang>/`.

**Dropped in v1.36:** About and Diagnostics (a live Diagnostics frame says "Channel: Dev build").

**Uploading a listing:** take the whole folder. No cross-referencing. Captions in `listing-copy*.md`
follow file-number order.

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
