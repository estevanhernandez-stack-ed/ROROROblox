# Submission packet — v1.25.0.0

Everything needed to complete the Partner Center submission in one pass, in the order the form
asks for it. Internal record; nothing here is pasted into Partner Center except where marked.

---

## Read this first — no manifest delta; the disclosure is network-side

Certification last saw **v1.24.0.0**; this is **v1.25.0.0**. The manifest is **unchanged** from
the certified version (after last cycle's manifest-delta letter, the reviewer letter says so
explicitly). What changed is network behaviour: two optional, off-by-default push services
(`api.pushover.net`, `ntfy.sh`/user-set server) that the user configures with their own
credentials. The letter leads with it, the privacy policy already names both, and the standing
"network behaviour is unchanged" boilerplate is retired for good. If certification asks
anything, it will be about the push services, and the answer is in the letter's first section.

## 1. Packages to upload — both of them

| | x64 | arm64 |
|---|---|---|
| File | `dist/RORORO-Store-x64-1.25.0.0.msix` | `dist/RORORO-Store-arm64-1.25.0.0.msix` |
| Size | 106.09 MB | 99.92 MB |
| `ProcessorArchitecture` | `x64` | `arm64` |

Shared by both: tag `v1.25.0.0` on `main`, identity `626LabsLLC.RoRoRoBlox`, publisher
`CN=177BCE59-0966-4975-9962-10E36652141F`, `PublisherDisplayName` `626Labs LLC` (no space),
unsigned — Partner Center signs after upload.

## 2. Notes for certification

Paste the fenced block from [`reviewer-letter-1.25.0.0.md`](reviewer-letter-1.25.0.0.md).
Reviewer-only, not shown to users. It retires the "network behaviour is unchanged" line and
pre-answers the push-service questions.

## 3. What's new in this version (public — do not skip)

Paste the fenced block from [`whats-new-1.25.0.0.md`](whats-new-1.25.0.0.md) into
**Store listings → [language] → What's new in this version**. Different field from step 2; both
must be filled.

## 4. Listing changes this submission

- **Product features** — ADD these three entries (cap is 20; the live set holds 14):

  ```
  Phone alerts through Pushover or ntfy — an alt drops and your phone buzzes, even with Discord closed
  ```

  ```
  Uptime marks — an all-good buzz every two hours while your accounts run, so silence means something's wrong
  ```

  ```
  Alerts fan out — desktop, Discord channels, and your phone in any mix, per alert
  ```

- **Unchanged:** short description, long description, screenshots (the carousel shows no
  Settings page, and no carousel surface changed this cycle), IARC rating, pricing (free),
  markets, privacy policy URL (the policy TEXT already names both push services and is live —
  GitHub Pages rebuilds from `main` on push).

## 5. After submission

Typical turnaround 24–72h. On rejection: bump to 1.26.0.0 (never a non-zero 4th component) and
re-run from playbook Phase 3, quoting the clause number per
[`listing-copy.md`](listing-copy.md) → response protocol.
