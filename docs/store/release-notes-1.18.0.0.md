# RoRoRo v1.18.0.0 — release notes

A backbone release, and worth saying so plainly: there is no new feature to show off here. v1.16
built Settings as five pages and stopped, and this is the release that puts things on those pages.
If you have ever opened Settings looking for something, not found it, and closed it again, this one
is for you.

The Partner Center block that v1.15 and v1.17 carried is not here, because the Store submission is
still parked on purpose. The short list below is for the GitHub release body and the clan Discord
post, and it will do for a listing later if this ships to the Store.

## Short list, for the GitHub release and the Discord post

```
• The memory warning settings are in Settings now. "Alerts and memory" holds all four of them: whether the memory watch runs at all, how much RAM to keep free, how big one Roblox client can get before it warns you, and how far ahead you want the warning. Changing any of those meant editing settings.json in Notepad before this.
• Leave one of those boxes empty and the page tells you what RoRoRo picks for you on this PC, with the real numbers and how much RAM it found. Empty used to mean you had no way of knowing what was about to happen.
• Type a number outside the range and it tells you why it will not take it, and keeps the value you already had. It does not quietly swap your number for one it prefers.
• Careful mode for Squad Launch is on Settings > Startup. Until now the only way to change it was to start a squad launch. The toggle inside Squad Launch stays exactly where it is. They are one setting in two places.
• Settings > Alerts tells you how many accounts you have muted, and gives you one button to unmute all of them. Muting has been right-click-only since it shipped.
• Compact mode stays on after a restart.
• A theme that fails to save now says so, and still applies for the rest of the session. A theme file RoRoRo cannot read gets named, instead of just never appearing in the list.
• Settings reads differently. Group names are headings now, instead of every single setting sitting in its own box the same size as a group of ten.
• Close is no longer the brightest button on the screen in nine windows. Remove, Clear, and "Stop all Roblox instances" now look like what they do.
• Every setting on the page speaks the same way. A few of them used to talk about registry keys.
```

---

## Longer form — for the GitHub release and the clan Discord post

## The memory settings finally have a page

RoRoRo has watched Roblox eat your RAM since v1.12. It warns you when one client gets too big, and
it warns you when the whole PC is heading for trouble. Four settings control that, and until today
not one of them was in the app. The page they belong on has been called **"Alerts and memory"** the
entire time.

**All four are on that page now.** Whether the memory watch runs at all, how much RAM to leave free
for Windows and everything else, how big one client can get before you hear about it, and how many
minutes of warning you want before your clients would fill the PC.

**An empty box now tells you what it is doing.** Leave any of the number boxes blank and RoRoRo picks
a value based on how much RAM your PC has. It used to pick silently. Now the page says how much RAM
it found and exactly what each blank box works out to on your machine, so blank means "I know what
this is" rather than "I hope this is fine". If RoRoRo cannot read your RAM size at all, it says that
too, and shows you the numbers it falls back to.

**A number it will not take gets refused out loud.** Put a negative megabyte figure in and it names
the number, tells you the range it will accept, and leaves your old value alone. Nothing gets written
and nothing gets quietly rounded. A setting that changes your number without telling you is the same
problem as a setting you cannot find.

**One thing to know:** memory values take effect the next time you start RoRoRo. That is deliberate.
The watchdog works those numbers out once when the app starts, and having the Settings page work them
out a second time is how two parts of an app end up disagreeing about what your settings are.

## Careful mode, without starting a squad launch

Careful mode makes Squad Launch wait for each account to actually land before sending the next one.
Good setting. The only way to change it was to open Squad Launch and begin one.

**It is on Settings > Startup now**, next to the other launch setting that already lives there. The
toggle inside Squad Launch has not moved and is not going anywhere. It is one setting shown in two
places, and both of them read the current value every time they open, so they cannot drift apart.

## Alerts admits what it owns

If you muted an account by right-clicking it three weeks ago, nothing in the app ever mentioned it
again. **Settings > Alerts now says how many accounts are muted, with one button to unmute all of
them.** If you have none muted, the whole thing stays out of your way rather than showing you a zero.

The count reads your actual account rows, not a saved list, so an account you deleted while it was
muted cannot show up in the number as an account you no longer have.

## When something fails, it says so

Pick a theme and it applies immediately. If RoRoRo then fails to save your choice to disk, you used
to find out after a restart, when the old theme came back.

**Now it tells you, in the same spot on the Appearance page, and the theme still applies for the rest
of the session.** Nothing about your session is degraded because a file write failed. If a theme file
in your themes folder cannot be read, that gets named too, so a file you dropped in yesterday that
never showed up is no longer a mystery. One bad file does not stop the good ones loading.

A successful save stays quiet. A message that appears every time you save is noise, and you stop
reading it.

Small thing while we were in there: the "Open themes folder" tooltip used to tell you to restart the
app after adding a theme. It never needed a restart, and clicking between Settings pages does not
re-check the folder either. It now tells you the truth, which is to close Settings and open it again.

## Settings looks different

Every setting used to sit in its own box, the same size and weight as a box holding ten settings, so
"a group" and "one thing" looked identical. Group names are headings now, standing above their
settings instead of inside a box of their own. Fewer boxes, more space between groups, same settings.

This holds up in all four themes including Flatline, which was the point. The old grouping leaned on
a background shade that is barely visible in any theme we ship, so it was never really doing the job
anywhere.

Everything on the page also reads in one voice now. A couple of settings used to talk about registry
keys and a couple used to speak as "I", which is a strange thing for a checkbox to do.

## The brightest button is now the one that does something

Nine windows in RoRoRo have a Close button, and in five of them Close was the loudest, most filled-in
thing on the screen. The button you press to leave was competing with the button you opened the
window for. The other four already sat back, so this was a matter of picking the version that was
already half true. **All nine sit back now**, and the filled button is the thing the window exists to
do.

Three buttons went the other way. **Remove** on an account row, **Clear** in History, and **Stop all
Roblox instances** now carry a heavier outline and heavier text than anything else on their screens,
because all three destroy something. They do not use a warning colour to do it, on purpose. In one of
the four themes a magenta button and an ordinary one are literally the same colour, and in another it
comes out a dim grey that reads as switched off rather than dangerous. A rank that disappears in a
theme you can pick is not a rank, so this one is carried in weight instead.

## Known limits, so you do not find them yourself

- **The memory numbers apply at next start**, as above.
- **Appearance has an "ask me again" option for the theme prompt**, for anyone who dismissed it in the
  first ten seconds of a launch. It only has something to offer if you built your own theme. All four
  built-in themes are never asked about, by design, so if you are on one of those it will tell you
  there is nothing to choose. That is correct behaviour and not a broken button.
- **Muting accounts is still right-click-only.** Settings can now tell you the count and unmute all of
  them. Muting one specific account still happens on its row.

## Getting it

Nothing to reconfigure. Your accounts, themes, private servers, tags, and settings all carry over.

How this one reaches you depends on how it gets published. v1.17 went out as a pre-release, and the
updater inside your copy deliberately skips pre-releases, so if you are sitting on an older build and
have not been offered anything, that is why and not a bug. If v1.18 goes out the same way, grab it
from the releases page. If it goes out as a normal release, your copy will offer it on its own.
