# Clan Discord post — v1.30.0.0

> Post when the Store listing actually shows 1.30, not when certification clears — same rule as
> every post since 1.21. Submitted for certification 2026-09-20. Setup.exe users get it within a
> day of the GitHub release regardless, so if the release is published first this post can go out
> on that and say so.
>
> **This one CAN lead with alerts, and 1.28's post could not.** The 1.28 post deliberately buried
> metric alerts because nothing reported a number — a plugin had to, and no plugin was out. Both
> halves exist now: Ur Score is in the plugin catalog, and 0.5.4 ships the tick that pairs with this
> release. So the lead is the thing the people who actually set alerts up will feel on their phones
> within a day.
>
> **The lead is a fix that makes the app quieter, and it is framed as a fix.** Anyone who set a rule
> during a battle got buzzed every few minutes for hours. After this they get one. That is
> unambiguously good, but it will read as "my alerts stopped working" to someone who does not know
> why — so the post says outright that going quiet is the fix, not a fault. Same sentence as the
> release notes; this is the audience that most needs it.
>
> **The recovery tick is second, not first.** It is the genuinely new thing, but it needs Ur Score
> 0.5.4 AND this release, and it is off until you tick it. Leading with it would send people
> looking for a switch whose other half may not be installed yet.
>
> **The plugin-install fix gets a line because a real person reported it.** Issue #136, from
> NinjaZix on v1.21: installing a plugin timed out after 100 seconds and there was nothing they
> could do. Anyone who tried once and gave up has a reason to try again, and naming that it was
> reported is how a clan learns that reporting things works.
>
> **What is deliberately NOT in here:** the crossing rule's internals, the batcher's group key, the
> shared title builder, the smoke tool's baseline injection, the CI test that had to stop racing the
> clock, and the test counts. None of it is felt by anyone using the app.
>
> **Not claimed:** that everyone can install Ur Score in two clicks. On a Store install the plugin
> marketplace is off by design, so it is a pasted link rather than a browse. The post points at the
> plugin channel instead of pretending otherwise.

---

```
**RoRoRo v1.30 is out** 🎉

Store: it'll arrive on its own, or open the Store app and hit "Get updates."
Setup.exe folks: it updates itself as usual.

**Your alerts will go quiet. That's the fix.**
If you set up an alert during a battle, it used to buzz you again every
few minutes for as long as the thing was true — three hours under your
pace meant thirty notifications about one problem. Now it tells you once,
when the number crosses your line, and shuts up until it crosses back.

Two things to know so it doesn't look broken:
• A brand-new rule waits for its second reading before it can say
  anything. A few minutes, usually.
• If you restart RoRoRo while something is already wrong, it won't
  re-announce it. It'll tell you the next time it genuinely changes.

**You can now ask to be told when it's over**
Tick "Also tell me when it comes right again" on any rule and you get a
second buzz when the number comes back — "K0i2 clan points is back above
9B", or "is climbing again" for a pace rule. Off unless you tick it, and
it goes wherever that rule's alerts already go.
Needs Ur Score 0.5.4, which is out now.

**Plugins install on a slow connection**
Installing a plugin gave up after 100 seconds, so if your connection
couldn't pull the whole file in that time you could never install one, no
matter how many times you tried. It gets ten minutes now, and tells you
what to try if it still runs out. Reported by NinjaZix — thanks, that one
was a real wall.

Nothing else moved. Accounts, themes, history and settings all carry over.
```
