# .claude/hooks/

Git pre-commit hooks for ROROROblox. Source-controlled here so the team has the same protective surface; installed into `.git/hooks/pre-commit` via `install.ps1`.

## Hooks

- **`pre-commit-secret-scan.sh`** — fails the commit when any staged file contains a real `.ROBLOSECURITY` cookie pattern, a `.pfx` / `.p12` / `.key` / `.pem` file, or a file starting with the PKCS-12 ASN.1 header (`0x3082...`). The cookie surface is the load-bearing user-secret in this product — once it ships, it ships, and incidents from this class don't get caught later.
- **`pre-commit-local-path-guard.sh`** — fails the commit when any staged file contains `c:\Users\<name>\` references (per pattern kk from the wbp-azure cycle: hardcoded user-profile paths break CI on every machine that isn't yours). Documentation files (CLAUDE.md, PROVENANCE.txt, process notes, Cart artifacts) are allowlisted because the path *is* the documentation in those.

## Install

After `git init` (or on a fresh checkout):

```powershell
powershell -ExecutionPolicy Bypass -File .claude/hooks/install.ps1
```

This writes `.git/hooks/pre-commit` calling both scripts in sequence. Use `-Force` to reinstall after editing the underlying scripts.

## Bypass

Don't `--no-verify`. If a hook fires on a real false positive:

1. Reproduce the failure standalone — `bash .claude/hooks/<hook>.sh` in the repo root.
2. If the regex / heuristic genuinely needs adjustment, edit the script and add a comment explaining the new edge case.
3. Re-run `install.ps1 -Force`.
4. Commit the hook change separately, then your real change.

The cookie-leak and hardcoded-path classes are exactly the kind of thing that ships once and never gets caught later. Keep the discipline.

## When the hooks block legitimate work

- **Test fixtures referencing the cookie format prefix.** Real `.ROBLOSECURITY` cookies start with the prefix `_|WARNING` followed by `:-DO` then `-NOT-SHARE-THIS` (written split here so the hook itself doesn't fire on this doc). Test fixtures must use clearly fake placeholder strings (e.g., `FAKE_COOKIE_FOR_TESTS_ONLY`) so the secret-scan hook doesn't fire on them.
- **Documentation that must reference a user-profile path** (e.g., a screenshot caption explaining where `accounts.dat` lives). Add the file to the allowlist in `pre-commit-local-path-guard.sh`. Audit additions — only files where the path *is* the documentation, not a code dependency.
