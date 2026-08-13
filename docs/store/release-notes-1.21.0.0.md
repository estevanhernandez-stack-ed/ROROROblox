# RoRoRo v1.21.0.0 — release notes

The release that gets the app ready to be photographed. v1.20 gave every button one vocabulary;
this gives the surfaces they sit on one too, and then fixes three things underneath that had nothing
to do with looks and everything to do with running eight clients at once.

If you only read one line: **RoRoRo no longer leaks a Roblox process every time it closes**, and
**the update check now looks at the version that is actually about to launch.**

## Short list, for the GitHub release and the Discord post

```
• Plugin processes now die with RoRoRo instead of surviving it. If RoRoRo crashed or was force-closed, its plugins stayed running forever, one more every session — six were found alive on one PC. They are now tied to RoRoRo at the OS level, so even a force-kill takes them with it, and any strays left by an older version get swept on startup.
• The update check was reading the wrong version. It asked "what is the newest Roblox on this PC" when the question is "which Roblox is about to launch" — and during an update those are different. That is why batch-launching your alts while Roblox was updating went haywire: RoRoRo said no update was pending, released all of them, and each one discovered separately that it was stale.
• RoRoRo also now holds a batch when it sees Roblox versions landing on top of each other, without needing the network at all.
• Both warning banners look the same as each other now, and are told apart by their words and a ▲ instead of by colour. The old amber-versus-other "distinction" only survived flatline because one of the banners was ignoring the theme entirely.
• Midnight's secondary text was below the accessibility floor at 4.19:1 — since the day that theme was written. Fixed, and the check that missed it was widened so it cannot miss the next one.
• The About window's background follows your theme. The 626 Labs mark stays fixed, on purpose, and there is now a test that enforces the split.
• History rows separate by spacing rather than a line, because a line dark enough to pass the contrast floor would have repainted every theme you have written.
• MainWindow has zero hard-coded colours in it for the first time.
```

---

## Longer form

### Plugin processes no longer outlive RoRoRo

This one is worth reading even if you do not use plugins, because it explains a PC that gets slower
the longer you use RoRoRo.

A plugin runs as its own process. When RoRoRo shut down cleanly it asked its plugins to stop, and
they did. **When it did not shut down cleanly, they simply kept running** — after a crash, after
Task Manager, after anything that skipped the polite path. One more orphan per session, forever.
Six were found alive on one machine.

The fix is not a better shutdown hook, because a shutdown hook is exactly what does not run when the
host is killed. Plugins are now attached to RoRoRo through a Windows **job object**, which is
enforced by the OS: when RoRoRo's process ends, for any reason, Windows ends its plugins too.
Nothing to catch, nothing to ask nicely.

Strays left behind by earlier versions are swept on startup, so this cleans up after itself once.

### The update check was answering the wrong question

If you have ever batch-launched your alts while Roblox was mid-update and watched it go sideways,
this is why.

RoRoRo has a gate that is supposed to notice a pending Roblox update and launch **one** client
first, let the update finish, then release the rest. Correct design. It was fed the wrong number.

It asked *"what is the newest Roblox installed on this PC"*. But a launch does not run the newest
installed version — it runs whatever the `roblox-player` handler is pointed at, and **during an
update those are two different versions.** Measured on a real machine: the handler was pinned to
`0,733,448` while `0,734,0` sat newer on disk. The gate read `0,734,0`, matched it against what
Roblox was serving, concluded nothing was pending, and released the whole batch — at which point
every client independently discovered *it* was the stale one and started updating at the same time.

It now reads the version the handler actually points at, and holds if **either** that or the newest
install disagrees with what Roblox is serving.

There is a second, cheaper signal too: if RoRoRo sees more than one Roblox version installed in the
last few minutes, updates are landing on top of each other and it holds the batch regardless. That
one needs no network at all.

### One recipe for both warning banners

The two warning banners in the app were told apart by colour. That looked fine, and it was not
fine — the amber one survived the flatline theme only because it was ignoring the theme entirely.
A distinction that only exists when one participant is broken is not a distinction.

Both now take the same themed recipe, and are told apart by **their words** and a `▲` marker.
That works in flatline, in a monochrome screenshot, and for anyone who does not separate those hues.

### Midnight's secondary text has been failing since it was written

The contrast gate was widened to check three prose pairs unconditionally, and it went red
immediately — on a real shipped defect. **Midnight's secondary text measured 4.19:1**, under the
4.5:1 AA floor, and had been since the day that theme shipped. Every instrument in the suite had
been blind to it.

Fixed to 4.60:1. The point is not the fix; it is that a gate which only checks what it was pointed
at will keep passing a theme nobody pointed it at.

### The About mark, and a claim that turned out to be wrong

The About window's background now follows your theme while the 626 Labs mark stays fixed, with a
test that enforces the split.

Building that test found the mark had **already** been partly themed — the magenta block's lit face
was bound to a theme slot. That was a real bug and the fix was correct. But the reason first given
for it was not: a later pixel render proved that face is completely hidden behind the block above
it, so no user has ever seen it. The fix stands; the explanation was corrected in five places rather
than quietly left standing.

### History rows separate by rhythm, not by a rule

The obvious fix for "these rows run together" is a separator line. At the contrast the accessibility
rule would require, that line would have landed as mid-grey — and it would have repainted **every
theme any user has ever authored**, without asking.

So the rows separate by spacing instead. Same readability, nobody's theme changes underneath them.
