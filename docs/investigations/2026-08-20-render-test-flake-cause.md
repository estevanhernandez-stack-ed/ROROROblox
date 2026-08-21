# The wall-clock render flake has a cause: a Roblox client was running

**Date:** 2026-08-20 · **Status:** cause identified, reproduced, and cleared

> **CORRECTION 2026-08-21 (F-107 re-measurement): the cause named below did not survive F-105.**
> These measurements were taken roughly 27 hours before commit `7dbe997` stopped the render
> harness from executing the real `App.OnStartup`, so every client-up run below ran real startup
> inside the test host — and a live client is exactly what routes that startup into its blocked
> branch: the singleton event reads as held, which reaches seamless takeover and the
> already-running modal's `ShowDialog()` on the shared render thread, plus a network update check
> inside the same 60s budget. The client below was idle at a desktop window, which saturates no
> GPU, and full 16-core CPU saturation had already been measured clean on 2026-08-12.
> Re-measured at HEAD on 2026-08-21 with startup suppressed: a real windowed client up, the guard
> bypassed, three consecutive full-suite runs — **1798/1798 in 7 seconds, every run**. The
> starvation theory was F-105 wearing a third disguise, and the client half of the render guard
> was removed the same day. The table below stays as evidence of what confounding looks like:
> "the only variable" was never the only variable.

## The standing mystery

`Rendering/*` tests have failed intermittently for weeks with

```
System.TimeoutException : Window render for '<surface> [<theme>]' did not finish within 60s
on the shared host thread: it began rendering after 0.0s of queue wait and then did not
finish, so THIS render is the slow one.
```

Recorded repeatedly as "one unidentified failure, re-runs green", and carried as a to-do with a
standing instruction: **do not raise the timeouts.** That instruction was right.

## What it actually is

Caught by accident while hand-verifying F-103. A bare `RobloxPlayerBeta` had been started to test
orphan adoption and was left running. With it up:

| run | result |
|---|---|
| branch, client running | **2 of 5 failed**, 1m |
| branch, client running (again) | **1 of 5 failed**, 1m |
| `main`, client running | **1 of 5 failed**, 1m |
| **client killed** | **5 of 5 passed, 2 seconds** |

One minute to two seconds, on the same commit, with the only variable being whether a Roblox client
was alive.

## Why it looks like flakiness

The render tests share one WPF host thread with a 60-second per-render timeout. A Roblox client
saturates the GPU and a good share of the CPU, so a render that normally completes in tens of
milliseconds does not finish inside a minute. Which specific tests trip depends on scheduling, which
is why the count varied between runs and why it never reproduced when someone re-ran it later on a
quiet machine.

**It was never a race in the test code.** It is resource starvation from outside the test process
entirely, which is exactly the kind of thing a test cannot see about itself.

## What follows

- **Do not raise the 60s timeout.** It is not too tight; it is being starved. Raising it would hide
  the signal and make the suite slower for everyone.
- **A Roblox client running during the suite invalidates the render tests.** Anyone chasing a render
  failure should check for `RobloxPlayerBeta` before anything else.
- ~~Worth considering: have the render fixture refuse to run — loudly — when a Roblox client is
  detected.~~ **Done, same day.** `RenderEnvironment` already refused to render while RoRoRo itself
  was running; it now covers the client too, in the same place, with its own message. Verified live
  in both directions: with a client up, 81 gates refuse in **236 ms** naming the pid, instead of
  timing out one minute at a time.

That last point is the same shape as the smoke instrument that now refuses to measure an idle
desktop: **a test that cannot produce a valid result should say so, not produce an invalid one.**

## Postscript, same day: this cost a wrong diagnosis before it was fixed

Hours after the table above was measured, a full-suite run reported **103 failed**, and it was
attributed in writing to a stale-DLL link from a locked build. That was wrong. `RenderEnvironment`
had a RoRoRo instance running and was doing exactly what it exists to do — and its own header
already records the signature: *"App running: 103 failed, 98 failed, host crash."* Same number, same
cause, documented since 2026-08-12.

The diagnosis went wrong because the top line reads `Failed: 103`, which looks like a code
regression, while the actionable message sat inside failure text nobody opened. The build error from
the DLL lock was the loudest thing on screen, so it got the blame.

**The guard worked and was still misread.** That residual is filed as F-107 rather than left here:
when the guard trips it produces ~100 identical failures, and one loud failure would be better.
xUnit 2.9.3 has no built-in `IAssemblyFixture`, so it is not a free change.

The lesson that generalises: **a check is only as good as the place its answer lands.** This one put
a correct answer somewhere a hurried reader would not look — the same failure shape as F-099's
warning going to a log file and F-103's symptom printing at Debug.
