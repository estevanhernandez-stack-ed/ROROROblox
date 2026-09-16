# Notes for certification — reviewer letter (v1.29.0.0)

> Paste the fenced block below the `---` marker — everything from `Hello reviewer,` to
> `626 Labs LLC`, not the backticks — into Partner Center → your app → **Submission options** →
> **Notes for certification**. **Partner Center only.** `submit_write.py` never sends
> `notesForCertification`, and the field read back empty through the API on v1.28 even after the
> letter was pasted, so confirm it on the Partner Center screen or not at all.
>
> **Delete the bracketed `[OWNER INPUT: ...]` line in section 6 before pasting**, along with the
> sentence after it if the answer is no. It offers the reviewer a signed-in Roblox account to
> exercise the alert path with. That is the owner's call and the owner's credential; nothing in this
> repo supplies one, and no address or contact is invented anywhere in this letter.
>
> **A single-version delta.** Certification last saw v1.28.0.0, which certified and published on
> 2026-09-12.
>
> **The letter leads with the smallness of the delta, because that is the finding.** The two things
> in the diff a reviewer could reasonably slow down on are both narrowing rather than widening: a
> `plugin_contract.proto` file that turns out to hold a comment change, and a new `allowed_mentions`
> field on an existing Discord call that stops an alert pinging a channel. Section 3 flags the proto
> file explicitly rather than waiting to be asked — the file name looks more significant than the
> change. Section 6 says plainly what a reviewer cannot exercise on certification hardware, because
> the headline change needs a plugin that reports a number and this package ships none.
>
> **Every claim in the letter was verified against the diff before writing, not assumed:**
>
> - `git diff v1.28.0.0..v1.29.0.0 -- src/ROROROblox.App/Package.appxmanifest` returns **nothing**;
>   against `main` (post-bump) the only difference is `Version="1.29.0.0"`. So: no capability change,
>   no new declared language, no new protocol, same startup task.
> - `plugin_contract.proto` is **comment-only** — the diff with comment lines filtered out is empty.
>   No field, no field number, no message, no RPC.
> - **No file** matching `RpcMethodCapabilityMap`, `PluginCapability`, `PluginHostService`, consent,
>   any `.csproj` or the manifest appears in the diff. No new capability, no consent change.
> - **No new host.** Every added line mentioning a URL, a hostname or an `HttpClient` across `src/`
>   and `tools/` is either a comment or a test; the only `new HttpClient(...)` added is in
>   `DiscordWebhookSenderTests`.
> - **No input synthesis added.** No added line in `src/` or `tools/` contains `SendInput`,
>   `keybd_event`, `mouse_event`, `DllImport`, `SetWindowsHookEx`, `PostMessage`, `SendMessage`,
>   `WriteProcessMemory` or `OpenProcess`.
> - **No `.xaml` and no `.resx` changed**, so nothing new appears in the interface and no UI string
>   moved.
> - `allowed_mentions` with an empty `parse` array is at exactly one send site,
>   `App/Discord/DiscordWebhookSender.cs`, which all five alert kinds pass through.
>
> **Not verified, and not claimed anywhere in the letter:** nothing. Every statement above maps to a
> command run against the tagged tree on 2026-09-15.
>
> Sources: `docs/store/release-notes-1.29.0.0.md`,
> `docs/store/submission-packet-1.29.0.0.md`,
> `docs/superpowers/specs/2026-09-15-metric-alert-wording-design.md` (APPROVED DEVIATIONS banner,
> rulings C1-C3), the 2026-09-15 entry in `docs/decisions.md`.

---

