# Microsoft Store submission checklist — RORORO

> **This list is walked fresh on every submission, and left unticked when you are done.** The boxes
> are not a record of what has ever been true - they are the walk itself, so a stale tick is worse
> than no tick. If you find it fully ticked, someone mistook it for a status document and it needs
> clearing before the next ship. (Done once, on 2026-08-13, by me.)
>
> On screenshots: this file said 3-6 and [screenshots-checklist.md](screenshots-checklist.md) said
> 1-9, and **neither had been read off the upload form**. Settled 2026-08-13 by submitting: 10 were
> accepted. Both figures are corrected. And
> [submission-packet-1.21.0.0.md](submission-packet-1.21.0.0.md) carries the upload-order detail
> this list only gestures at.

> Pre-flight + post-flight procedure for submitting to Partner Center. Lifted from Sanduhr's playbook with RORORO-specific addenda. Run the pre-flight EVERY submission (initial + every resubmission).

## Pre-flight (in order)

### Identity & paperwork

- [ ] Partner Center publisher account exists and is verified
- [ ] App identity reserved in Partner Center (e.g., `626Labs.RORORO` or similar — must be unique across the Store)
- [ ] Publisher display name decided: **626 Labs LLC** *(or Estevan Hernandez until LLC paperwork lands)*
- [ ] Identity fields in `Package.appxmanifest` updated to match Partner Center reservation:
  - `<Identity Name="..." Publisher="..." />` matches the reserved name + the Partner-Center-issued publisher CN
  - `<Properties><PublisherDisplayName>...</PublisherDisplayName>` matches the display name above
  - Version bumped per release (track the latest version of the technical-fix PR before locking)

### Trademark disclaimer surface check

Per Sanduhr playbook 10.1.4.4.a — the disclaimer must appear in MULTIPLE surfaces. Verify each:

- [ ] **Store description (long form):** trademark paragraph present (`docs/store/listing-copy.md`)
- [ ] **Store copyright field:** disclaimer appended (`docs/store/listing-copy.md`)
- [ ] **MSIX manifest `<Description>`:** short disclaimer present (`Package.appxmanifest`)
- [ ] **About box:** disclaimer present (`AboutWindow.xaml`)
- [ ] **README:** disclaimer block at top + footer
- [ ] **Privacy policy:** disclaimer in footer (`docs/PRIVACY.md`)

### Privacy policy

- [ ] `docs/PRIVACY.md` rendered to a public, crawlable URL (GitHub Pages, custom domain, or raw GitHub permalink — domain preferred)
- [ ] Privacy policy URL added to Partner Center listing
- [ ] Privacy claims match age-rating answers (no telemetry, no third-party data sharing)

### Build artifacts

- [ ] All Logos PNGs present and pass `scripts/build-msix.ps1 -Verify`
- [ ] Run `scripts/install-local-msix.ps1` and verify: install succeeds, app launches, basic flow works (add account → launch as), uninstall succeeds, LocalState is gone after uninstall
- [ ] Build the **Store** flavor (unsigned — Partner Center signs after upload):
  ```powershell
  powershell -ExecutionPolicy Bypass -File scripts/build-msix.ps1 -Store
  ```
- [ ] MSIX packed for BOTH architectures: `dist/RORORO-Store-x64-<version>.msix` and `dist/RORORO-Store-arm64-<version>.msix` (`finalize-store-build.ps1` run twice, second time with `-Architecture arm64`)

### Listing materials

- [ ] Screenshots captured per `docs/store/screenshots-checklist.md` — multi-state, and **at least 10 fit**: the v1.21.0.0 submission uploaded 10 and Partner Center took all of them. The "3–6" that used to sit here was a playbook figure, not a form limit.
- [ ] Long description from `docs/store/listing-copy.md` ready to paste
- [ ] Short description (≤200 chars) ready
- [ ] Keywords picked (no flagged terms — see listing-copy.md)
- [ ] What's-new release notes ready (DON'T fill in until version is locked)

### Age rating

- [ ] `docs/store/age-rating.md` answers ready to enter into the IARC questionnaire
- [ ] Age rating answers consistent with privacy policy + listing description

### Documentation surfaces

- [ ] CONTRIBUTING.md / README.md note Microsoft Store as the primary distribution path
- [ ] All in-app links work (Repo URL, Issues URL, Open log folder)

## Submit

1. Partner Center → Apps → New product → MSIX/PWA app
2. Pick the reserved app name
3. Upload BOTH `dist/RORORO-Store-x64-<version>.msix` and `dist/RORORO-Store-arm64-<version>.msix`
4. Fill in pricing (Free), markets (Worldwide unless intentional limit), age rating questionnaire
5. Paste listing copy, screenshots, keywords, privacy policy URL
6. Submit for certification

