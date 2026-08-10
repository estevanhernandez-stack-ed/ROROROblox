# RoRoRo v1.17.0.0 — release notes

Two audiences, two blocks. The Partner Center block leads with the accessibility theme, because an
accessibility theme is a listing asset rather than a footnote. The longer form is for the GitHub
release body and the clan Discord post.

## Paste-ready: Partner Center "What's new in this version"

Same shape as v1.15's and v1.14's. Benefit first, mechanism second, one line each.

```
• New theme: Flatline. Built to stay readable when you can't rely on colour — on a cheap panel, in direct sun, or if you don't tell colours apart the way the default theme assumes. It sits in Settings > Appearance beside the other three.
• Flatline is measured, not claimed. Every text-on-background pair in the app clears the AA bar of 4.5:1 under it, with nothing carved out as an exception.
• Every theme now says its warnings in more than colour. An expired account gets a rule down the left edge of its row, and idle and Roblox-version warnings get a triangle marker beside the text. Cover the colour and you can still tell what is wrong.
• Each theme in the picker now carries one plain sentence saying what it is for, so you know what you are choosing before you choose it.
• Status dots and the idle and memory chips now follow the theme you picked instead of holding the default theme's colours. Five places were getting that wrong, including the one in the status bar.
• Heads up on the default theme: the dot that means "running" is white now, not green. It used to be a fixed colour no theme could change, and white is the one value that stays clearly apart from the expired and idle dots in all four themes.
```

---

## Longer form — for the GitHub release and the clan Discord post

## A theme that doesn't need you to see colour

RoRoRo says a lot in colour. A green dot means an account is running, amber means the session went
stale, magenta means Roblox limited it. If you don't separate those colours the way the default
theme assumes — and roughly one man in twelve doesn't — the app isn't hard to read, it's missing
information. Same story on a cheap laptop panel, or with sun on the screen.

**Flatline is the fix, and it's in Settings > Appearance now.** Pick it and every part of the window
you look at all day goes colourless. No prompt, no warning, no restart. It repaints while you're
looking at it.

It's greys, but it is not one flat grey. Rows still stand off the page more clearly than they do in
the default theme, because separating things by lightness still works fine when telling colours
apart doesn't. And the palette is measured rather than eyeballed: the text-and-background pairs in
the app were all checked against the AA readability bar under Flatline, and nothing was exempted to
get there.

## Every theme reads better now, not just the new one

Building Flatline made the colour-only signals impossible to miss, and the fixes for them ship in
**all four** themes. Nothing here is Flatline-only.

**An expired account now has a rule down the left edge of its row.** The amber fill stays. If you
can't pick the amber out, the rule is still there, and the row still says "Session expired" in
words.

**Idle and Roblox-version warnings get a triangle.** The memory chip already did this, so this is
one warning mark across the whole window instead of one place remembering and everywhere else
forgetting.

**We left alone the things that were already fine.** The include/skip dot on each row tells you its
state by being filled or hollow. The MAIN tag says MAIN. Adding decoration to those would have made
a theme built on legibility harder to read, which is the wrong direction.

## Five places that were ignoring your theme

Pick any theme and some things stayed the default theme's colours no matter what: the status dot on
each row, the idle chip, the memory chip in both the normal and the compact row, and the live-count
dot in the status bar. Those colours were baked into the code where no theme could reach them.

All five come from the theme now. If you've built your own theme, more of the app follows it than
did last week.

## One visible change to the default theme

**The dot that means "running" is white now, instead of green.** That's the default theme too, not
just Flatline, so you'll notice it.

The old green was one of those baked-in colours no theme could change. Picking a replacement meant
picking one value that stays clearly apart from the expired dot and the idle dot in every theme, and
white is the one that does. The word beside the dot — "In Pet Sim 99", "Ready", "Session expired" —
has always said the same thing the dot says, and still does.

## Also

- Every theme in the picker now has one sentence under it saying what it's for. Four themes, four
  sentences. A name you don't recognise shouldn't read like a broken mode.
- Drop your own theme JSON in and the line just isn't there. No empty gap where it would have been.
- **Known limit, so you don't find it yourself.** Two banners that only show up in specific
  situations still carry their own amber under Flatline: the Bloxstrap notice, and the one about
  recovering the Roblox lock. They're on the list, and they get fixed together with a shared banner
  style rather than one at a time.

## Getting it

Direct download updates itself. Store copies come through the Store as usual. Nothing to reconfigure
— your accounts, themes, private servers and settings all carry over.
