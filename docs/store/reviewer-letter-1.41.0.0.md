# Notes for certification — reviewer letter (v1.41.0.0)

> **PREPARED 2026-11-26 on `cart/v141-item13`, NOT SHIPPED.** Drafted against the v1.41 spec, with the
> cycle's items 9 to 12 still being built. Paste the block below the `---` marker, from the greeting to
> `support@626labs.dev`, into Partner Center. The same text, paste-ready and already plain, is
> `reviewer-letter-1.41.0.0.paste.txt`. **Partner Center only.** `submit_write.py` never sends
> `notesForCertification`. The box is on the product-level **Additional Testing Information** page
> and holds the v1.40.0.0 letter: select everything and replace it.
>
> **Before it is pasted, re-check four things against the tagged build.**
> 1. **Section 3's measurements are from `acfb0af`, not the release.** At `acfb0af`, against
>    `v1.40.0.0`: `Package.appxmanifest` diff empty; `*.proto` and `*NativeMethods.txt` diffs empty;
>    added lines under `src/ROROROblox.App` and `src/ROROROblox.Core` (tests excluded) matching the
>    ten native-call names: 0; added http(s) addresses: `api.status.io`, `create.roblox.com`,
>    `games.roblox.com`, `status.roblox.com`; added lines touching the registry: 0. Re-run the
>    commands in `submission-packet-1.41.0.0.md` section 7 on the release branch and change the
>    date and any number that moved.
>    **Re-run 2026-11-27 on `release/v1.41.0.0` (main `b719edd` + the stamp): every result above
>    holds; the manifest diff is the Version attribute only. Date in the letter moved to 2026-11-27.**
> 2. **The switch's label** is quoted as `Show Roblox's status and game activity` (spec assumption 32).
>    Item 9 owns the string (`SettingsPage_RobloxFeeds*`); match it.
> 3. **No live run is claimed.** Section 4 says what a reviewer can see and claims no live run. If the
>    cycle's live smoke passes (`docs/store/smoke-2026-11-26-roblox-feeds.md`), add one sentence in
>    the style of v1.40's "Run live on development builds" paragraph, and no more.
> 4. **The two links** are opened by the shell on a click (items 7 and 9 draw them). Confirm both open
>    in the browser and that the app fetches neither.
>
> **What the letter does not claim.** It does not say the false hold was seen in the field: the Neb log
> search (spec section 20) has not come back, and the fix is worded "could be held". It does not say a
> Roblox incident was observed: none has been, and the letter says the parser is tested against other
> pages' recorded answers.
>
> **The three things new at the boundary, and the second section leads with them:** one new host with
> a default-on call, an existing request that now also runs while the window is open, and one existing
> endpoint asked for a new purpose. A reviewer reading the diff would stop at each. Length 4,731
> characters, inside what Partner Center has taken (v1.40: 4,983).

---

Hello reviewer,

RoRoRo is a launcher that runs several Roblox clients side by side, each signed
in as a different account the user saved. This is v1.41.0.0. Certification
last saw v1.40.0.0, so this is one version's delta: a Roblox status line, saved
games that show their player count, and a fix to a false "Roblox is updating"
hold, which together add one new network host.

1. WHAT CHANGED

- NEW: one notice line in the main window about Roblox itself. It says when
  Roblox has an update waiting (a statement: no launch waits on it or is
  blocked by it), or when Roblox's public status page reports a problem on
  the rows that matter for launching, with a link to that page.
- NEW: saved games in the Library and in an open account panel show how many
  people are playing and when the game last changed.
- NEW: one Settings checkbox, "Show Roblox's status and game activity", on by
  default. Off stops the status read and the player-count reads at once.
- FIXED: the client-version check compared two spellings of the same version
  ("0, 748, 0, 7481526" and "0.748.0.7481526") as text, so a batch of two or
  more accounts could be held behind the first with "Roblox is updating" when
  Roblox was already current. Versions are now compared as versions.

2. WHAT A CODE REVIEW WOULD FIND

One new host: api.status.io. Status.io is a third-party service that hosts
Roblox's public status page (status.roblox.com). It is not Roblox, GitHub or
Microsoft. The app sends one anonymous GET for Roblox's page and reads only
numeric status codes and ids from the answer. No text from the answer is
shown, stored or logged: the service only chooses which of the app's own
sentences appears. Nothing about the user is sent: no cookie, no account, no
identifier. The request carries the app's usual User-Agent, RORORO/<version>,
and nothing else the app adds. It runs only while the main window is open:
first read 30 seconds after the window is shown, then about every ten minutes,
never while the app is in the tray, never at startup. It is on by default; the
Settings checkbox above turns it off and then no request goes to that host.
Any failure (no network, a timeout, a bad answer) shows nothing and delays
nothing. No Roblox incident has occurred while we built this, so the parser is
tested against recorded real answers from other Status.io pages, and Roblox's
own page was captured twice, both "Operational". PRIVACY.md has a row:
https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/PRIVACY.md

Two existing hosts, new use, both read-only and cookie-free:
clientsettingscdn.roblox.com, the client-version request the app already made
before a batch launch, now also runs about every 30 minutes while the main
window is open, to show the update notice. It is not behind the checkbox.
games.roblox.com/v1/games, a documented public endpoint the app already calls,
is asked once for the saved games on screen (game ids only) while the Library
or an account panel is open, for the player count and the last-updated date.

Two web addresses are opened in the user's browser when clicked, and the app
fetches neither: Roblox's release notes (create.roblox.com/docs/release-notes)
and status.roblox.com.

No launch of any kind waits on any of these reads.

3. NOTHING ELSE MOVED

Measured against the v1.40.0.0 tag on 2026-11-27, not assumed:

  - Package.appxmanifest: no change but the Version attribute. No new
    capability.
  - plugin_contract.proto and both NativeMethods.txt: unchanged. No capability,
    consent prompt or RPC.
  - Network: the hosts above. In added lines of shipped source the only web
    addresses are api.status.io, games.roblox.com, create.roblox.com and
    status.roblox.com.
  - No input synthesis and no new native import. In every added line of shipped
    source the count of SendInput, keybd_event, mouse_event, SetWindowsHookEx,
    SendMessage, PostMessage, WriteProcessMemory, OpenProcess, DllImport and
    LibraryImport is zero.
  - Registry: no new key or value.
  - Text: new strings in English and the six translated languages.

The app still launches the official Roblox client unmodified and injects into
nothing. It is not affiliated with, endorsed by, or sponsored by Roblox
Corporation.

4. HOW TO SEE IT

The status line shows only while Roblox's status page reports a problem, and
the update notice only while Roblox has an update waiting, so no line is the
expected state when you look.

Saved-game counts: add one account, open the Library, and a saved game shows
"N playing" and an updated date. In Settings, clear the checkbox: the counts
and the status line disappear and no request goes to api.status.io.

Thank you,
626 Labs LLC
support@626labs.dev
