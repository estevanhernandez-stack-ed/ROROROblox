# Microsoft Store submission checklist — ROROROblox

> Pre-flight + post-flight procedure for submitting to Partner Center. Lifted from Sanduhr's playbook with ROROROblox-specific addenda. Run the pre-flight EVERY submission (initial + every resubmission).

## Pre-flight (in order)

### Identity & paperwork

- [ ] Partner Center publisher account exists and is verified
- [ ] App identity reserved in Partner Center (e.g., `626Labs.ROROROblox` or similar — must be unique across the Store)
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
- [ ] MSIX packed at `dist/ROROROblox-Store.msix`

### Listing materials

- [ ] Screenshots captured per `docs/store/screenshots-checklist.md` (3–6, multi-state)
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
3. Upload `dist/ROROROblox-Store.msix`
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

- [ ] **Arm64 (AArch64) build target.** Partner Center flagged: *"Future Windows on Arm devices will no longer support AArch32, therefore we recommend updating your targeted platforms to Arm64 (AArch64), which works on all Windows on Arm devices, as soon as possible in order to ensure your customers can continue to enjoy your experience."* Current MSIX is x64 only (`<Identity ProcessorArchitecture="x64" />`). v1.1.1 or v1.2 should add an Arm64 build flavor + ship a multi-arch package (or two packages — Microsoft accepts either pattern). Action: extend `scripts/build-msix.ps1` to support `-Architecture arm64`, regenerate manifest with arm64 ProcessorArchitecture, and produce `dist/ROROROblox-Store-arm64.msix` alongside the x64 build. Bump version when shipping.

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

Sanduhr passed on submission **3** (two rejections, both 10.1.4.4). For ROROROblox the bar is higher (Roblox trademark exposure > Anthropic exposure). **Plan for 2–4 cycles.** Each cycle ~24–72 hours.

## Roblox-side risk (ROROROblox-specific, not in Sanduhr playbook)

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
