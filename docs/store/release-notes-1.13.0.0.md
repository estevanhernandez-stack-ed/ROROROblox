# Release notes — v1.13.0.0

> Paste the block between the `---` markers into the GitHub Release body.
> **Bundles two merged PRs since v1.12.0.0** — #70 (a launch gate that turned out to target
> the wrong writer and is deleted again in #71) and #71 (the fix that works). Net user-facing
> change is #71 alone; #70 never shipped to anyone.
>
> No reviewer-letter change expected: this release reads and writes
> `%LOCALAPPDATA%\Roblox\GlobalBasicSettings_<N>.xml`, a Roblox *settings file* RoRoRo has
> written since v1.2. It does not read from the Roblox client process, so the v1.12 letter's
> memory-counter disclosure is unaffected. Confirm before submitting if Partner Center asks.

---

## Your FPS caps actually stick now

If you set different frame-rate caps on different accounts, you may have noticed they didn't
always take — an alt running at the cap you set on some *other* account. That was real. It's
fixed.

Here's what was going on, because it's a good one.

Roblox keeps **one** settings file for every client on your PC. When RoRoRo launches an
account it writes that account's cap into that file, and the client reads it a couple of
seconds after it starts. Launch a second account inside those couple of seconds and its cap
lands on top of the first one — before the first client has read it. Whoever reads last wins,
and it's a coin flip which that is. That's why it looked random, and why some of you saw the
first account get the second's cap while others saw the opposite.

RoRoRo now waits. Before writing the next account's cap it waits until the previously
launched client has proven it already read its own, then waits for it to finish settling.

**What you'll notice:** if your accounts have *different* caps, launches stagger — about 15-20
seconds each. If they all share the *same* cap, nothing changes and they launch as fast as
they ever did. Most people are in the second group and won't see any difference at all.

There's a note in the app when your caps differ so the wait isn't a mystery, and you can
dismiss it. It'll come back if you change your caps to a combination you haven't seen the
note for yet.

**Want your launches fast again?** Set every account to the same cap. That's the whole trick —
no contention, no wait.

---

## Under the hood, for the curious

This took three attempts and the first two shipped clean test suites while being wrong, so
it's worth writing down what actually settled it.

The first attempt waited for the launched client's *process* to appear. It turns out Roblox
starts more than one process per launch and the first one is frequently not the client — we
measured gaps of 0.02 s and 5.9 s between them, and closing a client produces a lookalike
process too. That mechanism was a coin flip.

The second waited for the settings file to go quiet. Right idea, wrong side of the launch: it
protected *our* write from the previous client but left the newly launched client's *read*
exposed to the next one. "Quiet" is ambiguous — right after a launch the file is quiet because
the client hasn't started writing yet, not because it finished.

What settled it was a ninety-second experiment instead of more reasoning. We primed the
settings file so RoRoRo wouldn't write it, waited for the client's first write, overwrote it
with a sentinel 50 ms later — and watched the client put its own value back three seconds
after. That proved the client had already read before it first wrote, which makes that first
write a reliable signal. Both earlier designs were built on assumptions about timing nobody
had measured.

The tell, if this ever regresses: the pre-write wait in the log reads in *milliseconds* on a
launch where the caps differed. Working, it reads 10-13 seconds.
