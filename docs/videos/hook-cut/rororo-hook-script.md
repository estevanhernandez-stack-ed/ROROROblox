# TikTok: RoRoRo hook cut, "eight clients, one PC"

> **Status: approved to post by Este, 2026-11-22. Not posted yet; posting is his click.**
> Listen to the close before posting: it is a cut of an existing take and has not been heard by a person.
> Render: `python docs/videos/hook-cut/render_hook.py` (needs the hub checkout beside this repo).
> Output: `rororo-hook-9x16.mp4`, 1080x1920, 27.0 s, H.264 + AAC.

**Runtime:** ~27s · **Slides:** 4 · **Voiceover:** yes (ElevenLabs voice: ItsJustEste, four takes already approved for the launch trailer; nothing new was generated)

This is the short cut the July ad plan asked for (`docs/marketing/tiktok-ad-plan.md`, 21 to 34 seconds, the count-up open). The launch trailer is 87 seconds; this is the first 25, rebuilt around the wall.

## The hook (slide 1)
> Overlay text: "8 Roblox clients. 1 PC."
> Voiceover: "Okay, this is EIGHT Roblox clients, running at the same time, on one PC. And the app doing it is free."

Why this hook: the wall is the product. The clients arrive one by one in the first second and a half, so there is motion before anyone decides to scroll.

No text is burned into the video. Add the overlays below in TikTok's editor, where they stay editable.

## Slides

### Slide 2 — the account list (8.0 s to 15.6 s)
- **Overlay text (add in TikTok):** "every alt, one click"
- **Voiceover:** "Save all your alts and manage your accounts. One click each, with live status and the game each one is actually in."
- **TikTok effect suggestion:** none; the push-in does the work.

### Slide 3 — the Library (15.6 s to 21.8 s)
- **Overlay text (add in TikTok):** "pick who plays"
- **Voiceover:** "Every game you've played lands in the Library. Search it, filter it, and pick which account plays."

## CTA (final slide, 21.8 s to 27.0 s)
- **Overlay text:** none needed; the card says "Free on the Microsoft Store. Search RoRoRo."
- **Voiceover:** "Free on the Microsoft Store. RoRoRo, from six-two-six Labs."

The close is cut from the launch trailer's closing take (`slide_10`): its first phrase, then its sixth and seventh. The cut points sit in the take's own pauses, found by measurement, and the result has the three phrases and two pauses expected. Nobody has listened to it. "Store" was spoken before a comma in the original, so it may not land as the end of a sentence.

The line asked for was "Free on the Microsoft Store. Search RoRoRo." It could not be generated on 2026-11-22: the ElevenLabs key on this machine was rejected (401, invalid API key). To use a new line, generated or recorded, save it as `voiceover/close.mp3` beside the renderer and render again; it replaces the cut. Voice `ItsJustEste`, the trailer's model and stability 0.45, then level it with the hub's `level_vo.py`.

## Caption
Eight Roblox clients on one PC, free on the Microsoft Store. Every alt saved, one click each. Search RoRoRo.

## Hashtags
#roblox #petsim99 #ps99 #robloxalt #robloxtips #rororo

Rotate the game tag per post (PS99 first).

## Link

Bio link, with a campaign id so the Store's channel report can tell this traffic from the rest:

```
https://apps.microsoft.com/detail/9NMJCS390KWB?cid=tiktok
```

For YouTube Shorts use `?cid=youtube-shorts`. The launch trailer is on YouTube and TikTok (Este, 2026-11-22), and the Store report showed no clicks under a `youtube` campaign in the 90 days to 2026-11-21. So video either sends nobody or sends them without the campaign id; check that the link under each posted video carries its `cid`.

## What is not in this cut, and why

- **Squad Launch.** The only capture on hand is the empty dialog (no saved servers). Held for seven seconds it is the weakest picture in the set, so the Library took the third beat. A capture with saved servers listed would earn Squad Launch its place back; that needs a live session.
- **The clients tiling in live.** The open animates the nine windows of one real capture. It is an edit of a still, not a recording of launches, and it does not claim a launch speed. A screen recording of real launches would be the stronger open; that also needs a live session.

## Guardrails (same as every RoRoRo surface)

- "for Roblox", never "by Roblox". Never "safe" or "undetectable", and nothing that implies Roblox approves.
- The narration is an AI voice, a clone of Este's own. TikTok and YouTube both ask for the synthetic-voice disclosure when a realistic voice is synthetic; switch it on at upload.
- No macro or automation footage.

## Voiceover script (full, for ElevenLabs Studio or re-recording)

[NARRATOR]
Okay, this is EIGHT Roblox clients, running at the same time, on one PC. And the app doing it is free.

[NARRATOR]
Save all your alts and manage your accounts. One click each, with live status and the game each one is actually in.

[NARRATOR]
Every game you've played lands in the Library. Search it, filter it, and pick which account plays.

[NARRATOR]
Free on the Microsoft Store. RoRoRo, from six-two-six Labs.
