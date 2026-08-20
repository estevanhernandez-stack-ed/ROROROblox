# The wall-clock render flake has a cause: a Roblox client was running

**Date:** 2026-08-20 · **Status:** cause identified, reproduced, and cleared

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
- Worth considering: have the render fixture refuse to run — loudly — when a Roblox client is
  detected, rather than timing out and reporting as a failure. A precondition check would name the
  cause instead of leaving the next person to rediscover this.

That last point is the same shape as the smoke instrument that now refuses to measure an idle
desktop: **a test that cannot produce a valid result should say so, not produce an invalid one.**
