# Submission packet — v1.32.1.0

Everything Partner Center asks for on this submission, in the order it asks. Certification last saw
**v1.32.0.0**, so this is a single version's delta — and a small one: one fix, no new feature.

> **DRAFTED 2026-10-06, not yet submitted.** Two things are open before the owner's submit click:
> the translation verifier did not run (section 3), and the clean-VM smoke has not been done
> (section 7). Both are stated rather than papered over.

---

## Read this first — a setting that now takes effect immediately

There is nothing new for a reviewer to assess. v1.32.1.0 changes **when an existing setting is
read**, not what the app may do. "Watch memory while accounts are running" was read once at startup;
it is now read whenever the user changes it, so switching it off stops the memory checks in that
session instead of at the next launch.

Verified against `git diff v1.32.0.0..HEAD`, not assumed:

- **Six files changed, all of them ours**, +387/-11: `App.xaml.cs`, `SettingsPage.xaml.cs`,
  `MemoryWatchdog.cs`, a new `MemoryWatchdogGate.cs`, and two test files.
- **`Package.appxmanifest`: the only change is `Version`.** Same identity, same publisher, same
  capabilities, same extensions.
- **No `.proto` change.** Contract version unchanged, no capability, consent or RPC file touched.
- **No input synthesis added.** No added line in `src/` contains `SendInput`, `keybd_event`,
  `mouse_event`, `SetWindowsHookEx`, `PostMessage`, `SendMessage(`, `WriteProcessMemory`,
  `OpenProcess`, `DllImport` or `LibraryImport` — grep count zero.
- **No new URL.** No added `http`/`https` string anywhere in `src/`.
- **No new process action.** Nothing starts, stops or opens a process that did not before; the
  change stops and starts a 30-second timer the app already owned.

The macro wall is untouched: the Store binary still synthesizes no input and injects into nothing.

## 1. Packages

| File | Size | Architecture |
|---|---|---|
| `dist/RORORO-Store-x64-1.32.1.0.msix` | 106.8 MB | x64 |
| `dist/RORORO-Store-arm64-1.32.1.0.msix` | 100.6 MB | arm64 |

Both unsigned — Partner Center signs after upload. Ship **both**.

Identity: `626LabsLLC.RoRoRoBlox`, publisher `CN=177BCE59-0966-4975-9962-10E36652141F`, display name
`626Labs LLC`. Version `1.32.1.0`, fourth component zero as the Store requires.

Sideload build for the clan: `dist/RORORO-Sideload-x64-1.32.1.0.msix` (106.8 MB), signed with
`dev-cert.pfx`, signer subject `CN=177BCE59-0966-4975-9962-10E36652141F` — equal to the packed
manifest's Publisher, which is the check that matters. No cert rotation this release.

## 2. Listing

**Audited, and one finding. Not unchanged.**

All three surfaces plus the hub page were re-read against the fresh release notes, per the standing
rule. The short description, the product features and `docs/index.md` need no edit: this release adds
no capability and removes no claim.

**The finding is in the long description.** It says alerts can be routed "to any mix of desktop
notifications, a Discord webhook you create, and your phone" per alert. For memory warnings that is
currently incomplete: a memory warning always raises a desktop balloon regardless of whether Desktop
is ticked for it, because that balloon is raised on a path that never consults the routing grid. The
claim is true of Discord and the phone, and true of the design; it overstates what unticking Desktop
does for one alert kind.

**Ruling: the listing is not edited for this release, and the gap is recorded here instead.** The
behaviour is a defect with a fix already scoped (the v1.33 alerts cycle, whose first structural task
is routing the desktop balloon like every other destination), not a product decision the listing
should be rewritten to match. Editing the claim now and editing it back next release is churn that
makes the listing less accurate on average, not more. The release notes name the behaviour plainly
under known issues, so a user is told. **If the alerts cycle slips past the next submission, revisit
this and soften the sentence.**

This is the same class as the v1.25 privacy paragraph that motivated the standing rule, with the
opposite remedy, and the difference is worth stating: there, the product had genuinely changed and
the claim had gone stale, so the claim had to move. Here the product is wrong and is being fixed.

## 3. What's new in this version

