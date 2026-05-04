#!/usr/bin/env bash
# ROROROblox — pre-commit secret scan
# Fails the commit if any staged file contains a Roblox session cookie or PFX bytes.
# Triggered as a git pre-commit hook (see .claude/hooks/install.ps1).

set -euo pipefail

red() { printf "\033[31m%s\033[0m\n" "$*" >&2; }
green() { printf "\033[32m%s\033[0m\n" "$*"; }

staged=$(git diff --cached --name-only --diff-filter=ACM)
if [ -z "$staged" ]; then
  exit 0
fi

violations=0

while IFS= read -r file; do
  [ -z "$file" ] && continue
  [ ! -f "$file" ] && continue

  # 1. Real .ROBLOSECURITY cookie literal — they always start with this exact prefix.
  # -I skips binary files (cookie strings are text; binary key blobs caught separately below).
  if grep -qIE "_\|WARNING:-DO-NOT-SHARE-THIS" "$file"; then
    red "[secret-scan] FAIL: $file contains a real .ROBLOSECURITY cookie pattern."
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
  size=$(stat -c%s "$file" 2>/dev/null || stat -f%z "$file" 2>/dev/null || echo 0)
  if [ "$size" -gt 0 ] && [ "$size" -lt 10000000 ]; then
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
