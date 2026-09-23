# Notes for certification — reviewer letter (v1.31.0.0)

> Paste the fenced block below the `---` marker — everything from `Hello reviewer,` to
> `626 Labs LLC`, not the backticks — into Partner Center → your app → **Submission options** →
> **Notes for certification**. The same text, paste-ready, is `reviewer-letter-1.31.0.0.paste.txt`.
> **Partner Center only.** `submit_write.py` never sends `notesForCertification`, and the field read
> back empty through the API on v1.28 even after the letter was pasted, so confirm it on the Partner
> Center screen or not at all.
>
> **A single-version delta.** Certification last saw v1.30.0.0, which certified and published on
> 2026-09-21.
>
> **The letter leads with the one delta and states it in full, because it is new remote content the
> Store binary reads.** A reviewer who finds a server-fetched list
> on their own, with the v1.4 letter's "never reads a curated list from a server" in the file, would
> reasonably ask. So section 2 says what the file can and cannot do before they have to: text and
> https links only, signed with the key already pinned, buttons from a fixed compiled set that only
> navigate inside RoRoRo, no new host, nothing about the user sent. The v1.4 promise is quoted and
> answered in one line — it was about plugins, and this list names none.
>
> **Every claim in the letter was verified against the diff before writing, not assumed:**
>
> - `git diff v1.30.0.0..main -- src/ROROROblox.App/Package.appxmanifest` is **empty**: the manifest
>   is patched during the Store build, not at a commit, so the uploaded package differs from v1.30's
>   by the `Version` attribute alone. (Not re-read from the working tree: a build is patching it as
>   this is written.)
> - **No `.proto` file appears in the diff**, and no file matching `RpcMethodCapabilityMap`,
>   `PluginCapability`, `PluginHostService` or consent. A plugin built for v1.30 runs untouched.
> - **No new host.** The one added production URL across `src/` and `tools/` is
>   `KnownIssuesFeed.FeedUrl`, `https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/known-issues.json`
>   (+ `.sig`) — the same `releases/latest/download/` path as `RobloxCompatChecker`'s
>   `roblox-compat.json`. Every other added URL is a `devforum.roblox.com` link inside a test, or a
>   XAML namespace.
> - **Signed and bounded:** `KnownIssuesFeed` verifies the raw bytes before parsing, refuses over
>   `MaxDocumentBytes` (256 KB) before fetching the signature, and re-verifies the cached copy on load.
>   The key is `RobloxCompatSigningKey`, already pinned.
> - **https-only links:** `KnownIssuesParser` line 159 rejects any link whose scheme is not https.
>   Links open through `IShellOpener` on click only; the URL is shown beside the label.
> - **Buttons navigate only:** `KnownIssueFeatureRoutes.For` maps `memory-watchdog` →
>   `MemorySettings` and `fps-caps`/`recycle` → `MainWindow`; anything else → `None` (text, no button).
>   `GoToKnownIssueFeature` in `App.xaml.cs` handles exactly those two.
> - **Entry text is plain text:** every entry field binds to a `TextBlock.Text`; there is no markup
>   path.
> - **No input synthesis added.** No added line in `src/` or `tools/` contains `SendInput`,
>   `keybd_event`, `mouse_event`, `DllImport`, `LibraryImport`, `SetWindowsHookEx`, `PostMessage`,
>   `SendMessage`, `WriteProcessMemory` or `OpenProcess`.
> - **Section 6 is exercisable because the file is already live:** `known-issues.json` + `.sig` have
>   been attached to the v1.30.0.0 release since 2026-09-23, and `release.yml` attaches them to the
>   v1.31 tag. The serious entry has no `robloxVersions`, so it applies on any PC, with or without
>   Roblox installed.
>
> **Not verified, and not claimed anywhere in the letter:** that the notice renders on certification
> hardware specifically. It needs the network at first launch; section 6 says "with a network
> connection" for that reason.
>
> **Length:** 3,240 characters, shorter than v1.30's 3,927, under the gotcha-row rule (deltas in
> full, the rest one line).

---

```
Hello reviewer,

RoRoRo is a launcher that runs several Roblox clients side by side, each signed
in as a different account the user saved. This is v1.31.0.0. Certification last
saw v1.30.0.0, so this is one version's delta.

1. WHAT CHANGED

One addition: a read-only page, Tools > Known Roblox issues, listing current
problems in Roblox itself, each with a workaround and, where the app has a
relevant feature, a button that opens that feature. A serious entry also shows
a one-time notice at the top of the main window, which the user can close.

2. WHERE THE LIST COMES FROM

The list is a small text file fetched from the same GitHub release address the
app already uses for its signed compatibility file. No new host.

  - Fetched at startup and every four hours. A plain download: no account
    data, no identifier, nothing about the user or the PC is sent.
  - Signed. The signature is checked against the public key already built
    into the app before a byte is read; a file that fails is ignored and the
    last verified copy stays. Over 256 KB is refused.
  - Text only: titles, sentences and https links. A non-https link is
    refused. A link opens in the default browser only when the user clicks
    it, with the full address shown beside it.
  - The buttons are a fixed set compiled into the app. An entry names one of
    three keys; each leads to one of two places inside RoRoRo (the memory
    setting, or the main window). An unknown key shows text with no button.
    The file cannot add an action, download anything, run code, change a
    setting or launch Roblox.

The v1.4 letter said the app never reads a curated list of plugins from a
server. That still holds: this list names no plugin and installs nothing.

3. THE MANIFEST DID NOT MOVE

Package.appxmanifest differs from v1.30.0.0 by the Version attribute only.
Same runFullTrust capability, same languages, same protocol declarations, same
startup task. No new capability is requested.

4. THE PLUGIN INTERFACE DID NOT MOVE

plugin_contract.proto does not appear in this diff, nor does any file
governing plugin capabilities or consent. A plugin built for v1.30 runs
untouched.

5. NO INPUT SYNTHESIS ADDED

No added line calls SendInput, keybd_event, mouse_event, SetWindowsHookEx,
PostMessage, SendMessage, WriteProcessMemory or OpenProcess, and no new
DllImport was added.

6. WHAT YOU CAN EXERCISE

Unlike the last three versions, this one is visible on a fresh install with a
network connection: open Tools > Known Roblox issues. Two entries are live. One
is marked serious, so the notice appears at the top of the main window shortly
after first launch; "See known issues" opens the page, and closing it keeps it
closed. Page text is in all seven interface languages; the entries are English,
and a non-English interface says so under the page title.

7. UNCHANGED, RESTATED

  - The app synthesizes no input and injects into no process.
  - Sign-in happens inside Roblox's own page in a WebView2 frame; only the
    session cookie is captured, encrypted to the Windows user.
  - No telemetry and no analytics.
  - Optional alerts go only to a Discord webhook or push service the user
    sets up themselves.

Thank you for your time.

626 Labs LLC
```