Blocks: [`whats-new-1.32.1.0.md`](whats-new-1.32.1.0.md) — English plus six translations, each
stepped into its own per-language sheet (`listing-copy-<lang>.md`), because the listing console reads
the **sheets**, not the version file. That was v1.32's near-miss and it was checked this time: all
six sheets announce 1.32.1.0.

Every block is well inside the 1500-character limit: English 667, German 751, French 851, Russian
746, Portuguese 744, Polish 724, Spanish 762.

**Verifier run: DONE 2026-10-06 at `442b521`, both shapes.** All six `whatsNew` pairs judged.

| Language | Verdict | Issue |
|---|---|---|
| fr | approve | none |
| de | approve | none |
| pt-br | approve | none |
| ru | revise | blocker — rule 3, "in-app UI strings must remain untranslated" |
| pl | revise | blocker — rule 3, same |
| es | revise | blocker — rule 3, same |

**All three blockers are one false positive, and it is overruled.** Rule 3 assumes the application's
interface is English. RoRoRo's interface has been localized into exactly these six languages since
v1.26, and the labels quoted in each block are lifted verbatim from the shipped
`Strings.<culture>.resx`. Applying the suggested fixes would put "Memory to keep free (MB)" into the
Spanish listing, naming a label no Spanish user can find in their own app — it would make the listing
less accurate, not more. v1.32 drew this same false positive twelve times out of twelve and overruled
it on identical grounds.

**The split is the tell.** The same construction was approved in French, German and Portuguese and
flagged in Russian, Polish and Spanish. A genuine rule violation would not land on half the set;
model variance does.

**No other issue was raised, in any language** — no mistranslation, no register slip, no cap
violation, nothing on content. For copy whose only prose is one heading, three bullets and a closing
line, with every UI label taken from the shipped resources, that is the expected result and it is now
evidenced rather than asserted.

**Known gaps in the run:** four pairs were still unjudged when the run stopped — `longDescription`
and `features` for some languages, which this release does not touch and which were already pending
before it. The verifier's origin also returned a timeout and then a 502 near the end; the six pairs
that matter were already judged by then. Worth filing upstream (dogfood issues go to
`estevanhernandez-stack-ed/translation-verification`): **rule 3 has now produced a false positive on
every pair of two consecutive releases**, because the rubric has no way to know the product's own UI
is localized. That is a rubric bug, not a copy bug, and it costs a manual overrule every release
until it is fixed.

## 4. Notes for certification

Nothing new to declare. No new capability, no new network endpoint, no new permission, no change to
data handling. The privacy policy is unchanged and still accurate.

## 5. Age rating, privacy, screenshots

Unchanged from v1.32.0.0. No new content, no new data collection, no new imagery. Screenshots do not
need retaking: the only visible difference is a checkbox behaving correctly, which no screenshot
shows.

## 6. GitHub release

Tag `v1.32.1.0` fires `release.yml`, which builds, tests, runs Velopack and drafts the release with
`rororo-win-Setup.exe`, the full and delta nupkgs and `releases.win.json`, then signs and attaches
`roblox-compat.json` + `.sig` and `plugins-catalog.json`. The sideload MSIX and `dev-cert.cer` are
attached by hand in Phase 6. The body is the block between the `---` markers in
[`release-notes-1.32.1.0.md`](release-notes-1.32.1.0.md).

`plugins-catalog.json` carries Ur Score 0.7.0, merged in PR #226 before this tag — deliberately, so
this release's re-upload does not revert the live catalog to 0.6.3.

## 7. Verification

- **Full solution, Release, on this tree:** 2,606 unit + 27 harness pass, 1 harness skip by design.
- **CI on `main`:** green at the commit this release is cut from, across x64, native arm64 and the
  secret-scan / local-path guard.
- **The fix itself was verified against the running app** on 2026-10-05, not only by test: with a
  client in game, a hand-written cap crossed one sample tick later; with watching off the same edit
  produced nothing for 150 seconds against under 30 seconds twice while on; re-enabling resumed
  immediately; and the UI checkbox saved both ways under a real click. Written up in
  [`smoke-2026-10-05-memory-watch-live-toggle.md`](../smoke-2026-10-05-memory-watch-live-toggle.md).
- **Clean-VM smoke: NOT DONE.** The playbook asks for a manual smoke on a clean VM and there was no
  VM available. The live verification above covers the behaviour this release changes but not a
  fresh install on an untouched machine. Stated so the owner can decide whether it blocks the submit
  click.
