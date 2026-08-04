# Smoke sheet — Discord alerts + presence logging

**Build:** `test/discord-combined` (PR #81 + PR #82 merged locally, not on main)
**Run:** `src\ROROROblox.App\bin\Release\net10.0-windows\ROROROblox.App.exe`
**Log:** `%LOCALAPPDATA%\ROROROblox\logs\rororoblox-<date>.log`
**Tests at build time:** 1253 unit + 18 integration, green.

> **Check which binary you're running.** The Store v1.3.4 install and this dev build both report
> assembly version 1.3.4 in the log. Confirm with:
> `Get-Process ROROROblox.App | Select-Object Path`
> It must say `bin\Release`. If it says anything else, you're smoking the wrong app.

---

## Part 1 — Alerts setup (no Roblox needed)

The whole path a clan member walks. Open **Preferences → Alerts**.

### 1. The field names what you paste

Paste each of these into the webhook box and **click away** (it commits on focus-loss, not on
keystroke — a URL is pasted whole, and validating every character flashes four rejections at
anyone typing one by hand).

| Paste | Expected |
|---|---|
| `https://discord.gg/abc123` | "That's a server invite... Server Settings → Integrations → Webhooks" |
| `https://discord.com/channels/123/456` | "That's a link to the channel, not a webhook." |
| `banana` | "That doesn't look like a webhook URL." |
| A bot-token-shaped string | "That looks like a bot token — don't share that anywhere" |

**Also check:** none of those messages echo back what you pasted. That string gets screenshotted
into clan channels when someone asks for help.

### 2. A real webhook names its channel

**Making one, if you haven't:**

1. In Discord, pick a server you own — or make one: **+** on the left edge → **Create My Own** →
   skip the questions. A server with only you in it is fine, free, and private.
2. Right-click the server name → **Server Settings**.
3. **Integrations** → **Webhooks** → **New Webhook**.
4. Click the webhook it created, pick which channel it posts to, then **Copy Webhook URL**.

That URL is the credential — anyone holding it can post to that channel forever. It's stored
DPAPI-encrypted on this PC and never logged, but don't paste it in chat.

To get the alerts **on your phone**, just have Discord installed there and be in that server.
Nothing else to set up — a webhook post is a normal message, so it notifies like any other.

Now paste it into the box and click away.

- Expect: **"Posts to #your-channel in Your Server."**
- This is the check that catches a clan webhook pasted into the personal slot *before* something
  private lands in a channel forty people read.

### 3. Send test

Click **Send test**.

- Expect the status line: "Sent. Check #your-channel — if it's there, you're done."
- **Then actually look in Discord.** The message should read
  **RoRoRo test / If you can read this, alerts work.**
- This is the only end-to-end coverage this feature has. There is no automated test that a real
  webhook post arrives.

### 4. The status line tells the truth

Set **An account drops out → My channel (phone)**, then clear the webhook box and click away.

- Expect: *"You've routed alerts to a Discord channel but haven't pasted a webhook, so they'll only
  show on this PC — which won't help when you're away from it."*

Now set both dropdowns to **Desktop only**.

- Expect: *"Desktop only. You'll see these at the PC, but nothing will reach your phone."*

Both dropdowns to **Off**:

- Expect: *"No alerts yet."*

### 5. The no-server walkthrough

Click **I don't have a Discord server**.

- Expect a dialog with the three-click server creation, then the webhook steps.
- Read it as if you'd never made one. This is the step every other guide skips.

### 6. Per-account mute

Right-click an account row → **Mute Discord alerts**.

- Expect the item to become **✓ Muted — no Discord alerts**.
- Close and reopen RoRoRo → the mute should still be showing on that row.
- Reopen Preferences → **your webhook URL and routing should still be there.** (The mute write
  rewrites the whole settings record; this is the check that it didn't eat anything.)

---

## Part 2 — Alerts firing (needs Roblox)

### 7. Dropped out

With alerts routed to your channel, launch an account, let it get in-game, then close that client.

- Expect one Discord message naming the account and the game.
- **Timing note:** the alert fires on the *presence-confirmed* close — both the process being gone
  and presence reporting not-in-game. That's deliberate (it dodges the anti-multilaunch ghost,
  where Roblox kills the pid and respawns the client), but it means the alert lands on the next
  presence poll, not instantly.
- Close a second account within five minutes → **no second message for the same account**, but a
  different account should still alert. That's the per-account cooldown.

### 8. Memory warning — the least-tested path, and the thresholds are wrong for this rig

There are **two** ways a crossing fires. Neither is reachable by just playing normally on a 47 GB
machine, which is why nobody has ever seen this alert.

**Trigger A — per-client cap.** One client's private bytes exceed the cap. The cap defaults to
**35% of installed RAM, floored at 4 GB** (`MemoryDefaults.CapMb`). On this machine:

> 47.1 GB installed → cap = **16,886 MB (16.5 GB) for a single Roblox client**

No Roblox client will ever reach that. **On a high-RAM machine this trigger is effectively dead.**

**Trigger B — projection.** Available RAM is falling fast enough to hit the reserve within
`ProjectionWarnMinutes` (**120 minutes**, default). The reserve is 8% of RAM clamped to
[1 GB, 4 GB] — **4 GB here**. So: eight clients growing steadily, with free memory heading toward
4 GB inside two hours. Reachable, but it takes a long real session.

**To actually test this today,** lower the cap by hand. There is no Preferences UI for it yet.

1. Close RoRoRo.
2. Edit `%LOCALAPPDATA%\ROROROblox\settings.json` and set `"MemoryCapMb": 1500`.
3. Start RoRoRo, launch two or three clients, let them load in.

Expect, once a client passes 1.5 GB:

- A tray memory badge and the row's warning chip.
- **One coalesced Discord message** if several accounts cross in the same sweep — several accounts
  crossing at once must be *one* message listing them, not one message each. That's the check.
- `memory cap crossed: account ... at NNNN MB (cap 1500 MB)` in the log.

Put the setting back to `null` afterward.

**Worth deciding separately:** a 35%-of-RAM cap means the per-client trigger never fires for anyone
with a big machine, and fires readily for someone on 16 GB (cap 5.6 GB). The people most likely to
need this alert are the ones least likely to get it. That's a threshold conversation, not a bug —
but it's the reason this path has stayed unobserved.

---

## Part 3 — Presence logging (PR #81)

The point of this half: a working presence and a dead pipe used to produce identical logs (nothing
at all). That cost three separate debugging sessions.

### 9. Connect and push are visible

Turn presence on, then open the log.

- Expect: `Discord IPC initialized for application ...`, `Discord URI scheme registered.`,
  `Discord Join subscription active.`, `Connected to Discord.`
- Expect a push line: `Discord presence → 3 accounts in one server | Pet Simulator 99! | party 3/8, join secret attached`
- **Check the secret is not in the log.** It should say *"join secret attached"* and never the
  actual secret — a private-server secret embeds a real private-server code, and logs get pasted
  into Discord.
- Repeat pushes with an unchanged roster drop to `Debug` so the file stays readable.

### 10. The Discord-restart question

This is the open one. Restart Discord while RoRoRo keeps running, and **note the wall-clock time**.

- Expect in the log: `Discord IPC pipe closed: <reason>` → `Discord connection dropped; retrying
  with backoff.` → then, within roughly a minute, `Reconnected to Discord; republishing presence
  from the current roster.` followed by a push line.
- **If the reconnect line appears** — the earlier "presence stays dead until I toggle it" was the
  backoff (Lachee retries at 500ms→60s), and the fix is a UI honesty problem, not a reconnect bug.
- **If it never appears** — that's a real defect and the log now proves it. Send me the timestamps.

Don't toggle presence during this test. Toggling forces a fresh connect and destroys the evidence.

---

## What to send back

Whatever failed, plus the log file. For step 10, the timestamps either way — that one is a live
question I can't answer from the code.
