#!/usr/bin/env python3
"""Apply a verdicts JSON from the translation verification tool to the listing files.

The verification tool (see docs/store/translation-verification-prompt.md) reviews
docs/store/listing-translations.json and hands back verdicts whose issues carry
`quote` (the exact problem text) and `suggestedFix` (a minimal replacement). This script
closes the loop: it locates each quote inside the right language's fenced block in
docs/store/listing-copy-<code>.md and swaps in the fix — the repo files stay the source
of truth, the tool never edits them.

    python scripts/apply-translation-verdicts.py verdicts.json            # report only
    python scripts/apply-translation-verdicts.py verdicts.json --apply    # edit + re-export

Rules: only "revise" results are applied; a quote that is missing or ambiguous within its
block is refused (reported, exit 1) rather than guessed at; a fix that would push the block
over its Store cap is refused the same way. --apply re-runs export-listing-translations.py
at the end so the JSON (and its sourceCommit, after you commit) stays in step.
"""
from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
STORE = ROOT / "docs" / "store"

FIELD_HEADINGS = {
    "shortDescription": "## Short description",
    "longDescription": "## Long description",
    "features": "## Product features",
    "whatsNew": "## What's new in this version",
    "copyright": "## Copyright",
    "trademark": "## Trademark info",
}
FIELD_CAPS = {"shortDescription": 200, "whatsNew": 1500}
FIELD_LINE_CAPS = {"features": 200}


def block_span(text: str, heading_prefix: str) -> tuple[int, int]:
    """(start, end) indices of the fenced block's inner text under the heading."""
    heading_at = -1
    pos = 0
    for line in text.splitlines(keepends=True):
        if line.startswith(heading_prefix):
            heading_at = pos + len(line)
            break
        pos += len(line)
    if heading_at < 0:
        raise LookupError(f"heading '{heading_prefix}' not found")
    m = re.compile(r"```\r?\n").search(text, heading_at)
    if not m:
        raise LookupError(f"no fence under '{heading_prefix}'")
    start = m.end()
    end = text.find("```", start)
    if end < 0:
        raise LookupError(f"unterminated fence under '{heading_prefix}'")
    return start, end


def main() -> None:
    args = [a for a in sys.argv[1:] if a != "--apply"]
    apply = "--apply" in sys.argv[1:]
    if len(args) != 1:
        raise SystemExit(__doc__)
    verdicts = json.loads(Path(args[0]).read_text(encoding="utf-8"))

    problems: list[str] = []
    applied = 0
    touched: set[Path] = set()

    for result in verdicts.get("results", []):
        lang, field = result.get("language"), result.get("field")
        verdict = result.get("verdict")
        label = f"{lang}/{field}"
        issues = result.get("issues", [])
        print(f"{label}: {verdict}" + (f" ({len(issues)} issue(s))" if issues else ""))
        if verdict != "revise":
            continue
        if field not in FIELD_HEADINGS:
            problems.append(f"{label}: unknown field")
            continue
        path = STORE / f"listing-copy-{lang}.md"
        if not path.exists():
            problems.append(f"{label}: no file {path.name}")
            continue

        text = path.read_text(encoding="utf-8")
        try:
            start, end = block_span(text, FIELD_HEADINGS[field])
        except LookupError as exc:
            problems.append(f"{label}: {exc}")
            continue

        block = text[start:end]
        for issue in issues:
            quote, fix = issue.get("quote", ""), issue.get("suggestedFix")
            if not quote or fix is None:
                problems.append(f"{label}: issue without quote+suggestedFix — apply by hand")
                continue
            hits = block.count(quote)
            if hits != 1:
                problems.append(
                    f"{label}: quote {'not found' if hits == 0 else f'ambiguous ({hits} hits)'}: "
                    f"{quote[:60]!r}")
                continue
            candidate = block.replace(quote, fix, 1)
            body = candidate.strip()
            cap = FIELD_CAPS.get(field)
            if cap is not None and len(body) > cap:
                problems.append(f"{label}: fix pushes block to {len(body)} > cap {cap}")
                continue
            line_cap = FIELD_LINE_CAPS.get(field)
            if line_cap is not None and any(len(l) > line_cap for l in body.splitlines()):
                problems.append(f"{label}: fix pushes a line over {line_cap}")
                continue
            block = candidate
            applied += 1
            print(f"  fix {'applied' if apply else 'ok (dry-run)'}: {quote[:50]!r} -> {fix[:50]!r}")

        if apply and block != text[start:end]:
            path.write_text(text[:start] + block + text[end:], encoding="utf-8")
            touched.add(path)

    for p in problems:
        print(f"PROBLEM: {p}")
    print(f"{applied} fix(es) {'applied' if apply else 'applicable'}, "
          f"{len(problems)} problem(s), files touched: {len(touched)}")

    if apply and touched:
        subprocess.run([sys.executable, str(ROOT / "scripts" / "export-listing-translations.py")],
                       check=True)
        print("export regenerated — review the diff, commit, and re-upload the JSON for re-verification")
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