```
Hello reviewer,

Thank you for your time on v1.29.0.0. Certification last saw
v1.28.0.0, which certified and published on 2026-09-12, so this
is a single version's delta - and a narrow one. Nothing in it
adds a network destination, a capability, a permission, a plugin
call, or a collected field. What changed is the wording and the
grouping of a notification the user already had, plus two
restrictions that make every notification narrower than it was.

1. WHAT CHANGED. Four items, all local to how an alert is
   composed and sent.

   a. An alert about a number the user is watching used to print
      an internal identifier and a raw value. It now prints the
      name the user gave their own rule, what they asked it to
      watch for, and the value with separators. The text is
      composed on the machine, from the user's own rule file and
      a value the application already held. Nothing new is read,
      stored, or transmitted.

   b. When one plugin report produces alerts for several
      accounts at once, they are now combined into a single
      notification per destination instead of one per account.
      Strictly fewer messages leave the machine than before. The
      combining happens in memory, holds for five seconds, is
      never written to disk, and is discarded rather than sent
      if the application exits while it is waiting.

   c. Every notification body is now sized to the destination it
      is going to and ends with "and N more" when it does not
      fit. Previously an over-long body was rejected by the
      service or cut off mid-word with no marker.

   d. Every Discord post now sets allowed_mentions to an empty
      parse list, so no text inside an alert - a rule name, an
      identifier, an account name - can notify the channel it
      lands in. This is a restriction on a call disclosed at
      v1.4, not a new call.

2. NO MANIFEST OR CAPABILITY CHANGE. The package manifest is
   identical to the one you certified for v1.28.0.0 apart from
   the version number: same runFullTrust capability and no
   other, same declared languages, same protocol declarations,
   same startup task.

3. THE PLUGIN INTERFACE FILE IS IN THE DIFF, AND THE CHANGE IS A
   COMMENT. plugin_contract.proto shows one corrected comment
   block and nothing else - no field, no field number, no
   message, no method. No new capability is defined, and a
   plugin built against v1.28 is unaffected. We raise it
   unprompted because a diff view will surface the file and the
   file name looks more significant than the change.

4. NO NEW NETWORK DESTINATION. The set of hosts is the one you
   certified: Roblox-owned endpoints, GitHub Releases for update
   checks and the signed compatibility feed, and - only when the
   user has configured them - the user's own Discord webhook and
   their own phone push service. The shipped application still
   gathers no watched number itself. It holds no address, no
   field path, and no vendor name for any third-party data
   source, and a test in the build fails if one ever appears.

5. NO CHANGE TO INPUT HANDLING, AND NO PLUGIN CODE IN THIS
   PACKAGE. The application synthesizes no keyboard or mouse
   input and injects nothing into any other process. That
   boundary is unchanged in this version and is enforced in the
   shipped binary.

   The plugin system you have certified since v1.4 is unchanged
   and still works the way that letter described: plugins are
   separate executables, distributed separately, never bundled
   in this MSIX, and never fetched by the application on its
   own. A user installs one deliberately and grants each
   capability on a consent sheet; a capability a plugin does not
   declare is treated exactly as a refusal. A plugin that
   synthesizes input must declare that capability and be granted
   it by the user, and it does so in its own process. The
   Store-distributed binary neither does it nor does it on a
   plugin's behalf. There is no plugin executable inside this
   package, and the MSIX can be inspected for that.

6. TESTING NOTES - WHAT IS AND IS NOT EXERCISABLE HERE. Items 1a
   and 1b only produce visible output while a plugin is
   reporting a number, and this package ships none; nor can one
   be installed from the application's own plugin catalogue
   today. On certification hardware you will therefore see the
   "Metric alerts" switch and its destination checkboxes under
   Settings > Alerts, and no metric notification, because there
   is nothing for the feature to report on. We would rather say
   that than describe a test you cannot run.

   Items 1c and 1d apply to every alert the application already
   had, so they are exercisable: sign in to a Roblox account,
   create a Discord webhook of your own, paste it into
   Settings > Alerts, and any alert that fires posts with
   mentions disabled and a body sized to its destination.
   [OWNER INPUT: keep or cut the next sentence.] If a signed-in
   account would help you exercise that path, ask through this
   submission's certification channel and we will supply one.

   Everything the listing describes - launching several saved
   accounts at once, the account vault, themes, session history
   - is testable without configuring any alert at all.

Credential handling is unchanged: session cookies remain
DPAPI-encrypted, local-only, and never exposed to plugins. There
is no analytics, no telemetry, and no third-party SDK.

The trademark position is unchanged from prior certifications:
RoRoRo is an independent tool, not affiliated with Roblox
Corporation, and the disclaimer appears on the Store
description, the About box, and the privacy policy. Product and
service names (Roblox, Squad Launch, Recycle, Discord, Pushover,
ntfy) are deliberately left untranslated across all languages.

Thank you,
626 Labs LLC
```
