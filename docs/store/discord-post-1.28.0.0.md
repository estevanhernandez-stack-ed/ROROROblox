# Clan Discord post — v1.28.0.0

> Post when the Store listing actually shows 1.28, not when certification clears — same rule as
> every post since 1.21. Setup.exe users get it within a day of the GitHub release regardless.
>
> **Metric alerts is deliberately NOT the lead, and is barely mentioned.** It is the headline of the
> release everywhere else, and it is the thing this clan actually asked for — watching your points
> rate during a battle instead of eyeballing it. But nothing in this package reports a number. A
> plugin has to, and the plugin is not out yet. A post that led with it would send twenty people to
> Settings to flip a switch that does nothing, and the next question would be "so what do I do
> now?" with no answer. The switch gets its post when the plugin ships and the pair works.
>
> **So the lead is the honest one: nothing you have to do.** For most of this clan v1.28 is the
> phone-alert setup getting easier and the Settings page stopping its worst habit. That is a
> maintenance release, and saying so plainly costs nothing — the people who set up phone alerts
> last month are exactly the people who hit both of these.
>
> **Pushover before ntfy, in that order.** Pushover is what most of the clan already runs (Este's
> ruling, 2026-09-04), so its change goes first even though the ntfy one is bigger. Leading with
> the QR code would open on the service fewer of them use.
>
> **"It lost my typing" is named as a bug, not softened.** Every box on the Alerts page held your
> edit until you clicked somewhere else, and the page has no Save button. Anyone who configured
> alerts and thought it had not saved was right. Owning that is better than describing a new
> feature.
>
> **The streamer-mode line is for a handful of people and stays anyway.** It is the only item here
> with a security shape: streamer mode used to mask account names while leaving a Discord webhook
> URL on screen, and a webhook URL reads off one paused frame.
>
> **What is deliberately NOT in here:** the report policy, the fence tests, the plugin RPC, the
> eighteen smoke rows, the manifest encoding bug. None of it is felt by anyone using the app.

---

```
**RoRoRo v1.28 is out** 🎉

Store: it'll arrive on its own, or open the Store app and hit "Get updates."
Setup.exe folks: it updates itself as usual.

Nothing you have to do this time. Two things got easier and one thing that
was quietly broken is fixed.

**Setting up phone alerts is less of a chore**

Pushover has two codes, they look alike, and they come from two different
pages — so there are now buttons that open the right page for each one.

If you use ntfy instead, the 33-character topic is a QR code now. Point your
phone's camera at it. It hides itself while streamer mode is on, because
that topic is the credential — anyone who scans it can read your alerts.

**Settings stopped losing your edits**

Every box on the Alerts page held whatever you typed until you clicked
somewhere else, and there is no Save button, so it looked like it hadn't
saved. If you set up alerts and thought it didn't take, you were right.
Enter saves now. Saved webhooks and phone keys also have a Remove button,
instead of reveal, select all, delete.

**If you stream: streamer mode covers your webhook now**

Turning it on used to mask your account names and leave a Discord webhook
URL sitting in plain view. It re-hides those too now, and asks before it
shows you one while you're live.

Setup.exe: https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest
```

---

## Hold this back until the plugin ships

Not for the v1.28 post. This is the clan-battle feature the clan asked for, and it needs the
reporter plugin alongside it or it is a switch that does nothing. Post it then, with a download.

```
**RoRoRo can watch your points rate during a battle**

You set the floor — points per minute, or per hour — and it tells you when
you drop under it. Toast on the PC, your own Discord channel, the clan
channel, or your phone.

It won't spam you: one alert per account per five minutes, same as every
other alert.
```
