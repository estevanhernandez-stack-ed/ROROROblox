# Submission packet — v1.30.0.0

Everything Partner Center asks for on this submission, in the order it asks. Certification last saw
**v1.29.0.0**, which certified and published on 2026-09-16, so this is a single version's delta.

> **SUBMITTED for certification 2026-09-20** by the owner. What remains on this side: publish the
> GitHub release draft, and the clan Discord post (`discord-post-1.30.0.0.md`), which by standing
> rule goes out when the Store listing actually shows 1.30 rather than when certification clears.

---

## Read this first — everything in this release narrows

The finding a reviewer should take away is the direction of travel. Every behaviour change here makes
the app do **less**, and the one addition is off until the user switches it on:

- **Alerts fire once, on the crossing, instead of repeatedly while a condition lasts.** A rule that
  stayed true used to re-alert on every observation, held back only by a five-minute cooldown. At the
  reporting interval in practice that was a notification roughly every six minutes for as long as the
  condition lasted. It is now one notification per crossing. Strictly fewer notifications, strictly
  fewer outbound calls.
- **The plugin installer's download budget went from 100 seconds to ten minutes.** Same single call
  to the same user-supplied address. No new destination; a download that used to fail now finishes.
- **One addition: an optional second notification when a number comes back over its line.** Off
  unless the user ticks it, and it routes to the destinations that rule's first notification already
  uses. No new setting, no new destination.
- **A notification about a number belonging to no single account now leads with the rule's name**
  instead of an empty account name. Display only.

Verified against `git diff v1.29.0.0..v1.30.0.0`, not assumed:

- **`Package.appxmanifest` differs by the `Version` attribute alone.** Same `runFullTrust`, same
  declared languages, same protocol declarations, same startup task. (The attribute reads `1.28.0.0`
  at the v1.29.0.0 tag because the manifest is patched during the Store build rather than at the bump
  commit; the package uploaded for v1.29 carried `1.29.0.0`.)
- **`plugin_contract.proto` is not in the diff at all** — not a field, not a message, not an RPC, not
  a comment. No file matching `RpcMethodCapabilityMap`, `PluginCapability`, `PluginHostService` or
  consent appears either. A plugin built against v1.28 or v1.29 runs untouched.
- **No new host.** Every added line across `src/` and `tools/` mentioning a URL, a hostname or an
  `HttpClient` is in a test, including a deliberately unreachable `example.invalid`. The one
  production change sets `Timeout` on an already-registered client.
- **No input synthesis added.** No added line contains `SendInput`, `keybd_event`, `mouse_event`,
  `DllImport`, `SetWindowsHookEx`, `PostMessage`, `SendMessage`, `WriteProcessMemory` or
  `OpenProcess`.
- **No `.xaml` and no `.resx` changed**, so nothing new appears in the interface and no UI string
  moved.

Unchanged and worth restating: the binary **gathers no third-party game data itself**, holds **no
address, field path or vendor name** for any such source (`NoVendorNameFenceTests` fails if one
appears), contacts **no new host**, synthesizes **no input**, and injects into nothing.

---

## 1. Packages

| File | Size | Architecture |
|---|---|---|
| `dist/RORORO-Store-x64-1.30.0.0.msix` | 106.66 MB | x64 |
| `dist/RORORO-Store-arm64-1.30.0.0.msix` | 100.48 MB | arm64 |

Both unsigned — Partner Center signs after upload. Ship **both**.

Identity: `626LabsLLC.RoRoRoBlox`, publisher `CN=177BCE59-0966-4975-9962-10E36652141F`, display name
`626Labs LLC`. Version `1.30.0.0`, fourth component zero as the Store requires.

## 2. Listing

**Audited 2026-09-20 against the fresh release notes, all four surfaces: short description, long
description, product features, and the hub page. Outcome: UNCHANGED.** Nothing in 1.30 makes an
existing claim incomplete — the crossing rule and recovery alerts refine alerting, which the listing
already claims, and the plugin-install fix repairs a claimed feature rather than adding one. No new
feature entry; still 19 of 20 used.

**Two questions were raised in the audit and RULED ON by the owner the same day: no change needed.**
A Store install arrives with no plugin, so there are no metric alerts for the alerts bullet to
enumerate, and nothing the user has not installed and granted makes a network call. The bullet and
the privacy paragraph are both claims about RoRoRo itself; the listing's plugin bullet covers the
rest. Full reasoning in `listing-copy.md`, including the fact that qualifies it — the plugin
marketplace is gated to unpackaged builds, but install-from-URL is not, so the ruling rests on the
default state rather than on plugins being unreachable.

## 3. What's new in this version

`docs/store/whats-new-1.30.0.0.md`, seven blocks — English plus fr, de, ru, pt-BR, pl, es.

**Verified by the translation verifier at commit `7994bdf`, both shapes, 18 pairs: 12 approve
clean.** The 6 that read `revise` all flag the same thing — the "the alert sentence is still English"
caveat has no English counterpart, which is claim fidelity working as designed. It is kept as a
**deliberate, precedented deviation**: v1.29 made the same call and certified with it, and an English
reader does not need telling that an English sentence is in English. Reversible by the owner.

The run found two real defects on the way: the caveat had been filed under the plugin-install section
it has nothing to do with, and the French install line had a relative pronoun with no antecedent. A
German word-order error was caught by eye before the run. Three defects in six hand-written
translations — the argument for running it.

## 4. Notes for certification

`docs/store/reviewer-letter-1.30.0.0.md`; paste-ready text in
`docs/store/reviewer-letter-1.30.0.0.paste.txt` (3,926 characters).

**Partner Center only.** `submit_write.py` never sends `notesForCertification`, and the field read
back empty through the API on v1.28 even after the letter was pasted — confirm it on the Partner
Center screen or not at all.

**Before pasting:** delete the bracketed `[OWNER INPUT: ...]` line and the sentence after it if the
answer is no. Section 5 already states plainly what a reviewer cannot exercise on certification
hardware, and why.

## 5. Age rating, privacy, screenshots

No change. `docs/store/age-rating.md` for the questionnaire; privacy URL points at the GitHub Pages
site; screenshots unchanged from the previous submission.

## 6. GitHub release

Draft at tag `v1.30.0.0`, body taken from the release notes between the `---` markers. Assets:
Velopack (`RORORO-win-Setup.exe`, `RORORO-win-Portable.zip`, `RELEASES`, `releases.win.json`, both
nupkgs), the signed compat pair, `plugins-catalog.json`, plus `RORORO-Sideload-x64-1.30.0.0.msix` and
`dev-cert.cer`. **No cert rotation this release.**

Publishing the draft is the owner's click.

## 7. Verification

2,329 unit + 27 harness tests pass (1 harness skip by design), confirmed over three consecutive full
runs locally and green on CI for x64 and arm64 at `e9d44cc`.

**One CI failure on the way, and it was not dismissed as a flake.** `WebhookCatcherTests
.DrainAsync_TwoPostsWellUnderTheQuietPeriodApart` failed on x64 while arm64 passed. The test posted,
waited 50 ms, posted again, and required the second to arrive inside a 150 ms quiet period — about
100 ms for a `Task.Delay` and an HTTP round trip on a runner executing the rest of the suite
alongside it. It measured the scheduler, not the boundary. `WebhookCatcher.Start` now takes an
optional quiet period; the smoke run keeps the shipped 150 ms and the two boundary tests ask for two
seconds, expressing their gaps as fractions of whatever period is in force. Second test in that class
to need this today; the 2026-09-15 decisions entry had left the family on the backlog as a load
flake, and both are now waiting on conditions instead of durations.
