#!/usr/bin/env bash
# ROROROblox — pre-commit local-path guard
# Fails the commit if any staged file contains a Windows user-profile path (c:\Users\<name>\).
# Per pattern kk from wbp-azure: a c:\Users\ reference in committable code breaks CI on every
# machine that isn't yours.
# Triggered as a git pre-commit hook (see .claude/hooks/install.ps1).

set -euo pipefail

red() { printf "\033[31m%s\033[0m\n" "$*" >&2; }
green() { printf "\033[32m%s\033[0m\n" "$*"; }

staged=$(git diff --cached --name-only --diff-filter=ACM)
if [ -z "$staged" ]; then
  exit 0
fi

# Documentation files that legitimately reference user-profile paths (working dir, project root).
# Add to this list with care — only files where the path is the documentation, not a code dependency.
allow=(
  "PROVENANCE.txt"
  "process-notes.md"
  "CLAUDE.md"
  "CONTRIBUTING.md"
  "docs/checklist.md"
  "docs/scope.md"
  "docs/spec.md"
  "docs/prd.md"
  "docs/builder-profile.md"
  "docs/superpowers/specs/2026-05-03-rororoblox-design.md"
  "docs/security-audit-2026-05-04.md"
  "docs/superpowers/plans/2026-05-14-plugincontract-nuget-publish.md"
  ".claude/hooks/pre-commit-local-path-guard.sh"
  ".claude/hooks/README.md"
)

violations=0

while IFS= read -r file; do
  [ -z "$file" ] && continue
  [ ! -f "$file" ] && continue

  # Skip allowlisted files
  is_allowed=0
  for allowed in "${allow[@]}"; do
    if [ "$file" = "$allowed" ]; then
      is_allowed=1
      break
    fi
  done
  [ "$is_allowed" -eq 1 ] && continue

  # Match c:\Users\ or C:/Users/ in any case.
  # -I skips binary files — compiled artifacts (.exe, .pdb, .dll) often have build-path strings
  # baked in by the toolchain that aren't deployment-relevant.
  if grep -IinE "([cC]:\\\\[uU][sS][eE][rR][sS]\\\\|[cC]:/[uU][sS][eE][rR][sS]/)" "$file" >/dev/null 2>&1; then
    red "[local-path-guard] FAIL: $file contains a c:\\Users\\ reference."
    grep -IinE "([cC]:\\\\[uU][sS][eE][rR][sS]\\\\|[cC]:/[uU][sS][eE][rR][sS]/)" "$file" | sed 's/^/  /' >&2
    violations=$((violations + 1))
  fi
done <<< "$staged"

if [ "$violations" -gt 0 ]; then
  red ""
  red "[local-path-guard] $violations file(s) with hardcoded user-profile paths. Commit blocked."
  red "[local-path-guard] Replace with relative paths or env vars (%LOCALAPPDATA%, %USERPROFILE%, $HOME)."
  red "[local-path-guard] If this is intentional documentation only, add the file to the allowlist in"
  red "[local-path-guard] .claude/hooks/pre-commit-local-path-guard.sh."
  exit 1
fi

green "[local-path-guard] clean — no c:\\Users\\ paths in staged files."
exit 0
