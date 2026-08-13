#!/usr/bin/env bash
# ROROROblox — pre-commit secret scan
# Fails the commit if any staged file contains a Roblox session cookie or PFX bytes.
# Triggered as a git pre-commit hook (see .claude/hooks/install.ps1).

set -euo pipefail

red() { printf "\033[31m%s\033[0m\n" "$*" >&2; }
green() { printf "\033[32m%s\033[0m\n" "$*"; }

# SCAN_ALL=1 (CI mode): scan every tracked file instead of the staged diff. The hook protects
# this machine's commits; the CI twin protects commits from machines without the hook installed
# (and --no-verify bypasses). CLAUDE.md: "the pre-commit hook AND CI must fail loud."
if [ "${SCAN_ALL:-0}" = "1" ]; then
  staged=$(git ls-files)
else
  staged=$(git diff --cached --name-only --diff-filter=ACM)
fi
if [ -z "$staged" ]; then
  exit 0
fi

violations=0

while IFS= read -r file; do
  [ -z "$file" ] && continue
  [ ! -f "$file" ] && continue

  # 1. Real .ROBLOSECURITY cookie literal.
  #
  # The prefix ALONE is not the discriminator, and matching on it alone is what turned this check
  # red on main. A real cookie is the warning prefix, then the `.|_` separator, then hundreds of
  # characters of session blob. The two things this used to flag are the capture tool's own secret
  # scanner — which necessarily contains this pattern because it scans for it — and that scanner's
  # self-test fixture, which stops at the human-readable tail and carries no session data at all.
  # Neither is a credential, and a scanner that cannot tell a scanner from a secret blocks every
  # commit to the file whose job is finding secrets.
  #
  # So require the blob. This is deliberately NOT an allowlist: no file is trusted, so a real cookie
  # pasted into capture-ui.ps1 still fails here, which matters because that script is the one that
  # handles cookies. Precision goes up and nothing gets a pass.
  #
  # {20,} is safe margin, not a tight fit — real cookies run to several hundred characters, so an
  # editor tightening this later has room before it risks clipping one. Same reasoning
  # capture-ui.ps1:512-514 already applies to its webhook floor.
  #
  # NOTE the capture tool's own pattern (capture-ui.ps1:516) stays loose on purpose and the two are
  # meant to differ: this one guards COMMITS and wants precision so fixtures do not block the repo;
  # that one guards SCREENSHOTS and wants recall, because a partial cookie rendered on screen is
  # still worth refusing to capture.
  #
  # -a, NOT -I: scan binary files too. Skipping them left a hole big enough to drive an index
  # through — .gitnexus/lbug is a 77 MB tracked database built by slurping the working tree, and it
  # cleared this scanner while containing 63 occurrences of the string ROBLOSECURITY. Those turned
  # out to be the identifier from source, not a credential, but the scanner did not establish that;
  # it never looked. A binary that ingests the repo is exactly where a leaked cookie would land, and
  # it was the one place guaranteed not to be checked.
  #
  # Safe to widen because the pattern is already precise by construction (see above): prefix AND
  # separator AND a 20+ char blob. That does not occur by accident in compiled output.
  if grep -qaE "_\|WARNING:-DO-NOT-SHARE-THIS.*\.\|_[A-Za-z0-9_%+/=-]{20,}" "$file"; then
    red "[secret-scan] FAIL: $file contains a real .ROBLOSECURITY cookie — prefix plus session blob."
    violations=$((violations + 1))
  fi

  # 2. Private-key bundle by extension.
  case "$file" in
    *.pfx|*.p12|*.key|*.pem)
      red "[secret-scan] FAIL: $file is a private-key bundle. .gitignore must cover this."
      violations=$((violations + 1))
      ;;
  esac

  # 3. PKCS-12 ASN.1 header bytes inside a non-key-extension file.
  # No upper size bound. There used to be a 10 MB cap here, which meant the check skipped exactly
  # the files most worth checking: .gitnexus/lbug is 77 MB and cleared it on size alone. The cap
  # bought nothing anyway — this reads four bytes, so a 77 MB file costs the same as a 4 KB one.
  size=$(stat -c%s "$file" 2>/dev/null || stat -f%z "$file" 2>/dev/null || echo 0)
  if [ "$size" -gt 0 ]; then
    if head -c 4 "$file" 2>/dev/null | xxd -p 2>/dev/null | grep -qE "^3082"; then
      red "[secret-scan] FAIL: $file starts with PKCS-12 ASN.1 header (0x3082...) — looks like a private key blob."
      violations=$((violations + 1))
    fi
  fi
done <<< "$staged"

if [ "$violations" -gt 0 ]; then
  red ""
  red "[secret-scan] $violations violation(s) found. Commit blocked."
  red "[secret-scan] If a finding is a documented placeholder (e.g., a clearly-fake test fixture),"
  red "[secret-scan] discuss before bypassing — do NOT --no-verify silently."
  exit 1
fi

green "[secret-scan] clean — no Roblox cookies or PFX bytes in staged files."
exit 0
