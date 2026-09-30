# Notes for certification — reviewer letter (v1.32.0.0)

> Paste the fenced block below the `---` marker — everything from `Hello reviewer,` to
> `626 Labs LLC`, not the backticks — into Partner Center → your app → **Submission options** →
> **Notes for certification**. The same text, paste-ready, is `reviewer-letter-1.32.0.0.paste.txt`.
> **Partner Center only.** `submit_write.py` never sends `notesForCertification`, so confirm it on the
> Partner Center screen or not at all.
>
> **A single-version delta.** Certification last saw v1.31.0.0.
>
> **The letter leads with the one delta that could be misread: RoRoRo now relaunches a client on
> its own.** Opt-in, per account, rate-capped, never the main account. A reviewer who meets "rejoins
> after Roblox's verification page" in the release notes could read it as getting around a bot
> check. So section 2 says what happens before they have to ask: the page is never read, clicked,
> answered or dismissed; the client is closed only after Roblox itself has taken the account out of
> the game, which RoRoRo learns from Roblox's own presence API, not the screen; the relaunch is the
> same launch the user's Launch button makes. That is the v1.14 letter's 10.1.1 answer ("a
> documented Roblox launch endpoint, in the same way a user would") applied to a new caller.
>
> **Join via friend is not new.** The per-account follow setting was disclosed with trust-aware
> Squad Launch (v1.11 letter, "per-account follow settings"). 1.32 makes every launch path honour it,
> so the letter gives it one line.
>
> **Every claim was verified against `git diff v1.31.0.0..main`, not assumed:**
>
> - `-- src/ROROROblox.App/Package.appxmanifest` is **empty**. The Store build patches `Version`
>   only.
> - **One `.proto` change**, additive: `LaunchResult` gains `string reason_code = 4`. The contract
>   version string stays "1.0". `PluginHostService.cs` changes only to copy that field (6 lines).
>   No file matching `RpcMethodCapabilityMap`, `PluginCapability` or consent is in the diff.
> - **No input synthesis added:** no added line under `src/` contains `SendInput`, `keybd_event`,
>   `mouse_event`, `SetWindowsHookEx`, `PostMessage`, `SendMessage`, `WriteProcessMemory`,
>   `OpenProcess`, `DllImport` or `LibraryImport`.
> - **No new URL:** the only added `http` strings in production code are the two XAML namespaces.
> - **One new process action, and it reuses an old one:** the added `_instanceStopper.StopAccount(id)`
>   in auto-rejoin is `RobloxInstanceStopper.StopAccount`, the routine **Recycle** has used since
>   v1.12 (`AccountRecycler`). It ends the tracked client's process at once; it is NOT the row Stop
>   button's graceful close-then-grace sequence (`ClientStopSequence`), and an earlier draft of this
>   letter wrongly said it was. It acts only on a client RoRoRo is tracking for that saved account.
>   The v1.12 letter's answer on process termination ("ordinary management of processes the user has
>   already delegated") holds, with one honest difference said out loud: Recycle is one click each
>   time, and auto-rejoin is one opt-in per account.
> - **The signal is Roblox's presence API**, which the app has polled for every saved account since
>   v1.5 (`PresenceService`, 25 s). No new endpoint and no screen reading.
> - **Caps:** `AutoRejoinMonitor`, 3 minutes out of game, 5 minutes after a fresh launch, at most 3
>   per account per hour, then the option is switched off and the user is told.
>
> **Section 6 is honest that a reviewer cannot easily see the feature fire.** It needs a signed-in
> Roblox account idle for 20 minutes. What a reviewer can see in a minute is the option itself and
> the dialog.

---

```
Hello reviewer,

RoRoRo is a launcher that runs several Roblox clients side by side, each signed
in as a different account the user saved. This is v1.32.0.0. Certification last
saw v1.31.0.0, so this is one version's delta.

1. WHAT CHANGED

Two things, both in how RoRoRo launches the user's own accounts.

  - "Rejoin if it drops out", an option on each account's right-click menu.
    Off by default and never offered for the account marked as the user's
    main. When it is on and that account's Roblox client is still open but the
    account has been out of the game for 3 minutes, RoRoRo closes that client
    and launches the account again.
  - "Join via friend", the per-account follow setting disclosed with Squad
    Launch in v1.11, is now honoured by every launch path, not only Squad
    Launch. When the account it follows is not in a game, the app asks the
    user instead of launching.

2. WHAT THE REJOIN DOES AND NEVER DOES

  - It learns that an account is out of the game from Roblox's presence API,
    which the app has polled for every saved account since v1.5. It does not
    read the screen.
  - It never reads, clicks, answers or dismisses anything inside the Roblox
    client, including Roblox's verification page. If a client shows that
    page, nothing happens until Roblox itself takes the account out of the
    game; then the client is closed like any other drop.
  - Closing uses the same routine as Recycle (disclosed in the v1.12 letter):
    it ends that account's Roblox client process. It acts only on a client
    the app is tracking for that saved account. The difference from Recycle
    is that the user opts in once per account instead of clicking each time.
  - The relaunch is the same launch the user's Launch button makes, through
    the documented Roblox launch path (see the v1.14 letter, 10.1.1).
  - At most 3 rejoins per account per hour. After that the option switches
    itself off for that account and the user is told. A stop by the user or
    by a plugin always wins.

3. THE MANIFEST DID NOT MOVE

Package.appxmanifest differs from v1.31.0.0 by the Version attribute only.
Same runFullTrust capability, languages, protocols and startup task.

4. THE PLUGIN INTERFACE GREW BY ONE FIELD

plugin_contract.proto gains one additive field: LaunchResult.reason_code, so
a plugin can tell why a launch was refused. The contract version is unchanged
and no capability, consent or RPC was added. A plugin built for v1.31 runs
untouched.

5. NO INPUT SYNTHESIS, NO NEW HOST

No added line calls SendInput, keybd_event, mouse_event, SetWindowsHookEx,
PostMessage, SendMessage, WriteProcessMemory or OpenProcess, and no DllImport
was added. No new network address: the only new alert ("auto-rejoin paused")
goes to the desktop by default, or to the Discord webhook or push service the
user already set up.

6. WHAT YOU CAN EXERCISE

Save two accounts (the first one saved becomes the main). Right-click the
second to see "Rejoin if it drops out"; it is absent on the main. Tick "Join
via friend" on the second and press its Launch while the main is not in a
game: a "Join through your main" dialog asks, with Cancel as the default.
Seeing a rejoin fire takes a signed-in account left idle about 20 minutes
until Roblox disconnects it.

7. UNCHANGED, RESTATED

  - The app synthesizes no input and injects into no process.
  - Sign-in happens inside Roblox's own page in a WebView2 frame; only the
    session cookie is captured, encrypted to the Windows user.
  - No telemetry and no analytics.

Thank you for your time.

626 Labs LLC
```
