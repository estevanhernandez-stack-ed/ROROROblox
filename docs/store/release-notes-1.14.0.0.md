# Release notes — v1.14.0.0

> Paste the block between the `---` markers into the GitHub Release body.
>
> **Bundles two internal releases.** v1.13.0.0 was tagged, built, and never submitted — its
> GitHub release sat as a draft. Its FPS-cap work is folded in here rather than shipped
> separately, so users and Store reviewers see one coherent release instead of two.
> [`release-notes-1.13.0.0.md`](release-notes-1.13.0.0.md) has the long-form version of the
> FPS-cap story if you want it for a Discord post.
>
> Reviewer letter: [`reviewer-letter-1.14.0.0.md`](reviewer-letter-1.14.0.0.md). One new
> PlaceLauncher request shape on an already-disclosed endpoint; no new hosts, capabilities, or
> stored data.

---

## Recycle puts you back in the *same server*

Recycle used to put you back in the game. Not the server — the game. Roblox picked a server
with room, which is rarely the one your squad is in. So the button that fixes a bloated client
cost you your spot, and after that happens once you stop pressing it.

Now it puts you back exactly where you were. Same server, same squad, same run.

**Squad Launch got the same upgrade, from the other direction.** It could only ever target a
private server. Paste a plain game link now — `roblox.com/games/<id>` — and your accounts go
into one *public* server together: the first one goes in, RoRoRo reads which server it landed
in, and sends the rest there.

A couple of things worth knowing, because they'll look like bugs otherwise:

- **A full server puts you in line.** Roblox queues you ("server full, waiting in line 1 of 7")
  and lets you in as spots open. That's normal — the join is coming, it's just late. RoRoRo will
  say some accounts aren't in yet and point you at their windows. Don't recycle them; you'd lose
  your place in the queue.
- **Same server isn't always the same world.** Pet Sim spreads its worlds across linked servers,
  so the game may move an account to its own world right after it joins. That's Pet Sim's doing,
  not a failed join.
- **Private servers are untouched.** They already point at exactly one server, so nothing about
  them changes.

RoRoRo checks with Roblox afterwards to see where each client actually ended up, and tells you
if someone didn't make it — including which accounts, by name.

## Recycle, when you want it

Recycle only used to appear when RoRoRo was warning about an account's memory. Now that it also
gets you back to your server, that's not the only reason to reach for it — so
**Settings → "Always show Recycle on running accounts"** puts it on every running row. Off by
default; nothing changes unless you turn it on.

## Your FPS caps actually stick

*(Built as v1.13, never released on its own — shipping here.)*

If you set different frame-rate caps on different accounts, they didn't always take: an alt
would run at the cap you'd set on some *other* account. Roblox keeps **one** settings file for
every client on your PC, and two launches close together would race each other into it.

RoRoRo now waits — before writing the next account's cap it waits until the previously launched
client has proven it read its own.

**What you'll notice:** if your accounts have *different* caps, launches stagger by about 15-20
seconds each. If they all share the same cap, nothing changes. Want your launches fast again?
Set every account to the same cap.

## Smaller

- The **Private server** button is now called **Squad Launch**, because it does more than
  private servers now. Same button, same place.

---

## Under the hood, for the curious

Two things in this release came from watching the real thing rather than reasoning about it,
and both changed the design.

**Roblox tells you when a server is full.** It doesn't quietly send you somewhere else — it
queues you, visibly, and honors the request late. That was the thing we most needed to know and
couldn't find documented anywhere: a silent redirect would have looked exactly like success. It
also means an automatic retry would be worse than useless, since it would throw away the place
in line the account was already holding. That's why RoRoRo tells you instead of retrying.

**The first version of the "who made it?" check was too impatient.** It gave everyone 90
seconds. A real eight-account squad landed at 11s, 28s, 53s, 2m15s, 2m33s, 2m33s, 2m34s and
2m59s — so it called the last four a miss, three of them within nine seconds of the cutoff. A
verdict that flips to false eight seconds after it prints is just wrong. The window is four
minutes now, from those numbers.