## Wait — typical 24–72 hours

Partner Center status page will move through:
- *In submission* → *Certification* → *Publishing* (success) OR *Failed* (rejection)

## Post-flight — if certified

- [ ] Tag the release in git (`git tag v<X.Y.Z>` matching the manifest version)
- [ ] Update README.md "Microsoft Store" install section to point to the live listing
- [ ] Capture the listing URL + add to dashboard
- [ ] Announce in clan Discord with Store link

### Carry to next release (Partner Center surfaced these on the v1.1 submission)

- ~~**Arm64 (AArch64) build target.**~~ **Done, and the note was stale from v1.11 onward.** Struck 2026-08-13. The action it asked for — *"extend `scripts/build-msix.ps1` to support `-Architecture arm64`"* — already exists and shipped; `finalize-store-build.ps1 -Architecture arm64` produced `RORORO-Store-arm64-1.21.0.0.msix` this release, and Partner Center has listed `RORORO-Store-arm64-1.15.0.0.msix` since v1.15. Its premise, *"Current MSIX is x64 only"*, has been false for six versions.
  **And the Partner Center notice it quotes does not apply to this app at all.** AArch32 is 32-bit ARM (MSIX `arm`); RoRoRo has never declared it, and never declared x86 either — verified 2026-08-13 across the csproj, both build scripts, `Package.appxmanifest` and CI. `arm64` (AArch64) is a different architecture, not a newer flavour of the same one, so there was never anything here to migrate *from*. The notice still displays against the v1.15 submission that already shipped arm64, which is what a blanket advisory looks like. **The real Arm concern was never AArch32 — it was x64-on-Arm emulation**, and shipping a native arm64 package is what answers that. Left visible rather than deleted so the next person does not re-derive it from the same notice.

## Post-flight — if rejected

Per Sanduhr playbook response protocol:

1. **Read the rejection email carefully.** Quote the specific clause numbers (e.g., "10.1.4.4.b") in your Notes-to-Publisher response.
2. **Identify the root cause, not the surface symptom.** If reviewer says "we couldn't tell what this app does," that's a *navigation* failure — fix the listing description's lead paragraph + screenshot ordering. If reviewer says "trademark concerns," that's an *attribution* failure — make the disclaimer more prominent.
3. **Increment version.** Bump `Version` in `Package.appxmanifest` for every resubmission. Partner Center treats resubmissions with the same version as updates-to-rejected, which is messier.
4. **Add a regression test if it's code-side.** Catch the bug class for the next release.
5. **Re-submit** with a Notes-to-Publisher message that:
   - Quotes the clause from the rejection
   - Names what was changed
   - Frames the change as collaborative ("we want to meet this requirement; here's how we addressed it")
   - Does NOT argue the rejection. Reviewers are people; argue the clause, not them.

## Resubmission cycles to expect

Sanduhr passed on submission **3** (two rejections, both 10.1.4.4). For RORORO the bar is higher (Roblox trademark exposure > Anthropic exposure). **Plan for 2–4 cycles.** Each cycle ~24–72 hours.

## Roblox-side risk (RORORO-specific, not in Sanduhr playbook)

Microsoft cert reviewers don't typically Google the trademark holder's stance, but Roblox Corp could submit a complaint to Microsoft if they object to our distribution. Probability is low — multi-instancing tools have existed for years (MultiBloxy, Bloxstrap-related forks, etc.) without takedown action. But it's non-zero. If it happens:

1. Don't panic. Microsoft typically asks the publisher for response, not auto-removes.
2. Respond with the same nominative-use framing — we describe compatibility, we don't claim affiliation, we don't modify the Roblox client.
3. If escalated, consult an attorney before responding further.

Document any Roblox-side compatibility event in the dashboard decisions log per CLAUDE.md.

## References

- [`docs/store/listing-copy.md`](listing-copy.md) — listing description + multi-feature value framing
- [`docs/store/age-rating.md`](age-rating.md) — questionnaire answers
- [`docs/store/screenshots-checklist.md`](screenshots-checklist.md) — capture plan
- [`scripts/install-local-msix.ps1`](../../scripts/install-local-msix.ps1) — local Add-AppxPackage smoke
- [`scripts/uninstall-local-msix.ps1`](../../scripts/uninstall-local-msix.ps1) — uninstall + cleanup verification
- [`docs/PRIVACY.md`](../PRIVACY.md) — privacy policy (host this URL publicly)
