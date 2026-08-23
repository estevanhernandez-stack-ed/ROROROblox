# Submission packet — v1.23.0.0

Everything needed to complete the Partner Center submission in one pass, in the order the form asks
for it. Internal record; nothing here is pasted into Partner Center except where marked.

---

## Read this first — routine submission, no wrinkles

Certification last saw **v1.22.0.0**; this is **v1.23.0.0**. A normal single-version jump with
**zero disclosure-surface change** — no new capability, endpoint, plugin RPC, or manifest edit.
The headline feature is local-only statistics computed from a file the app already writes. This is
the lightest submission since the catch-ups began; if certification asks anything, it will be about
the statistics feature, and the answer is one sentence: computed locally from local data, nothing
transmitted, privacy policy unchanged.

## 1. Packages to upload — both of them

| | x64 | arm64 |
|---|---|---|
| File | `dist/RORORO-Store-x64-1.23.0.0.msix` | `dist/RORORO-Store-arm64-1.23.0.0.msix` |
| Size | 91.25 MB | 86.15 MB |
| `ProcessorArchitecture` | `x64` | `arm64` |

Shared by both: tag `v1.23.0.0` on `main`, identity `626LabsLLC.RoRoRoBlox`, publisher
`CN=177BCE59-0966-4975-9962-10E36652141F`, `PublisherDisplayName` `626Labs LLC` (no space),
unsigned — Partner Center signs after upload.

## 2. Notes for certification

Paste the fenced block from [`reviewer-letter-1.23.0.0.md`](reviewer-letter-1.23.0.0.md).
Reviewer-only, not shown to users.

## 3. What's new in this version (public — do not skip)

Paste the fenced block from [`whats-new-1.23.0.0.md`](whats-new-1.23.0.0.md) into
**Store listings → [language] → What's new in this version**. This is the public field and a
different field from step 2; both must be filled. This is the step that keeps getting left off.

## 4. Everything else

Unchanged from v1.22.0.0: listing description (`listing-copy.md`), screenshots, IARC rating,
pricing (free), markets, privacy policy URL. Touch nothing.

## 5. After submission

Typical turnaround 24–72h. If the v1.22 submission is somehow still pending, this one replaces it
in the slot. On rejection: bump to 1.24.0.0 (never a non-zero 4th component) and re-run from
playbook Phase 3.
