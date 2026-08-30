# Submission packet — v1.24.0.0

Everything needed to complete the Partner Center submission in one pass, in the order the form
asks for it. Internal record; nothing here is pasted into Partner Center except where marked.

---

## Read this first — one deliberate manifest delta, pre-explained

Certification last saw **v1.23.0.0**; this is **v1.24.0.0**. A normal single-version jump with
**one deliberate disclosure-surface change**: the manifest gains two `uap10:Protocol`
declarations and one `desktop:StartupTask` (disabled by default). All three move registrations
the app has always made per-user in HKCU — reviewed in every prior certification — into the
package manifest, because packaged installs virtualize registry writes and the features were
silently dead on the Store build. **Capabilities are unchanged (runFullTrust only).** If
certification asks anything, it will be about the manifest diff, and the reviewer letter answers
it before they ask. Everything else is one local-stats accuracy fix and a privacy-policy text
correction (same URL, no behavior change).

## 1. Packages to upload — both of them

| | x64 | arm64 |
|---|---|---|
| File | `dist/RORORO-Store-x64-1.24.0.0.msix` | `dist/RORORO-Store-arm64-1.24.0.0.msix` |
| Size | 106.04 MB | 99.87 MB |
| `ProcessorArchitecture` | `x64` | `arm64` |

Shared by both: tag `v1.24.0.0` on `main`, identity `626LabsLLC.RoRoRoBlox`, publisher
`CN=177BCE59-0966-4975-9962-10E36652141F`, `PublisherDisplayName` `626Labs LLC` (no space),
unsigned — Partner Center signs after upload. Both are ~15 MB larger than v1.23's: the
Windows 10.0.19041 TFM pulls the CsWinRT projections in with the self-contained runtime.

## 2. Notes for certification

Paste the fenced block from [`reviewer-letter-1.24.0.0.md`](reviewer-letter-1.24.0.0.md).
Reviewer-only, not shown to users. **Do not skip this one this time in particular** — it
pre-explains the manifest diff.

## 3. What's new in this version (public — do not skip)

Paste the fenced block from [`whats-new-1.24.0.0.md`](whats-new-1.24.0.0.md) into
**Store listings → [language] → What's new in this version**. Different field from step 2; both
must be filled.

## 4. Listing changes this submission (unlike v1.23, there are some)

- **Product features** — ADD these two entries (Partner Center cap is 20; the live set holds 12):

  ```
  Start with Windows if you want — one toggle, and Windows' own Startup list stays in control
  ```

  ```
  Discord Join starts RoRoRo even when it's closed, and always asks before launching anything
  ```

- **Screenshots** — seven refreshed 2026-08-30 in `docs/store/screenshots/`: `02-themes`,
  `03-about`, `04-games`, `05-diagnostics`, `06-history`, `07-plugins`, `08-theme-builder`.
  Replace those 1:1 by filename in Partner Center; keep `01`, `09`, `10` as uploaded. One
  caption changes (`06-history`) — the new text is in
  [`screenshots-checklist.md`](screenshots-checklist.md) under "Recaptured 2026-08-30".

- **Unchanged:** short description, long description, IARC rating, pricing (free), markets,
  privacy policy URL (the policy TEXT at that URL was corrected and is already live — GitHub
  Pages rebuilds from `main` on push; verified serving the corrected uninstall wording
  2026-08-30).

## 5. After submission

Typical turnaround 24–72h. On rejection: bump to 1.25.0.0 (never a non-zero 4th component) and
re-run from playbook Phase 3, quoting the clause number in the response per
[`listing-copy.md`](listing-copy.md) → response protocol.
