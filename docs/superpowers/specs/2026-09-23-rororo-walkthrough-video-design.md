# RoRoRo walkthrough video — design

**Date:** 2026-09-23. **Status:** approved section by section in conversation with Este the same day.
**Scope:** one long-form YouTube video (15-20 min) about RoRoRo and its plugins. The plan lives in
this repo; the writing, voice and edit happen on Este's other machine, whose video tooling lives in
`626Labs-LLC.github.io` and this repo.

## Why

RoRoRo has a feature map ([`docs/features.md`](../../features.md)), a feature ledger, a decision
log and three build stories, and no long-form video. The 2026-08-30 launch video
(`626Labs-LLC.github.io/assets/video/rororo/`) is a short, stills-based ad. This one is the long
answer: how it was built, then everything it does.

## Decisions (Este, 2026-09-23)

| Question | Answer |
|---|---|
| Audience | Builders and developers first; also a full walkthrough for people who want the tour. Opens with the build story. |
| Voice | Este's ElevenLabs voice (ItsJustEste, stability 0.35, digits spelled out for the voice — the launch video's settings). |
| Length | 15-20 minutes, with YouTube chapters. |
| Accounts on screen | Real accounts with RoRoRo's streamer mode on. |
| Production approach | Script first: story and narration drive the shot list; captures fill it. |
| Who writes the story and script | Este's other machine. This machine writes the brief. |
| Where media travels | OneDrive: `%USERPROFILE%\OneDrive\RoRoRo video 2026\`. Git carries text only. |
| Where the plan lives | This repo only. |

## 1. Chapters

| At | Chapter | Carries it |
|---|---|---|
| 0:00 | **Cold open** (~30 s) | Eight Roblox clients side by side on one PC, then the RoRoRo window. No narration until the story's first line. |
| 0:30 | **The story** (~4½ min) | The fourth, wrap-up build story in Este's voice (section 2). Mostly B-roll: the app across versions, commits, the decision log, Partner Center certifications. |
| 5:00 | **The app** (~8 min) | Accounts and one-click launch, the encrypted vault, live status, games and private servers, Squad Launch and Follow, memory watchdog and Recycle, Known Roblox issues, history, themes, compact mode, streamer mode, languages, alerts to Discord and phone. Roughly the feature map's sections in order. |
| 13:00 | **Plugins** (~5 min) | Opens on the macro wall: core observes, consented plugins act. Then Ur Task, Ur AFK, Ur OCR, Ur Score feeding metric alerts, and Ur MCP driving RoRoRo from Claude last, as the builder-audience payoff. |
| 18:00 | **Close** (~1 min) | How it is made (the test suite, the decision log, same-day Store certification, all public), install links, *Imagine Something Else.* |

The feature map's "Build, CI, tooling" and "Test instruments" sections get no chapter; the story
carries the how, and the close gets one line of it.

## 2. The story (brief for the other machine)

A fourth build story in the same first-person voice as the other three, written on Este's other
machine and saved here as `docs/build-story-2026-09-23.md`. The narration is a spoken cut of it:
about 680 words for 4½ minutes at ~150 words a minute.

**The thread:** each story is someone asking the app for something, and the app learning to answer.

- [`build-story-2026-05-28.md`](../../build-story-2026-05-28.md) — competitor recon at midday, v1.7
  shipped by evening.
- [`build-story-2026-06-12.md`](../../build-story-2026-06-12.md), "My time with Fable" — the
  collaborator, and its absence.
- [`build-story-2026-09-08.md`](../../build-story-2026-09-08.md), "The words were the easy part" —
  the acquisitions report showed who actually used it, and they needed it in their own language.
- **Today:** twice a clan member believed RoRoRo was broken and it was Roblox (the memory leak; the
  window that froze on drag). So the app now says so itself: Known Roblox issues
  ([`decisions.md`](../../decisions.md), the 2026-09-23 entry).

It lands on *the app learning to tell you what isn't its fault*, which hands straight to "The app"
chapter.

**Constraints:** quote the earlier stories (one line each, where it lands) rather than retelling
them; ground every fact in the repo, the commit log and the decision log; nothing is said on
Este's behalf about Fable beyond what the June story says. Voice rules are the repo's
(CLAUDE.md "Voice and brand").

## 3. Production folder and shot list

**In this repo, `docs/videos/2026-walkthrough/`,** beside the launch video's scripts:

- `brief.md` — the chapters, audience, streamer rule and story brief above, as the working copy.
- `shot-list.md` — one row per shot, grouped by chapter.
- `captures-manifest.md` — every captured file: name, shot number, duration, size, date, app version.
- `script.md` — empty until the other machine writes it.

**Shot-list row:**

| # | Chapter | Feature (feature-map row) | On screen | Owner | Length | Status |
|---|---|---|---|---|---|---|
| 12 | The app | Memory watchdog + Recycle | A memory warning chip on a row; Recycle clicked; the client back in its server | me | 8 s | captured |

- **Owner** is `me` (anything this machine drives through RoRoRo's own windows) or `Este` (a live
  game, gameplay, or a plugin acting inside Roblox).
- **Status** is `open`, `captured` or `re-shoot`. Whoever finishes a row flips it.
- **The feature column names the feature-map row**, so an app change shows which shots went stale.

**Media stays out of git** (several GB; LFS would still bloat the repo). Captures are written to
`%USERPROFILE%\OneDrive\RoRoRo video 2026\captures\` and sync to the other machine on their own.

## 4. Capture method (this machine)

- **Driving:** UI Automation against RoRoRo's own windows only (open, click, switch pages by name),
  the method of the 2026-09-23 live run. Never Roblox's windows.
- **Recording:** ffmpeg on the RoRoRo window, 60 fps, H.264 MP4 at native size; full display only
  for full-screen shots. **Every shot gets a still (PNG) and a clip**, because the existing renderer
  is stills-based (section 6).
- **Pacing:** scripted shots move at readable speed with deliberate holds, leaving room to cut and
  to narrate.
- **Before any capture:** streamer mode on and verified in a screenshot; UI in English, default
  theme, fixed window size; each plugin window checked for real account names before it is captured.
- **No Roblox launches by this machine** without asking Este first: those are real launches of real
  accounts. Shots that need a running client are Este's rows.
- **Version:** the current release (v1.31.0.0). Older versions for the story's B-roll come from
  screenshots already in the repos and release pages, not from reinstalling old builds.

**Split, roughly.** *Mine:* most of "The app" (RoRoRo's own UI), Known Roblox issues page and notice,
settings, themes, compact mode, history, diagnostics, the plugin catalog and consent sheets, and Ur
MCP driving RoRoRo (this machine can be the Claude on screen). *Este's:* the cold open, anything
in-game (Squad Launch landing, Follow, Recycle's return to the server), and Ur Task, Ur AFK and Ur
OCR acting on a game.

## 5. Order and hand-off

1. **This machine:** this spec, then `brief.md` and a first `shot-list.md` built from the chapters
   and the feature map. Pushed.
2. **Other machine:** the story and the full narration script from the brief; render the narration.
   Narration lengths become shot lengths.
3. **This machine, in parallel with 2:** capture the `me` rows (the UI doesn't depend on the words).
   Once narration exists, retime and re-shoot anything that runs short.
4. **Este:** record the `Este` rows.
5. **Other machine:** the edit, chapters, and the YouTube description (this machine can draft it
   from the brief).

## 6. The existing pipeline, and the open question it raises

The launch video's pipeline is `626Labs-LLC.github.io/assets/video/rororo/src/`: `manifest.json`
(slides of images, each with its narration line), `render.py` (renders 9:16 / 4:5 / 1:1 / 16:9 via
the tiktok-video-maker skill's `build_video.py`), `make_frames.py`, `shot-crops.json`, and
`assets/video/tools/level_vo.py` for the voice track. It animates **stills** (Ken Burns, slides,
cascades).

**Open, for the other machine to decide:** a 15-20 minute walkthrough needs real screen recordings.
Either the renderer learns to take clips, or the long cut is edited in a video editor and the
pipeline renders only the short spin-offs. Captures ship as both stills and clips so either path
works.

## Risks

- **Streamer mode masks the account manager, never in-game.** Gameplay recordings must keep name
  tags off screen or be blurred in the edit.
- **Plugin windows may show real account names** (Ur Score's score book, Ur Task's assignment
  table). Checked per window before capture.
- **ffmpeg may be missing, or may not capture a hardware-accelerated window cleanly.** Checked before
  the first shot; the fallback is Windows' own screen recorder with Este pressing record.
- **The app moves on.** A release between capture and publish can stale shots; the feature-map
  column in the shot list is the check.

## Out of scope

Localized cuts (the launch video has seven; this one is English first), short-form spin-offs, and a
fully automated re-runnable capture script. The last is worth building only if this video proves
the format.
