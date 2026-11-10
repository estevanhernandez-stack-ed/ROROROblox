# Reply drafts: four Store reviews and one issue

Drafted 2026-11-10 for the owner to post. Nothing here has been sent. Store replies are public,
capped at 1,000 characters, and written in the reviewer's language. Each stays inside the standing
lines: no "safe", no "undetectable", no promise Roblox approves.

A sentence in **[brackets]** is true only once the named fix has shipped. Drop it or keep it
accordingly.

## Store reviews

### 2026-11-07, 3 stars, US, v1.35: "launching issue"

> Whenever I launch an account, it says that i cant launch it because I don't have an app launch
> can launch this link.

```
Thanks for reporting this, and sorry it stopped you at the first launch. That Windows message means
this PC has no Roblox that RoRoRo can start. It usually happens when Roblox was installed from the
Microsoft Store: that edition can't be launched by other apps. The fix is to install Roblox from
roblox.com/download. Both can stay installed, and your accounts in RoRoRo are untouched. After that,
Launch As works. [The next update catches this and tells you in the app instead of leaving you with
a Windows message.] If it still fails, email support@626labs.dev and we'll sort it out.
```

### 2026-10-31, 5 stars, US, v1.35: "Pretty neat software"

> ... I have *some* doubts as to whether or not this is a Trojan based on the reviews on the store
> ... can there be an option to modify FPS caps customly? ... (One is 75hz and the other is 180hz)

```
Thank you, and the doubt is fair, so here is what you can check yourself. RoRoRo is open source:
the code is on GitHub under 626 Labs. You sign in on Roblox's own page, so your password never
reaches the app. The saved session is encrypted on your PC with Windows' own protection and is sent
only to Roblox. Nothing is injected into the Roblox client, and there is no telemetry. On FPS caps:
you're right, 75 and 180 are missing from the list. [Both are in the next update, along with a
custom value.] Thanks for taking the time to write this.
```

### 2026-11-04, 5 stars, BR, v1.35: "E MUITO UTIL"

> simplesmente umas das coisas mais perfeitas pra usar com o roblox no slap battles também e muito
> útil ... precisa de vários jogadores pra uma certa badge e com isso fica muito mais fácil

```
Muito obrigado! Ficamos felizes que o RoRoRo esteja ajudando no Slap Battles. Se você ainda não
usou, o Squad Launch coloca todas as suas contas no mesmo servidor privado de uma vez, o que ajuda
bastante quando uma badge pede vários jogadores. Bom jogo!
```

### 2026-09-23, 5 stars, US, v1.30: "itwork even potato loptop can handle 4 account"

```
Four accounts on a laptop is exactly what it's for. If the laptop ever starts to struggle, set a low
FPS cap on the alts and leave your main alone. Thanks for the review!
```

## GitHub issue #112: "Roblox Verification"

Opened 2026-08-14 with a support bundle. A second person asked for an update on 2026-09-03. Never
answered.

```
Sorry for the long silence on this one. Both of you deserved an answer in August.

**What is happening.** The "verify you're not a robot" page comes from Roblox, not from RoRoRo.
Roblox sometimes flags an account, or a connection, and then challenges every join. Removing the
account and adding it again does not clear it, because the flag is on Roblox's side. That matches
what you saw.

**What helps today.**

- **Join by following.** Launch one account normally, then have the flagged account follow it into
  the game: right-click the flagged account and turn on joining via a friend. In our own testing
  that got a flagged account in when a direct join kept looping.
- **Find out whether it is the account or the connection.** Launch a different account from the same
  PC. If that one joins cleanly, the flag is on the account. If every account is challenged, it is
  more likely the connection.
- **Update to 1.36.** An account in this state now shows a **Needs verification** badge, and one
  click opens Roblox as that account so you can do the check there instead of mid-launch.

**What we can't do.** RoRoRo can't solve or skip the check, and won't try to.

If the badge shows the wrong thing for you, or following doesn't get you in, a fresh Diagnostics
bundle from 1.36 would tell us a lot.
```

## Notes for whoever posts these

- The Microsoft Store edition sentence rests on a measurement: that edition's package declares the
  `roblox` protocol only, read from version 2.699.877.0 on 2026-11-10.
- "In our own testing" in the #112 reply refers to the 2026 join-challenge investigation
  (`docs/investigations/2026-05-07-account-bot-challenge.md` and the follow path). Re-read it before
  posting if the wording feels stronger than what was seen.
- The menu wording in the #112 reply ("joining via a friend") was written from the code's name for
  the setting, not read off the screen. Match it to the label the app shows before posting.
- #260 (invisible cursor) already has a reply. A second reporter added detail on 2026-11-04: newest
  Windows 11, 100% scale, 1080p. The main window is the only `FluentWindow` in the app, which is
  worth testing as the cause before replying again.
