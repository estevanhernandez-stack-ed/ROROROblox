#!/usr/bin/env python3
"""Export the Store listing translations as one JSON for the verification tool.

Reads docs/store/listing-copy.md (English source of truth), the per-language
listing-copy-<code>.md files, and the current whats-new file, and writes
docs/store/listing-translations.json: every field with the English source beside each
translation, its Partner Center cap, and the generating commit. Este's Gemini/Firebase
verification tool ingests this file; approved corrections come back as edits to the
per-language .md files, and this script is re-run. Run from the repo root:

    python scripts/export-listing-translations.py
"""
from __future__ import annotations

import datetime
import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
STORE = ROOT / "docs" / "store"
LANGUAGES = ["fr", "de", "ru", "pt-br", "pl", "es"]
WHATS_NEW_VERSION = "1.25.0.0"

# (json key, heading prefix in the .md files, per-block char cap, per-line cap)
FIELDS = [
    ("shortDescription", "## Short description", 200, None),
    ("longDescription", "## Long description", 10000, None),
    ("features", "## Product features", None, 200),
    ("whatsNew", "## What's new in this version", 1500, None),
    ("copyright", "## Copyright", 200, None),
    ("trademark", "## Trademark info", None, None),
]


def block_after_heading(text: str, heading_prefix: str, path: Path) -> str:
    """The first fenced block after the heading line starting with heading_prefix."""
    lines = text.splitlines()
    for i, line in enumerate(lines):
        if line.startswith(heading_prefix):
            rest = "\n".join(lines[i + 1 :])
            m = re.search(r"```\n(.*?)```", rest, re.DOTALL)
            if not m:
                raise SystemExit(f"{path.name}: no fenced block under '{heading_prefix}'")
            return m.group(1).strip()
    raise SystemExit(f"{path.name}: heading '{heading_prefix}' not found")


def english_source() -> dict[str, str]:
    listing = (STORE / "listing-copy.md").read_text(encoding="utf-8")
    whats_new = (STORE / f"whats-new-{WHATS_NEW_VERSION}.md").read_text(encoding="utf-8")
    out = {}
    for key, heading, _cap, _line_cap in FIELDS:
        if key == "whatsNew":
            m = re.search(r"```\n(.*?)```", whats_new, re.DOTALL)
            if not m:
                raise SystemExit("whats-new file has no fenced block")
            out[key] = m.group(1).strip()
        else:
            out[key] = block_after_heading(listing, heading, STORE / "listing-copy.md")
    return out


def language_blocks(code: str) -> dict[str, str]:
    path = STORE / f"listing-copy-{code}.md"
    text = path.read_text(encoding="utf-8")
    return {
        key: block_after_heading(text, heading, path)
        for key, heading, _cap, _line_cap in FIELDS
    }


def check_caps(key: str, value: str, cap: int | None, line_cap: int | None, label: str) -> None:
    if cap is not None and len(value) > cap:
        raise SystemExit(f"{label}/{key}: {len(value)} chars exceeds cap {cap}")
    if line_cap is not None:
        for n, line in enumerate(value.splitlines(), 1):
            if len(line) > line_cap:
                raise SystemExit(f"{label}/{key} line {n}: {len(line)} exceeds {line_cap}")


def main() -> None:
    commit = subprocess.run(
        ["git", "rev-parse", "--short", "HEAD"], cwd=ROOT, capture_output=True, text=True
    ).stdout.strip()

    en = english_source()
    translations = {code: language_blocks(code) for code in LANGUAGES}

    fields = []
    for key, _heading, cap, line_cap in FIELDS:
        check_caps(key, en[key], cap, line_cap, "en")
        per_language = {}
        for code in LANGUAGES:
            check_caps(key, translations[code][key], cap, line_cap, code)
            per_language[code] = translations[code][key]
        entry: dict = {"field": key, "en": en[key], "translations": per_language}
        if cap is not None:
            entry["charCap"] = cap
        if line_cap is not None:
            entry["perLineCharCap"] = line_cap
        if key == "whatsNew":
            entry["version"] = WHATS_NEW_VERSION
        if key == "features":
            entry["note"] = "One feature per line; Partner Center takes each line as one entry."
        fields.append(entry)

    payload = {
        "product": "RoRoRo",
        "storeId": "9NMJCS390KWB",
        "sourceLanguage": "en",
        "languages": LANGUAGES,
        "sourceCommit": commit,
        "generatedUtc": datetime.datetime.now(datetime.timezone.utc)
        .replace(microsecond=0)
        .isoformat(),
        "sourceOfTruth": "docs/store/listing-copy*.md — corrections are edits there, then re-run this script",
        "fields": fields,
    }

    out = STORE / "listing-translations.json"
    out.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {out.relative_to(ROOT)} — {len(fields)} fields x {len(LANGUAGES)} languages, commit {commit}")


if __name__ == "__main__":
    main()
