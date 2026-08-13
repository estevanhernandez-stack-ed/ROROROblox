# Clan Discord post — v1.21.0.0

> Post when the Store listing actually shows 1.21, not when certification clears. Rollout is not
> instant and "Get updates" showing nothing is a worse first impression than posting an hour later.
>
> **Written for someone on the Store build at v1.15**, because that is where most of the clan is.
> They have seen none of 1.16 through 1.20, so this is six releases of news arriving at once.
>
> **Honesty is the policy here, including on the accessibility fixes.** Two of them describe defects
> that shipped and sat for a long time — outlines at 1.26:1 since v1.1, one theme's text under the
> readable floor since the day it was written. Those are stated plainly, not softened into "improved
> contrast". The clan installed a free tool from someone they know; the thing that keeps that
> relationship is being told what was actually wrong.
>
> **What is deliberately NOT in here:** the invisible half. 60+ copied attribute sets collapsed into
> named styles, 63 button attribute sets onto seven ranks, a build fence, a render-gate suite. Real
> work, and none of it belongs in a Pet Sim clan channel beyond the one line acknowledging it.

---

```
**RoRoRo v1.21 is on the Microsoft Store** 🎉

Most of you installed from the Store, which means you've been on v1.15 this
whole time. This update brings six releases at once. It'll arrive on its own,
or open the Store app and hit "Get updates" to pull it now.

**The one that'll actually change your day**

Launching your alts while Roblox is mid-update. RoRoRo used to fire off the
whole batch, and then every client would separately work out it was out of
date and start updating at the same moment. You know how that goes. It now
checks which version is actually about to run, holds the batch, lets one
update finish, then launches the rest.

**Settings is a real place now**

Memory warnings, how much RAM to leave free, how big one client can get
before it warns you, careful mode for Squad Launch. All of that used to mean
editing a text file, and most of you reasonably never did. It's all on a page
now, in plain words. Leave a box empty and it tells you what it picks for
your PC, with the real numbers.

**Stuff you'll notice**

• Close is no longer the brightest button on the screen. In nine windows it
  was the loudest thing there, sitting next to Remove and "Stop all Roblox
  instances". Those now look like what they do.
• Muted accounts have a home. There's a count and a one-button unmute-all.
  Muting was right-click-only before, so if you muted something and forgot,
  there was genuinely no way to find it.
• Games is back on the toolbar next to Settings instead of buried in a menu.
• Type a number a setting won't take and it tells you why and keeps yours.
  It used to quietly swap in one it preferred.
• A theme file RoRoRo can't read now gets named instead of just never showing
  up in the list.
• Compact mode stays on after a restart.
• Plugins shut down when RoRoRo does, instead of being left running.

**Two things that were broken for a long time**

Being straight with you, because you'd rather know.

Every button and input outline in the app was far too faint against its
background, in the default theme, since v1.1. It's been that way about as
long as RoRoRo has existed. All of them now meet the accessibility standard,
in every theme.

And one theme's secondary text was under the readable floor from the day that
theme shipped. Also fixed. Both were caught by new tests that measure the
actual pixels on screen rather than trusting what the code says it painted,
which is why they surfaced now and not two years from now.

**Themes**

There's a fourth one, Flatline. It carries no meaning in colour at all, so
nothing is lost on a bad monitor, in sunlight, or if you're colour blind.
Warning banners and expired rows now use a marker and wording rather than
just a colour, in every theme.

You can also build your own from ten colours. It saves as a file you can send
to someone else.

**On Windows 10?** The Store build works on 22H2. The old "Store needs
Windows 11" note was out of date, sorry about that one.

Full notes: <https://github.com/estevanhernandez-stack-ed/ROROROblox/releases>

A lot of this release was under the floorboards, honestly. Rewiring how every
button and surface in the app gets its colours so the next few releases go
faster and break less. Not much to look at, but it's why the list above
exists.
```

---

## If you'd rather post something shorter

```
**RoRoRo v1.21 is on the Store.** If you installed from the Store you've been
on v1.15, so this is six releases at once. Hit "Get updates" in the Store app.

The big one: launching alts while Roblox is mid-update no longer sets every
client updating at the same moment. It holds the batch, updates once, then
launches the rest.

Also: Settings is a real page now instead of a text file you had to edit.
Close stopped being the brightest button in nine windows, next to Remove and
"Stop all Roblox instances". Muted accounts have a count and an unmute-all.
Games is back on the toolbar. There's a fourth theme, Flatline, that carries
no meaning in colour at all.

And two honest ones: every button outline in the app was far too faint in the
default theme since v1.1, and one theme's text has been under the readable
floor since the day it shipped. Both fixed, both caught by new tests that
measure real pixels instead of trusting the code.

On Windows 10? The Store build works on 22H2 — the old "needs Windows 11"
note was wrong.

Full notes: <https://github.com/estevanhernandez-stack-ed/ROROROblox/releases>
```
