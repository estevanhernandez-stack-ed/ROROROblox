# Notes for certification — reviewer letter (v1.30.0.0)

> Paste the fenced block below the `---` marker — everything from `Hello reviewer,` to
> `626 Labs LLC`, not the backticks — into Partner Center → your app → **Submission options** →
> **Notes for certification**. **Partner Center only.** `submit_write.py` never sends
> `notesForCertification`, and the field read back empty through the API on v1.28 even after the
> letter was pasted, so confirm it on the Partner Center screen or not at all.
>
> **A single-version delta.** Certification last saw v1.29.0.0, which certified and published on
> 2026-09-16.
>
> **The letter leads with the direction of travel, because that is the finding.** Every behaviour
> change in this release makes the app do LESS: alerts fire once instead of repeatedly, and a
> download that used to fail gets a longer budget rather than a new destination. The one addition —
> an optional second alert when a number comes back over its line — is off unless the user ticks it
> and goes to destinations they already chose. Nothing widens.
>
> **Every claim in the letter was verified against the diff before writing, not assumed:**
>
> - `git diff v1.29.0.0..HEAD -- src/ROROROblox.App/Package.appxmanifest` shows the **version
>   attribute and nothing else**. Same `runFullTrust` capability, same declared languages, same
>   protocol declarations, same startup task. (The attribute reads `1.28.0.0` at the v1.29.0.0 tag
>   because the manifest is patched during the Store build rather than at the bump commit; the
>   package uploaded for v1.29 carried `1.29.0.0`. The diff is still one attribute.)
> - **`plugin_contract.proto` does not appear in the diff at all.** No field, no field number, no
>   message, no RPC — not even a comment. A plugin built against v1.28 or v1.29 runs untouched.
> - **No file** matching `RpcMethodCapabilityMap`, `PluginCapability`, `PluginHostService` or
>   consent appears in the diff. No new capability, no consent change.
> - **No new host.** Every added line across `src/` and `tools/` containing a URL, a hostname or an
>   `HttpClient` is in a TEST: `new HttpClient(_http)` and `using var stock = new HttpClient()` in
>   `PluginInstallerTests`, and `https://example.invalid/plugin/` as that test's unreachable URL.
>   The one production change to an `HttpClient` sets `Timeout` on the client that was already
>   registered.
> - **No input synthesis added.** No added line in `src/` or `tools/` contains `SendInput`,
>   `keybd_event`, `mouse_event`, `DllImport`, `SetWindowsHookEx`, `PostMessage`, `SendMessage`,
>   `WriteProcessMemory` or `OpenProcess`.
> - **No `.xaml` and no `.resx` changed**, so nothing new appears in the interface and no UI string
>   moved. The new alert wording is composed in Core, which is English for every language — the
>   same limitation v1.29's what's-new block disclosed.
>
> **Not verified, and not claimed anywhere in the letter:** nothing. Every statement above maps to a
> command that was run against this diff.
>
> **Section 5 says plainly what a reviewer cannot exercise**, for the same reason it did at v1.28
> and v1.29: the alert path needs a plugin that reports a number, and this package ships none.

---

```
Hello reviewer,

RoRoRo is a launcher that runs several Roblox clients side by side, each signed
in as a different account the user saved. This is v1.30.0.0. Certification last
saw v1.29.0.0, so this is one version's delta and a small one.

1. WHAT CHANGED, AND WHICH WAY IT POINTS

Every behaviour change in this release makes the app do less, not more.

  a) Alerts fire once, on the change, instead of repeatedly while a condition
     lasts. A user rule that was true for three hours used to notify roughly
     every six minutes for those three hours. It now notifies when the number
     crosses the line and then stays quiet. This is strictly fewer
     notifications and strictly fewer outbound calls.

  b) Installing a plugin from a user-supplied link had a 100-second limit on
     the whole download, so a large plugin on a slow connection could never
     install. The limit is now ten minutes and the file is read as it arrives.
     Same single call to the same user-supplied address; no new destination.

  c) One addition: a rule may ask for a second notification when the number
     comes back over its line. It is off unless the user ticks it, and it goes
     to the destinations that rule's first notification already goes to. There
     is no new setting and no new destination.

  d) A notification about a number that belongs to no single account used to
     print with an empty name where the account name goes. It now leads with
     the rule's own name. Display only.

2. THE MANIFEST DID NOT MOVE

Package.appxmanifest differs from v1.29.0.0 by the Version attribute and
nothing else. Same runFullTrust capability, same declared languages, same
protocol declarations, same startup task. No new capability is requested.

3. THE PLUGIN INTERFACE DID NOT MOVE

plugin_contract.proto does not appear in this diff at all - no field, no
message, no RPC, not even a comment. No file governing plugin capabilities or
consent appears either. A plugin built against v1.28 or v1.29 runs against this
build untouched.

4. NO NEW NETWORK DESTINATION

The app contacts no host it did not contact at v1.29.0.0. Every added line in
this diff mentioning a URL or an HttpClient is inside a unit test, including an
intentionally unreachable example.invalid address used to prove a timeout is
reported helpfully. The one production change sets a timeout value on an
HttpClient that already existed.

RoRoRo gathers no third-party game data itself. It holds no address, no field
path and no vendor name for any such source; a build-time test
(NoVendorNameFenceTests) fails if one appears. Numbers come from a plugin the
user installs and grants permission to, per capability, by name.

5. WHAT YOU CANNOT EXERCISE ON CERTIFICATION HARDWARE, AND WHY

The notification path in section 1(a) and 1(c) only runs for someone who has
installed a plugin that reports numbers and has written a rule for it. This
package ships no such plugin, and a fresh install makes no notification calls
at all until the user configures a destination themselves. So there is nothing
to observe here without that setup, and we would rather say so than have you
hunt for it.

Everything else in the app is exercisable normally: launching accounts,
themes, settings, the tools window, and the plugin page with its per-capability
consent sheet.

6. UNCHANGED SINCE THE LAST REVIEW, RESTATED

  - The app synthesizes no input and injects into no process. It observes and
    it launches; it does not play the game for anyone.
  - Account credentials are never seen by the app. Sign-in happens inside
    Roblox's own page in a WebView2 frame; only the session cookie Roblox sets
    is captured, and it is encrypted to the Windows user before it touches
    disk.
  - No telemetry and no analytics.
  - Optional notifications go only to a Discord webhook or a push service the
    user sets up themselves.

Thank you for your time.

626 Labs LLC
```
