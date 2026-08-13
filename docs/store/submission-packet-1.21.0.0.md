# Submission packet — v1.21.0.0

Everything needed to complete the Partner Center submission in one pass, in the order the form asks
for it. Internal record; nothing here is pasted into Partner Center except where marked.

---

## Read this first — this submission is not incremental

**Certification last saw v1.15.0.0.** This is v1.21.0.0. Nothing was withdrawn or rejected in
between — v1.16 through v1.20 shipped to the direct-download channel and were never submitted,
because the Store lane was deliberately parked while the UI backbone was remediated. Six versions
arrive at once.

Two consequences worth holding:

1. **The reviewer letter opens on the gap** rather than letting a reviewer discover a six-version
   discrepancy on their own. That is the single most important paragraph in the packet.
2. **The "what's new" text has to cover six releases**, not one. Release notes exist for each
   (`release-notes-1.17` … `-1.21`); v1.16 has only a feature-ledger row.

---

## 1. Packages to upload — both of them

| | x64 | arm64 |
|---|---|---|
| File | `dist/RORORO-Store-x64-1.21.0.0.msix` | `dist/RORORO-Store-arm64-1.21.0.0.msix` |
| Size | 91.12 MB | 86.02 MB |
| `ProcessorArchitecture` | `x64` | `arm64` |

Shared by both: built from `0c04ee4` on `main` (tag `v1.21.0.0`), identity `626LabsLLC.RoRoRoBlox`,
publisher `CN=177BCE59-0966-4975-9962-10E36652141F`, version `1.21.0.0`, `runFullTrust` and nothing
else, and **unsigned** — Partner Center signs on upload, do not sign locally.

The filenames match what Partner Center already lists for v1.15.0.0
(`RORORO-Store-arm64-1.15.0.0.msix` / `RORORO-Store-x64-1.15.0.0.msix`). The scripts emit that shape
now, so it does not need correcting per release.

**On the AArch32 notice Partner Center shows.** It does not apply. AArch32 is 32-bit ARM (MSIX
`arm`); this app has never declared it, and neither has x86 — checked across the csproj, both build
scripts, the manifest and CI. `arm64` is a different architecture, not a newer flavour of the same
one. v1.15 already shipped arm64 and the notice displays against it anyway, which is what a blanket
advisory looks like.

**Verified by reading the packed `AppxManifest.xml` back out of the `.msix`**, not by trusting the
build script's output. Identity, version and the single capability all confirmed after packing.

**Rebuild it if `main` moves.** The package must come from the commit the tag points at. This one was
rebuilt twice for exactly that reason.

## 2. Notes for certification

Paste the fenced block from [`reviewer-letter-1.21.0.0.md`](reviewer-letter-1.21.0.0.md) verbatim.

It answers, in this order: the version jump, no new capabilities, nothing new leaving the machine,
what the app does and how (documented mutex + published auth-ticket flow, no client modification),
credential handling under DPAPI, **policy 10.2.2 and why the Store edition carries no in-app plugin
catalog**, what changed since v1.15, accessibility, and the testing posture.

## 3. Screenshots — 10, with captions

Files in [`screenshots/`](screenshots/), captions in
[`screenshots-checklist.md`](screenshots-checklist.md). All 1920×1080, brand navy, streamer-mode
identities.

**Check the real cap before uploading.** The checklist says Partner Center accepts 1–9; that number
was copied from the Sanduhr playbook, not read off the upload form, and this set is 10. If it caps at
9, drop `08-theme-builder.png` — `02-themes.png` already carries the theming story. **Do not drop
`10-multi-instance.png`**, which is the only frame showing the product doing the thing the listing is
about.

`10-multi-instance.png` is the one frame that is not raw: eight nameplate regions are blurred,
because other players' names rendered in-game and streamer mode has no reach there. Provenance is in
the checklist under its own heading.

## 4. Listing text

- Long description, short description, keywords: [`listing-copy.md`](listing-copy.md)
- What's new: draw from `release-notes-1.21.0.0.md`, and say plainly that it also carries 1.16–1.20.
- Privacy policy URL: the GitHub Pages render of `docs/PRIVACY.md`
- Age rating: answers in [`age-rating.md`](age-rating.md), and they must stay consistent with the
  privacy claims.

## 5. What is actually in this release

Headline is presentation, but three changes are not:

- **Plugin processes die with the host.** They are attached through a Windows job object, so an
  abnormal exit takes them too. Previously one orphan accumulated per session; six were found alive
  on one machine. This strictly reduces what runs on a user's PC and is called out in the reviewer
  letter for that reason.
- **The pre-warm gate reads the version that will actually launch** — the `roblox-player` handler's
  pin — instead of the newest version installed. Those differ precisely during an update, which is
  the reported "batch-launching alts while Roblox updates goes crazy".
- **Two measured contrast failures fixed**, at 4.19:1 and 1.29:1 against the 4.5:1 AA floor. One had
  been shipping since the theme it belongs to was written.

## 6. Verified before this packet was written

- Full solution green: **1643 unit + 23 integration**, locally, on the tagged commit.
- CI green on `0c04ee4` — guard, x64 and arm64 jobs.
- Packed manifest identity and capability read back out of the `.msix`.
- Both pre-commit guards pass a whole-tree sweep, including a 77 MB binary that had been clearing
  all three secret-scan checks at once until this cycle widened two of them.

## 7. Known-open, disclosed here so it is not a surprise later

- **F-105 — nine whole-window render gates are skipped on CI.** They wedge on a runner and pass on a
  desktop; the mechanism is unsolved. They are skipped rather than passed, so a CI run reports
  `9 skipped` on its face. They still run locally and did on the tagged commit. This is a real
  coverage reduction against the Store build, and it is the reason the local run is the one that
  counts right now.
- **`submission-checklist.md` is 0 of 31 ticked** on an app that has shipped to the Store several
  times. It is a template nobody maintained and **must not be used as a pre-flight gate** in this
  state — it will report that the publisher account is unverified. Ticking it against reality is
  owed before the next submission.
- **The plugin contract package is not on nuget.org.** `ROROROblox.PluginContract` 0.5.0 through
  0.8.0 are all absent, so the `rororo-ur-task` PR stays red. No effect on the Store package, which
  ships no in-app plugin catalog.
- **The screenshot cap is unconfirmed.** See §3.
